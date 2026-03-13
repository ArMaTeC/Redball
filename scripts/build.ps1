#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [switch]$SkipTests,

    [Parameter()]
    [switch]$SkipLint,

    [Parameter()]
    [switch]$SkipSecurity,

    [Parameter()]
    [switch]$SkipWPF,

    [Parameter()]
    [switch]$SkipMSI,

    [Parameter()]
    [switch]$BuildAll,

    [Parameter()]
    [string]$OutputPath = './dist',

    [Parameter()]
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

# Resolve project root (parent of scripts directory)
$script:ProjectRoot = Split-Path $PSScriptRoot -Parent
$script:DistPath = Join-Path $ProjectRoot $OutputPath

# Import version from version.txt or WPF project if not specified
if (-not $Version) {
    $versionFilePath = Join-Path $PSScriptRoot 'version.txt'
    if (Test-Path $versionFilePath) {
        $Version = (Get-Content $versionFilePath -Raw).Trim()
    }
    else {
        # Try to read from WPF project
        $wpfProjectPath = Join-Path $ProjectRoot 'src' 'Redball.UI.WPF' 'Redball.UI.WPF.csproj'
        if (Test-Path $wpfProjectPath) {
            $versionMatch = Get-Content $wpfProjectPath | Select-String -Pattern '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>'
            if ($versionMatch) {
                $Version = $versionMatch.Matches.Groups[1].Value
            }
        }
    }
}
if (-not $Version) {
    $Version = '2.1.19'
}

$script:Version = $Version

function Write-HostSafe {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'Build script requires console output for user feedback')]
    param(
        [Parameter(Mandatory, Position = 0)]
        [object]$Object,
        [System.ConsoleColor]$ForegroundColor,
        [switch]$NoNewline
    )
    if ($ForegroundColor) {
        Write-Host $Object -ForegroundColor $ForegroundColor -NoNewline:$NoNewline
    }
    else {
        Write-Host $Object -NoNewline:$NoNewline
    }
}

function Write-BuildHeader {
    param([string]$Message)
    Write-HostSafe "`n=== $Message ===" -ForegroundColor Cyan
}

function Write-BuildStep {
    param([string]$Message)
    Write-HostSafe "  → $Message" -ForegroundColor Yellow
}

function Write-BuildSuccess {
    param([string]$Message)
    Write-HostSafe "  ✓ $Message" -ForegroundColor Green
}

function Write-BuildError {
    param([string]$Message)
    Write-HostSafe "  ✗ $Message" -ForegroundColor Red
}

function Test-ModuleInstalled {
    param([string]$Name, [string]$Version = '')
    $module = Get-Module -ListAvailable -Name $Name | Select-Object -First 1
    if (-not $module) {
        return $false
    }
    if ($Version) {
        return $module.Version -eq $Version
    }
    return $true
}

function Install-BuildModule {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        
        [string]$RequiredVersion = '',
        
        [switch]$SkipPublisherCheck
    )
    
    $installParams = @{
        Name  = $Name
        Force = $true
        Scope = 'CurrentUser'
    }
    if ($RequiredVersion) {
        $installParams['RequiredVersion'] = $RequiredVersion
    }
    if ($SkipPublisherCheck) {
        $installParams['SkipPublisherCheck'] = $true
    }
    
    Write-BuildStep "Installing module: $Name"
    Install-Module @installParams
}

#region Build Steps

function Step-RestoreDependency {
    Write-BuildHeader "Restoring Dependencies"
    
    # Ensure Pester is available
    if (-not (Test-ModuleInstalled -Name 'Pester' -Version '5.5.0')) {
        Install-BuildModule -Name 'Pester' -RequiredVersion '5.5.0' -SkipPublisherCheck
    }
    else {
        Write-BuildSuccess "Pester 5.5.0 already installed"
    }
    Import-Module Pester -RequiredVersion 5.5.0 -Force
    
    # Ensure PSScriptAnalyzer is available
    if (-not (Test-ModuleInstalled -Name 'PSScriptAnalyzer')) {
        Install-BuildModule -Name 'PSScriptAnalyzer' -SkipPublisherCheck
    }
    else {
        Write-BuildSuccess "PSScriptAnalyzer already installed"
    }
    
    # Check for .NET SDK if building WPF
    if (-not $SkipWPF) {
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $dotnet) {
            throw ".NET SDK not found. Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download"
        }
        Write-BuildSuccess ".NET SDK found: $(dotnet --version)"
    }
}

function Step-ValidateJson {
    Write-BuildHeader "Validating JSON Configuration Files"
    
    $jsonFiles = Get-ChildItem -Path $ProjectRoot -Filter '*.json' -File
    $invalidFiles = @()
    
    foreach ($file in $jsonFiles) {
        try {
            $null = Get-Content $file.FullName -Raw | ConvertFrom-Json -ErrorAction Stop
            Write-BuildSuccess "$($file.Name) is valid JSON"
        }
        catch {
            Write-BuildError "$($file.Name) is invalid: $_"
            $invalidFiles += $file.Name
        }
    }
    
    if ($invalidFiles.Count -gt 0) {
        throw "Invalid JSON files found: $($invalidFiles -join ', ')"
    }
}

function Step-RunSecurityScan {
    Write-BuildHeader "Running Security Scan"
    
    $issues = @()
    $psFiles = Get-ChildItem -Path $ProjectRoot -Filter '*.ps1' -Recurse -File | 
    Where-Object { $_.Name -notmatch 'Tests\.ps1$|build\.ps1$' }
    
    Write-BuildStep "Scanning $($psFiles.Count) PS1 files..."
    
    # Check for hardcoded credentials
    $content = $psFiles |
    Select-String -Pattern '(?i)\b(password|secret|apikey|api_key|token)\s*=\s*["\x27][^"\x27]+["\x27]' |
    Where-Object { $_ -notmatch 'example|placeholder|changeme|TimestampServer|Hotkey|ShortcutKey' }
    
    if ($content) {
        $issues += "Potential hardcoded credentials found"
        $content | ForEach-Object { Write-Warning $_ }
    }
    
    # Check for Invoke-Expression usage
    $invokeExpr = $psFiles |
    Select-String -Pattern 'Invoke-Expression|iex\s'
    
    if ($invokeExpr) {
        $issues += "Invoke-Expression usage found - review for security"
        $invokeExpr | ForEach-Object { Write-Warning $_ }
    }
    
    if ($issues.Count -gt 0) {
        throw "Security issues found: $($issues -join ', ')"
    }
    
    Write-BuildSuccess "Security scan passed"
}

function Step-RunLinting {
    Write-BuildHeader "Running PSScriptAnalyzer"
    
    Import-Module PSScriptAnalyzer -Force
    
    $results = Invoke-ScriptAnalyzer -Path $PSScriptRoot -Severity Warning, Error
    
    if ($results) {
        foreach ($result in $results) {
            $color = if ($result.Severity -eq 'Error') { 'Red' } else { 'Yellow' }
            Write-HostSafe "  [$($result.Severity)] $($result.RuleName) at line $($result.Line)" -ForegroundColor $color
            Write-HostSafe "    → $($result.Message)" -ForegroundColor Gray
        }
        $errors = $results | Where-Object { $_.Severity -eq 'Error' }
        if ($errors) {
            throw "PSScriptAnalyzer found errors!"
        }
        $warnings = $results | Where-Object { $_.Severity -eq 'Warning' }
        Write-HostSafe "  ⚠ $($warnings.Count) warnings (non-blocking)" -ForegroundColor Yellow
    }
    else {
        Write-BuildSuccess "No issues found"
    }
}

function Step-RunTest {
    Write-BuildHeader "Running Tests"
    
    $testPath = Join-Path $ProjectRoot 'tests' 'Redball.Tests.csproj'
    if (-not (Test-Path $testPath)) {
        Write-Warning "Test project not found: $testPath"
        return
    }
    
    Write-HostSafe "Running dotnet test..." -ForegroundColor Yellow
    dotnet test $testPath --configuration $Configuration --verbosity normal
    
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed"
    }
    
    Write-BuildSuccess "All tests passed"
}
function Get-FileLockInfo {
    param([string]$FilePath)
    
    if (-not (Test-Path $FilePath)) {
        return $null
    }
    
    # Method 1: Check loaded modules in running processes
    $lockingProcesses = @()
    foreach ($proc in Get-Process) {
        try {
            $modules = $proc.Modules | Where-Object { $_.FileName -like "*$FilePath*" -or $_.FileName -eq $FilePath }
            if ($modules) {
                $lockingProcesses += [PSCustomObject]@{
                    ProcessName = $proc.ProcessName
                    ProcessId   = $proc.Id
                    Path        = $proc.Path
                    Method      = 'Module'
                }
            }
        }
        catch {
            Write-Verbose "Failed to enumerate modules for process $($proc.Name): $_"
        }
    }
    
    # Method 2: Try to open file exclusively to confirm lock
    try {
        $stream = [System.IO.File]::Open($FilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
        $stream.Close()
        $stream.Dispose()
    }
    catch {
        # File is locked - try to find process using WMI
        try {
            $fileName = Split-Path $FilePath -Leaf
            $wmiProcesses = Get-CimInstance -ClassName Win32_Process -Filter "CommandLine LIKE '%$fileName%'" -ErrorAction SilentlyContinue
            foreach ($wmiProc in $wmiProcesses) {
                if ($wmiProc.ProcessId -ne $PID) {
                    $lockingProcesses += [PSCustomObject]@{
                        ProcessName = $wmiProc.Name
                        ProcessId   = $wmiProc.ProcessId
                        Path        = $wmiProc.ExecutablePath
                        Method      = 'CIM'
                    }
                }
            }
        }
        catch {
            Write-Verbose "Failed to query WMI: $_"
        }
    }
    return $lockingProcesses | Select-Object -Unique -Property ProcessName, ProcessId, Path, Method
}

function Stop-LockingProcess {
    [CmdletBinding(SupportsShouldProcess)]
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseSingularNouns', '', Justification = 'Function name is clear and intentional')]
    param([string]$FilePath)

    Write-BuildStep "Detecting processes locking file..."
    $lockers = Get-FileLockInfo -FilePath $FilePath
    
    if (-not $lockers) {
        # Fallback: Check common processes that might lock DLLs
        $commonProcesses = @('Redball.UI.WPF', 'dotnet', 'MSBuild', 'VBCSCompiler', 'devenv', 'explorer')
        foreach ($procName in $commonProcesses) {
            $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
            if ($procs) {
                $lockers = $procs | Select-Object @{N = 'ProcessName'; E = { $_.ProcessName } }, 
                @{N = 'ProcessId'; E = { $_.Id } }, 
                @{N = 'Path'; E = { $_.Path } }, 
                @{N = 'Method'; E = { 'Heuristic' } }
                break
            }
        }
    }
    
    if ($lockers) {
        Write-HostSafe "`n  Detected potential locking processes:" -ForegroundColor Yellow
        $lockers | Format-Table -AutoSize | Out-String | ForEach-Object { Write-HostSafe $_ -ForegroundColor Yellow }

        Write-BuildStep "Attempting to terminate locking processes..."
        foreach ($locker in $lockers) {
            try {
                $proc = Get-Process -Id $locker.ProcessId -ErrorAction SilentlyContinue
                if ($proc) {
                    if ($PSCmdlet.ShouldProcess($locker.ProcessName, "Terminate Process")) {
                        $proc | Stop-Process -Force -ErrorAction Stop
                        Write-BuildSuccess "Terminated: $($locker.ProcessName) (PID: $($locker.ProcessId))"
                    }
                }
            }
            catch {
                Write-Warning "Failed to terminate $($locker.ProcessName): $_"
            }
        }
        
        Start-Sleep -Seconds 3
        
        # Verify lock is released
        try {
            $testStream = [System.IO.File]::Open($FilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
            $testStream.Close()
            $testStream.Dispose()
            Write-BuildSuccess "File lock released"
            return $true
        }
        catch {
            Write-BuildError "File is still locked. Manual intervention required."
            Write-HostSafe "`n  Options to resolve:" -ForegroundColor Cyan
            Write-HostSafe "    1. Run: handle.exe $FilePath  (from Sysinternals)" -ForegroundColor Gray
            Write-HostSafe "    2. Close Visual Studio or other IDE instances" -ForegroundColor Gray
            Write-HostSafe "    3. Restart Windows Explorer or logoff/login" -ForegroundColor Gray
            Write-HostSafe "    4. Reboot computer" -ForegroundColor Gray
            return $false
        }
    }
    else {
        Write-Warning "Could not identify locking process"
        return $false
    }
}

function Step-BuildWPF {
    Write-BuildHeader "Building WPF Application"
    
    $solutionPath = Join-Path $ProjectRoot 'Redball.v3.sln'
    $projectPath = Join-Path $ProjectRoot 'src' 'Redball.UI.WPF' 'Redball.UI.WPF.csproj'
    $publishDir = Join-Path $DistPath 'wpf-publish'
    
    if (-not (Test-Path $projectPath)) {
        Write-Warning "WPF project not found: $projectPath"
        return
    }
    
    # Detect and resolve file locks before building
    $objDir = Join-Path $ProjectRoot 'src' 'Redball.UI.WPF' 'obj' $Configuration 'net8.0-windows' 'win-x64'
    $dllPath = Join-Path $objDir 'Redball.UI.WPF.dll'
    
    if (Test-Path $dllPath) {
        try {
            $testStream = [System.IO.File]::Open($dllPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
            $testStream.Close()
            $testStream.Dispose()
        }
        catch {
            Write-Warning "DLL file is locked by another process"
            $resolved = Stop-LockingProcess -FilePath $dllPath
            if (-not $resolved) {
                throw "Cannot resolve file lock. Please close locking applications manually."
            }
        }
    }
    
    # Also try to stop known processes
    $runningProcesses = Get-Process -Name 'Redball.UI.WPF', 'Redball' -ErrorAction SilentlyContinue
    if ($runningProcesses) {
        Write-BuildStep "Stopping running Redball processes..."
        $runningProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Write-BuildSuccess "Processes stopped"
    }
    
    # Restore packages
    Write-BuildStep "Restoring NuGet packages..."
    dotnet restore $solutionPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed"
    }
    Write-BuildSuccess "Packages restored"
    
    # Build solution
    Write-BuildStep "Building solution ($Configuration)..."
    dotnet build $solutionPath --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed"
    }
    Write-BuildSuccess "Build completed"
    
    # Publish single-file executable
    Write-BuildStep "Publishing single-file executable..."
    dotnet publish $projectPath `
        --configuration $Configuration `
        --output $publishDir `
        --self-contained false `
        --runtime win-x64 `
        --property:PublishSingleFile=true `
        --property:PublishTrimmed=false `
        --property:EnableCompressionInSingleFile=false
    
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed"
    }
    Write-BuildSuccess "Published to: $publishDir"
    
    # List output
    $exePath = Join-Path $publishDir 'Redball.UI.WPF.exe'
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        Write-HostSafe "  Executable: $($fileInfo.Name) ($([math]::Round($fileInfo.Length / 1MB, 2)) MB)" -ForegroundColor Gray
    }
}

function Step-BuildMSI {
    Write-BuildHeader "Building MSI Installer"
    
    # Accept WiX v7 OSMF EULA
    $env:WIX_OSMF_EULA_ACCEPTED = '1'
    
    $msiScript = Join-Path $ProjectRoot 'installer' 'Build-MSI.ps1'
    if (-not (Test-Path $msiScript)) {
        Write-Warning "MSI build script not found: $msiScript"
        return
    }
    
    & $msiScript -Configuration $Configuration -Version $Version
    
    if ($LASTEXITCODE -ne 0) {
        throw "MSI build failed"
    }
    
    $msiPath = Join-Path $DistPath "Redball-$Version.msi"
    if (Test-Path $msiPath) {
        $fileInfo = Get-Item $msiPath
        Write-BuildSuccess "MSI created: $($fileInfo.Name) ($([math]::Round($fileInfo.Length / 1MB, 2)) MB)"
    }
}

function Step-CreateReleasePackage {
    Write-BuildHeader "Creating Release Artifacts"
    
    # MSI now contains everything - just verify it exists and show summary
    $msiPath = Join-Path $DistPath "Redball-$Version.msi"
    if (-not (Test-Path $msiPath)) {
        throw "MSI not found: $msiPath"
    }
    
    $msiInfo = Get-Item $msiPath
    Write-BuildSuccess "Release MSI ready: $($msiInfo.Name) ($([math]::Round($msiInfo.Length / 1MB, 2)) MB)"
    Write-HostSafe "  Location: $msiPath" -ForegroundColor Gray
    Write-HostSafe "  Contains: WPF Application + Core Files + Configuration" -ForegroundColor Gray
    
    # Show bundle if it exists
    $bundlePath = Join-Path $DistPath "Redball-Setup-$Version.exe"
    if (Test-Path $bundlePath) {
        $bundleInfo = Get-Item $bundlePath
        Write-BuildSuccess "Release Setup ready: $($bundleInfo.Name) ($([math]::Round($bundleInfo.Length / 1MB, 2)) MB)"
        Write-HostSafe "  Location: $bundlePath" -ForegroundColor Gray
        Write-HostSafe "  Contains: MSI + .NET 8 Desktop Runtime auto-download" -ForegroundColor Gray
    }
}

#endregion

# Main Build Process
try {
    Write-HostSafe @'
  _____          _ _           _ _   ____        _ _     _ 
 |  __ \        | | |         | | | |  _ \      (_) |   | |
 | |__) |___  __| | |__   __ _| | | | |_) |_   _ _| | __| |
 |  _  // _ \/ _` | '_ \ / _` | | | |  _ <| | | | | |/ _` |
 | | \ \  __/ (_| | |_) | (_| | | | | |_) | |_| | | | (_| |
 |_|  \_\___|\__,_|_.__/ \__,_|_|_| |____/ \__,_|_|_|\__,_|
'@
    Write-HostSafe "  Building Redball v$Version ($Configuration)`n" -ForegroundColor Cyan
    
    # Ensure dist directory exists
    if (-not (Test-Path $DistPath)) {
        New-Item -ItemType Directory -Path $DistPath -Force | Out-Null
    }
    
    # Always run these
    Step-RestoreDependency
    Step-ValidateJson
    
    # Security scan
    if (-not $SkipSecurity) {
        Step-RunSecurityScan
    }
    else {
        Write-HostSafe "  Skipping security scan ( -SkipSecurity )" -ForegroundColor Yellow
    }
    
    # Lint
    if (-not $SkipLint) {
        Step-RunLinting
    }
    else {
        Write-HostSafe "  Skipping linting ( -SkipLint )" -ForegroundColor Yellow
    }
    
    # Tests
    if (-not $SkipTests) {
        Step-RunTest
    }
    else {
        Write-HostSafe "  Skipping tests ( -SkipTests )" -ForegroundColor Yellow
    }
    
    # WPF Build (required before MSI)
    if (-not $SkipWPF) {
        Step-BuildWPF
    }
    else {
        Write-HostSafe "  Skipping WPF build ( -SkipWPF )" -ForegroundColor Yellow
    }
    
    # MSI Build (requires WPF files)
    if (-not $SkipMSI) {
        if ($SkipWPF) {
            Write-Warning "WPF build skipped - MSI will use previously built files if available"
        }
        Step-BuildMSI
    }
    else {
        Write-HostSafe "  Skipping MSI build ( -SkipMSI )" -ForegroundColor Yellow
    }
    
    # Finalize release (MSI is now the primary artifact)
    if (-not $SkipMSI -or $BuildAll) {
        Step-CreateReleasePackage
    }
    
    Write-HostSafe "`n══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-HostSafe "  BUILD SUCCEEDED" -ForegroundColor Green
    Write-HostSafe "══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-HostSafe "  Version:  $Version" -ForegroundColor Gray
    Write-HostSafe "  Config:   $Configuration" -ForegroundColor Gray
    Write-HostSafe "  Output:   $((Resolve-Path $DistPath).Path)" -ForegroundColor Gray
    Write-HostSafe "══════════════════════════════════════════════════════════`n" -ForegroundColor Green
    
    exit 0
}
catch {
    Write-HostSafe "`n══════════════════════════════════════════════════════════" -ForegroundColor Red
    Write-HostSafe "  BUILD FAILED" -ForegroundColor Red
    Write-HostSafe "══════════════════════════════════════════════════════════" -ForegroundColor Red
    Write-HostSafe "  Error: $_" -ForegroundColor Red
    Write-HostSafe "══════════════════════════════════════════════════════════`n" -ForegroundColor Red
    exit 1
}
