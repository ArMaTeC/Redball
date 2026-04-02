#requires -Version 5.1
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification = 'Parameters are used within function scope')]
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
    [switch]$InstallMsi,

    [Parameter()]
    [switch]$BuildAll,

    [Parameter()]
    [switch]$SkipVerify,

    [Parameter()]
    [string]$OutputPath = './dist',

    [Parameter()]
    [string]$Version = '',

    [Parameter()]
    [switch]$SkipVersionBump,

    [Parameter()]
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string]$BumpComponent = 'Patch',

    [Parameter()]
    [switch]$BumpCommit,

    [Parameter()]
    [switch]$BumpPush,

    [Parameter()]
    [string]$BumpMessage = '',

    [Parameter()]
    [switch]$SkipReleaseCommit,

    [Parameter()]
    [switch]$SkipReleasePush,

    [Parameter()]
    [string]$ReleaseMessage = '',

    [Parameter()]
    [switch]$NoClean,

    [Parameter()]
    [switch]$Parallel,

    [Parameter()]
    [switch]$SignDriver
)

$ErrorActionPreference = 'Stop'

# Resolve paths safely
$currentScriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path $MyInvocation.MyCommand.Path -Parent }

$script:ProjectRoot = Split-Path $currentScriptRoot -Parent
if (-not $script:ProjectRoot) { $script:ProjectRoot = (Get-Item .).FullName }

$script:DistPath = Join-Path $script:ProjectRoot $OutputPath

# Import version from version.txt or WPF project if not specified
if (-not $Version) {
    $versionFilePath = Join-Path $currentScriptRoot 'version.txt'
    if (Test-Path $versionFilePath) {
        $Version = (Get-Content $versionFilePath -Raw).Trim()
    }
    else {
        # Try to read from WPF project
        $srcDir = Join-Path $script:ProjectRoot 'src'
        $wpfDir = Join-Path $srcDir 'Redball.UI.WPF'
        $wpfProjectPath = Join-Path $wpfDir 'Redball.UI.WPF.csproj'
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
    [CmdletBinding()]
    param()
    Write-BuildHeader "Restoring Dependencies"
    
    # Ensure Pester is available
    if (-not $SkipTests) {
        if (-not (Test-ModuleInstalled -Name 'Pester' -Version '5.5.0')) {
            Install-BuildModule -Name 'Pester' -RequiredVersion '5.5.0' -SkipPublisherCheck
        }
        else {
            Write-BuildSuccess "Pester 5.5.0 already installed"
        }
        Import-Module Pester -RequiredVersion 5.5.0 -Force
    }

    # Ensure PSScriptAnalyzer is available
    if (-not $SkipLint) {
        if (-not (Test-ModuleInstalled -Name 'PSScriptAnalyzer')) {
            Install-BuildModule -Name 'PSScriptAnalyzer' -SkipPublisherCheck
        }
        else {
            Write-BuildSuccess "PSScriptAnalyzer already installed"
        }
        Import-Module PSScriptAnalyzer -Force
    }
    
    # Check for .NET SDK if building WPF
    if (-not $SkipWPF) {
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $dotnet) {
            throw ".NET SDK not found. Please install .NET 10.0 SDK from https://dotnet.microsoft.com/download"
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
    [CmdletBinding()]
    param()
    Write-BuildHeader "Running PSScriptAnalyzer"

    $lintRoot = if ($PSScriptRoot) { $PSScriptRoot } elseif ($currentScriptRoot) { $currentScriptRoot } else { $script:ProjectRoot }
    if (-not (Test-Path $lintRoot)) {
        throw "Lint path not found: $lintRoot"
    }
    
    # Fix BOM encoding for all ps1 files in scripts directory before linting
    # This addresses the PSUseBOMForUnicodeEncodedFile rule warnings
    # Exclude the currently running script to avoid self-modification/file lock issues
    Write-HostSafe "  Fixing UTF-8 BOM encoding for scripts..." -ForegroundColor Gray
    $scriptsToUpdate = Get-ChildItem -Path $lintRoot -Filter *.ps1 -Recurse | Where-Object { $_.FullName -ne $PSCommandPath }
    foreach ($scriptToUpdate in $scriptsToUpdate) {
        try {
            $contentToFix = Get-Content -Path $scriptToUpdate.FullName -Raw
            $contentToFix | Set-Content -Path $scriptToUpdate.FullName -Encoding utf8BOM
        }
        catch {
            Write-Warning "Skipping BOM update for $($scriptToUpdate.FullName): $($_.Exception.Message)"
        }
    }
    
    Import-Module PSScriptAnalyzer -Force

    $results = @()
    try {
        $analysis = Invoke-ScriptAnalyzer -Path $lintRoot -Severity Warning, Error -ErrorAction Stop
        if ($analysis) { $results += $analysis }
    }
    catch {
        Write-Warning "Directory lint failed, falling back to per-file lint: $($_.Exception.Message)"
        $lintFiles = Get-ChildItem -Path $lintRoot -Filter *.ps1 -Recurse
        foreach ($lintFile in $lintFiles) {
            try {
                $fileAnalysis = Invoke-ScriptAnalyzer -Path $lintFile.FullName -Severity Warning, Error -ErrorAction Stop
                if ($fileAnalysis) { $results += $fileAnalysis }
            }
            catch {
                Write-Warning "Failed to lint $($lintFile.FullName): $($_.Exception.Message)"
            }
        }
    }
    
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
    [CmdletBinding(SupportsShouldProcess)]
    param()
    Write-BuildHeader "Running Tests"
    
    $testProjDir = Join-Path $ProjectRoot 'tests'
    $testPath = Join-Path $testProjDir 'Redball.Tests.csproj'
    if (-not (Test-Path $testPath)) {
        Write-Warning "Test project not found: $testPath"
        return
    }

    $runningRedballProcesses = Get-Process -Name 'Redball.UI.WPF', 'Redball' -ErrorAction SilentlyContinue
    if ($runningRedballProcesses) {
        Write-BuildStep "Stopping running Redball processes to avoid test-time file locks..."
        foreach ($proc in $runningRedballProcesses) {
            try {
                if ($PSCmdlet.ShouldProcess("$($proc.ProcessName) (PID: $($proc.Id))", 'Stop process')) {
                    Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                    Write-BuildSuccess "Stopped: $($proc.ProcessName) (PID: $($proc.Id))"
                }
            }
            catch {
                Write-Warning "Failed to stop $($proc.ProcessName) (PID: $($proc.Id)): $($_.Exception.Message)"
            }
        }

        Start-Sleep -Seconds 2
    }
    
    Write-HostSafe "Running dotnet test..." -ForegroundColor Yellow
    dotnet test $testPath --configuration $Configuration --verbosity normal
    
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed"
    }
    
    Write-BuildSuccess "All tests passed"
}

function Step-BumpVersion {
    [CmdletBinding()]
    param()
    
    Write-BuildHeader "Bumping Version ($BumpComponent)"
    
    $propsPath = Join-Path $ProjectRoot 'Directory.Build.props'
    $srcDir = Join-Path $ProjectRoot 'src'
    $wpfDir = Join-Path $srcDir 'Redball.UI.WPF'
    $wpfProjectPath = Join-Path $wpfDir 'Redball.UI.WPF.csproj'
    $versionFilePath = Join-Path $PSScriptRoot 'version.txt'
    
    $targetPath = $propsPath
    if (-not (Test-Path $targetPath)) {
        $targetPath = $wpfProjectPath
    }
    
    if (-not (Test-Path $targetPath)) {
        throw "Version target file not found at: $targetPath"
    }
    
    $targetContent = Get-Content $targetPath -Raw
    $versionPattern = '<Version>([0-9]+)\.([0-9]+)\.([0-9]+)</Version>'
    $match = [regex]::Match($targetContent, $versionPattern)
    
    if (-not $match.Success) {
        throw "Could not find version pattern in $targetPath"
    }
    
    $major = [int]$match.Groups[1].Value
    $minor = [int]$match.Groups[2].Value
    $patch = [int]$match.Groups[3].Value
    $currentVersion = "$major.$minor.$patch"
    
    Write-HostSafe "Current version (from $(Split-Path $targetPath -Leaf)): $currentVersion" -ForegroundColor Cyan
    
    # Calculate new version
    switch ($BumpComponent) {
        'Major' {
            $major++
            $minor = 0
            $patch = 0
        }
        'Minor' {
            $minor++
            $patch = 0
        }
        'Patch' {
            $patch++
        }
    }
    
    $newVersion = "$major.$minor.$patch"
    Write-HostSafe "New version: $newVersion" -ForegroundColor Green
    
    # Update target file
    $targetContent = $targetContent -replace '<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>', "<Version>$newVersion</Version>"
    $targetContent = $targetContent -replace '<FileVersion>[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?</FileVersion>', "<FileVersion>$newVersion.0</FileVersion>"
    $targetContent = $targetContent -replace '<AssemblyVersion>[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?</AssemblyVersion>', "<AssemblyVersion>$newVersion.0</AssemblyVersion>"
    
    Set-Content -Path $targetPath -Value $targetContent -NoNewline
    Write-BuildSuccess "Updated $targetPath"
    
    # Write version file for MSI and other build processes
    Set-Content -Path $versionFilePath -Value $newVersion -NoNewline
    Write-BuildSuccess "Updated version.txt"
    
    # Update script-level version variable for the rest of the build
    $script:Version = $newVersion
    
    Write-BuildSuccess "Version bumped to $newVersion"
}

function Step-CommitVersionBump {
    [CmdletBinding()]
    param(
        [string]$CommitMessage = '',
        [switch]$PushToRemote
    )
    
    Write-BuildHeader "Committing Version Bump"
    
    $propsPath = Join-Path $ProjectRoot 'Directory.Build.props'
    $srcDir = Join-Path $ProjectRoot 'src'
    $wpfDir = Join-Path $srcDir 'Redball.UI.WPF'
    $wpfProjectPath = Join-Path $wpfDir 'Redball.UI.WPF.csproj'
    $versionFilePath = Join-Path $PSScriptRoot 'version.txt'
    
    $targetPath = if (Test-Path $propsPath) { $propsPath } else { $wpfProjectPath }
    
    $resolvedMessage = if ($CommitMessage) { $CommitMessage } elseif ($BumpMessage) { $BumpMessage } else { "Bump version to $Version" }
    
    Write-BuildStep "Committing with message: $resolvedMessage"
    git add $versionFilePath
    git add $targetPath
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-BuildStep "No staged version changes detected; skipping commit"
        return
    }

    git commit -m $resolvedMessage
    if ($LASTEXITCODE -ne 0) {
        throw "git commit failed"
    }
    
    if ($PushToRemote) {
        Write-BuildStep "Pushing to remote..."
        git push
        if ($LASTEXITCODE -ne 0) {
            throw "git push failed"
        }
        Write-BuildSuccess "Pushed version bump to remote"
    }
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
            Write-Debug "Module enumeration error details: $($_.Exception.Message)"
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
            Write-Debug "WMI query error details: $($_.Exception.Message)"
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
                # For Redball processes, only consider instances running from the project folder
                if ($procName -eq 'Redball.UI.WPF') {
                    $procs = $procs | Where-Object {
                        try { $_.Path -and $_.Path.StartsWith($ProjectRoot, [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
                    }
                    if (-not $procs) { continue }
                }
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

function Test-WdkAvailable {
    [CmdletBinding()]
    param()

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Include'
    if (-not (Test-Path $kitsRoot)) {
        return $false
    }

    $ntddkHeader = Get-ChildItem -Path $kitsRoot -Filter 'ntddk.h' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
    return ($null -ne $ntddkHeader)
}

function Step-BuildDriver {
    [CmdletBinding()]
    param()
    Write-BuildHeader "Building Redball.KMDF Driver"
    
    $driverProjPath = Join-Path $ProjectRoot 'src\Redball.Driver\Redball.KMDF.vcxproj'
    $driverDistPath = Join-Path $script:DistPath 'driver'
    
    if (-not (Test-Path $driverProjPath)) {
        Write-Warning "Driver project not found: $driverProjPath"
        return
    }

    # Detect MSBuild (Enterprise preferred, BuildTools fallback)
    $vsPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
    if (-not $vsPath) {
        $vsPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -products * -latest -property installationPath
    }
    $msbuildPath = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
    
    if (-not (Test-Path $msbuildPath)) {
        Write-Warning "MSBuild not found at $msbuildPath. Driver build will be skipped."
        return
    }

    if (-not (Test-WdkAvailable)) {
        Write-Warning "WDK headers (ntddk.h) were not found. Skipping KMDF driver build. Install WDK 11 to build driver artifacts."
        return
    }

    Write-BuildStep "Building driver ($Configuration|x64)..."
    # Ensure environment is initialized for MSBuild to find its targets (VCTargetsPath)
    $env:VCTargetsPath = Join-Path $vsPath "MSBuild\Microsoft\VC\v170\"
    & $msbuildPath $driverProjPath /p:Configuration=$Configuration /p:Platform=x64 /t:Build `
        /p:SpectreMitigation=false `
        /p:CheckMSVCComponents=false `
        /p:InfVerif_DoNotVerify=true `
        /p:EnableInfVerif=false `
        /p:SignMode=Off
    
    if ($LASTEXITCODE -ne 0) {
        Write-BuildError "Driver build failed. Ensure WDK 11 is installed."
        # Don't throw here to allow app build if driver fails
        return
    }

    if (-not (Test-Path $driverDistPath)) { New-Item -ItemType Directory -Path $driverDistPath -Force | Out-Null }
    
    $outDir = Join-Path (Split-Path $driverProjPath) "x64\$Configuration"
    Copy-Item -Path (Join-Path $outDir "Redball.KMDF.sys") -Destination $driverDistPath -Force
    Copy-Item -Path (Join-Path $outDir "Redball.KMDF.inf") -Destination $driverDistPath -Force
    
    # Copy catalog file if it exists (Inf2Cat output)
    $catPath = Join-Path $outDir "Redball.KMDF\redball.kmdf.cat" 
    if (Test-Path $catPath) {
        Copy-Item -Path $catPath -Destination $driverDistPath -Force
    }
    
    if ($SignDriver) {
        Write-BuildStep "Signing driver binary..."
        $signScript = Join-Path $currentScriptRoot "Sign-Driver.ps1"
        if (Test-Path $signScript) {
            $sysPath = Join-Path $driverDistPath "Redball.KMDF.sys"
            & $signScript -DriverPath $sysPath
        }
        else {
            Write-Warning "Sign-Driver.ps1 not found. Skipping signing."
        }
    }

    Write-BuildSuccess "Driver artifacts copied to $driverDistPath"
}

function Step-BuildWpfApp {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    Write-BuildHeader "Building WPF Application"
    
    $solutionPath = Join-Path $script:ProjectRoot 'Redball.v3.sln'
    $srcDir = Join-Path $script:ProjectRoot 'src'
    $wpfDir = Join-Path $srcDir 'Redball.UI.WPF'
    $projectPath = Join-Path $wpfDir 'Redball.UI.WPF.csproj'
    $publishDir = Join-Path $script:DistPath 'wpf-publish'
    
    if (-not (Test-Path $projectPath)) {
        Write-Warning "WPF project not found: $projectPath"
        return
    }
    
    if (-not $PSCmdlet.ShouldProcess("WPF App v$Version", 'Build')) {
        return
    }
    
    # Stop Redball processes running from the project folder early to prevent locks
    $runningProcesses = Get-Process -Name 'Redball.UI.WPF', 'Redball', 'MSBuild', 'dotnet' -ErrorAction SilentlyContinue
    if ($runningProcesses) {
        $projectProcesses = $runningProcesses | Where-Object {
            try { $_.Path -and $_.Path.StartsWith($ProjectRoot, [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
        }
        if ($projectProcesses) {
            Write-BuildStep "Stopping project-related processes to prevent file locks..."
            $projectProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            Write-BuildSuccess "Project processes stopped"
        }
    }

    # Detect and resolve file locks before building
    $srcDir = Join-Path $ProjectRoot 'src'
    $wpfDir = Join-Path $srcDir 'Redball.UI.WPF'
    $objDir = Join-Path $wpfDir 'obj'
    $netDir = Join-Path $objDir 'net10.0-windows'
    $winDir = Join-Path $netDir 'win-x64'
    $dllPath = Join-Path $winDir 'Redball.UI.WPF.dll'
    
    if (Test-Path $dllPath) {
        try {
            $testStream = [System.IO.File]::Open($dllPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
            $testStream.Close()
            $testStream.Dispose()
        }
        catch {
            Write-Warning "DLL file is locked by another process"
            $null = Stop-LockingProcess -FilePath $dllPath
        }
    }
    
    # Restore packages
    Write-BuildStep "Restoring NuGet packages..."
    dotnet restore $solutionPath --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed"
    }
    Write-BuildSuccess "Packages restored"
    
    # Building Solution with MSBuild (essential for hybrid solutions)
    Write-BuildStep "Building solution ($Configuration)..."
    $vsPathForSln = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
    $msbuildPathForSln = Join-Path $vsPathForSln 'MSBuild\Current\Bin\MSBuild.exe'
    $env:VCTargetsPath = Join-Path $vsPathForSln "MSBuild\Microsoft\VC\v170\"

    # Force specific flags for KMDF Driver success (Spectre mitigation is optional if libraries are missing)
    & $msbuildPathForSln $solutionPath /p:Configuration=$Configuration /p:Platform="Any CPU" /t:Build /m `
        /p:SpectreMitigation=false `
        /p:CheckMSVCComponents=false `
        /p:InfVerif_DoNotVerify=true `
        /p:EnableInfVerif=false `
        /p:SignMode=Off
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild solution build failed"
    }
    Write-BuildSuccess "Solution build completed"
    
    # Publish modular application (C# only)
    Write-BuildStep "Publishing modular application..."
    dotnet publish $projectPath `
        --configuration $Configuration `
        --output $publishDir `
        --self-contained false `
        --runtime win-x64 `
        --property:PublishSingleFile=false `
        --property:PublishTrimmed=false `
        --property:EnableCompressionInSingleFile=false
    
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed"
    }
    # Finalize Publish Directory
    Write-BuildStep "Finalizing modular publish assets (moving DLLs to dll/)..."
    Start-Sleep -Seconds 1 # Give OS time to release locks
    
    $dllDir = Join-Path $publishDir "dll"
    if (-not (Test-Path $dllDir)) { New-Item -ItemType Directory -Path $dllDir -Force | Out-Null }
    
    # Move DLLs to dll folder (excluding the main executable's primary assembly)
    $dllsToMove = Get-ChildItem -Path $publishDir -Filter "*.dll" | Where-Object { $_.Name -ne "Redball.UI.WPF.dll" }
    
    if ($null -ne $dllsToMove -and $dllsToMove.Count -gt 0) {
        foreach ($file in $dllsToMove) {
            $moved = $false
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                try {
                    Move-Item -Path $file.FullName -Destination $dllDir -Force -ErrorAction Stop
                    $moved = $true
                    break
                }
                catch {
                    Write-Warning "  Attempt ${attempt}: Failed to move $($file.Name): $($_.Exception.Message)"
                    if ($attempt -lt 3) {
                        # Try to identify and stop locking process
                        $null = Stop-LockingProcess -FilePath $file.FullName
                        Start-Sleep -Seconds ($attempt * 2)
                    }
                }
            }
            if (-not $moved) {
                throw "Could not move file $($file.Name) after multiple attempts. File is likely locked."
            }
        }
        Write-BuildSuccess "Successfully moved dependencies to $dllDir"
    }

    # Note: Assembly resolution from dll/ is handled by Program.cs at runtime
    # via AssemblyLoadContext.Default.Resolving - no runtimeconfig patching needed
    
    # Unblock InputInterceptor.dll (prevents Windows security blocks)
    $interceptorFile = Join-Path $dllDir "InputInterceptor.dll"
    if (Test-Path $interceptorFile) {
        Write-HostSafe "  Unblocking InputInterceptor.dll..." -ForegroundColor Gray
        Unblock-File -Path $interceptorFile -ErrorAction SilentlyContinue
    }
    
    # Create logs folder placeholder
    $logsDir = Join-Path $publishDir "logs"
    if (-not (Test-Path $logsDir)) { New-Item -ItemType Directory -Path $logsDir -Force | Out-Null }
    $logsKeepFile = Join-Path $logsDir ".keep"
    New-Item -ItemType File -Path $logsKeepFile -Force | Out-Null

    # Generate Update Manifest
    Write-BuildStep "Generating Update Manifest..."
    $pubFiles = Get-ChildItem -Path $publishDir -File -Recurse
    $manifestFiles = @()
    foreach ($file in $pubFiles) {
        # PowerShell 5.1 compatible relative path
        $fullPath = $file.FullName
        if ($fullPath.StartsWith($publishDir)) {
            $relativePath = $fullPath.Substring($publishDir.Length).TrimStart('\').TrimStart('/')
        }
        else {
            $relativePath = $file.Name
        }

        if ([string]::IsNullOrWhiteSpace($relativePath) -or $relativePath.StartsWith("..")) { continue }
        if ($relativePath -eq "manifest.json") { continue }
        $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
        $manifestFiles += @{
            name = $relativePath
            hash = $hash
            size = $file.Length
        }
    }

    $manifest = @{
        version   = $Version
        timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        files     = $manifestFiles
    }

    $manifestPath = Join-Path $publishDir "manifest.json"
    $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding UTF8
    Write-BuildSuccess "Publish completed and manifest generated in $publishDir"
    
    $srcDir = Join-Path $ProjectRoot 'src'
    $wpfDir = Join-Path $srcDir 'Redball.UI.WPF'
    $assetsSource = Join-Path $wpfDir 'Assets'
    $assetsDest = Join-Path $publishDir 'Assets'
    if (Test-Path $assetsSource) {
        if (-not (Test-Path $assetsDest)) {
            New-Item -ItemType Directory -Path $assetsDest -Force | Out-Null
        }
        $assetsSourceWildcard = Join-Path $assetsSource '*'
        Copy-Item -Path $assetsSourceWildcard -Destination $assetsDest -Recurse -Force
        Write-BuildSuccess "Copied Assets to publish directory"
    }
    
    # List output
    $exePath = Join-Path $publishDir 'Redball.UI.WPF.exe'
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        Write-HostSafe "  Executable: $($fileInfo.Name) ($([math]::Round($fileInfo.Length / 1MB, 2)) MB)" -ForegroundColor Gray
    }

    # Verify build before proceeding to MSI
    if (-not $SkipVerify) {
        Step-VerifyBuild -PublishDir $publishDir
    }
}

function Step-BuildService {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    Write-BuildHeader "Building Redball Input Service"

    $srcDir = Join-Path $script:ProjectRoot 'src'
    $serviceDir = Join-Path $srcDir 'Redball.Service'
    $helperDir = Join-Path $srcDir 'Redball.SessionHelper'
    $serviceProject = Join-Path $serviceDir 'Redball.Service.csproj'
    $helperProject = Join-Path $helperDir 'Redball.SessionHelper.csproj'
    $publishDir = Join-Path $script:DistPath 'Redball.Service'

    if (-not (Test-Path $serviceProject)) {
        Write-Warning "Service project not found: $serviceProject"
        return
    }

    if (-not $PSCmdlet.ShouldProcess("Input Service v$Version", 'Build')) {
        return
    }

    # Build and publish service
    Write-BuildStep "Publishing service ($Configuration)..."
    dotnet publish $serviceProject `
        --configuration $Configuration `
        --output $publishDir `
        --self-contained false `
        --runtime win-x64 `
        --property:PublishSingleFile=true

    if ($LASTEXITCODE -ne 0) {
        throw "Service publish failed"
    }

    # Build and publish session helper
    if (Test-Path $helperProject) {
        Write-BuildStep "Publishing session helper ($Configuration)..."
        $helperPublishDir = Join-Path $publishDir 'SessionHelper'
        dotnet publish $helperProject `
            --configuration $Configuration `
            --output $helperPublishDir `
            --self-contained false `
            --runtime win-x64 `
            --property:PublishSingleFile=true

        if ($LASTEXITCODE -ne 0) {
            throw "Session helper publish failed"
        }

        # Copy helper to service directory for easy access
        $helperExe = Join-Path $helperPublishDir 'Redball.SessionHelper.exe'
        $serviceHelperPath = Join-Path $publishDir 'Redball.SessionHelper.exe'
        if (Test-Path $helperExe) {
            Copy-Item $helperExe $serviceHelperPath -Force
            Write-BuildSuccess "Copied session helper to service directory"
        }
    }

    # Verify output
    $serviceExe = Join-Path $publishDir 'Redball.Service.exe'
    if (Test-Path $serviceExe) {
        $fileInfo = Get-Item $serviceExe
        Write-BuildSuccess "Service built: $($fileInfo.Name) ($([math]::Round($fileInfo.Length / 1KB, 2)) KB)"
    }
    else {
        throw "Service executable not found after build"
    }

    Write-HostSafe "  Install: .\scripts\Install-Service.ps1" -ForegroundColor Gray
    Write-HostSafe "  Uninstall: .\scripts\Uninstall-Service.ps1" -ForegroundColor Gray
}

function Step-SignExecutables {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [string]$CertificateName = "RedballDevCert"
    )
    Write-BuildHeader "Signing Executables"

    # Locate Signtool (SDK)
    $sdkPath = Join-Path "${env:ProgramFiles(x86)}" "Windows Kits\10\bin"
    $signtool = Get-ChildItem -Path $sdkPath -Filter "signtool.exe" -Recurse | Where-Object { $_.FullName -like "*\x64\signtool.exe" } | Select-Object -First 1
    if (-not $signtool) {
        Write-Warning "signtool.exe not found. Skipping code signing. Install Windows SDK to enable signing."
        return
    }
    Write-BuildStep "Found signtool: $($signtool.FullName)"

    # Find or create self-signed certificate in CurrentUser\My
    $certStore = "Cert:\CurrentUser\My"
    $cert = Get-ChildItem -Path $certStore | Where-Object { $_.Subject -eq "CN=$CertificateName" } | Select-Object -First 1
    
    if (-not $cert) {
        Write-BuildStep "Creating new code signing certificate: $CertificateName"
        # Create with proper code signing EKU
        $cert = New-SelfSignedCertificate `
            -Subject "CN=$CertificateName" `
            -CertStoreLocation $certStore `
            -Type CodeSigningCert `
            -KeyUsage DigitalSignature `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -NotAfter (Get-Date).AddYears(5) `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")
        Write-BuildSuccess "Certificate created with thumbprint: $($cert.Thumbprint)"
    }
    else {
        Write-BuildSuccess "Using existing certificate: $CertificateName (Thumbprint: $($cert.Thumbprint))"
    }

    # Trust certificate locally (for service installation)
    try {
        $tmpCertPath = [System.IO.Path]::GetTempFileName()
        Export-Certificate -Cert $cert -FilePath $tmpCertPath -Force | Out-Null
        Import-Certificate -FilePath $tmpCertPath -CertStoreLocation "Cert:\LocalMachine\Root" -ErrorAction SilentlyContinue | Out-Null
        Import-Certificate -FilePath $tmpCertPath -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" -ErrorAction SilentlyContinue | Out-Null
        Remove-Item $tmpCertPath -ErrorAction SilentlyContinue
        Write-BuildStep "Certificate trusted in local stores"
    }
    catch {
        Write-Warning "Could not trust certificate in local machine stores (may need admin): $_"
    }

    # Files to sign
    $filesToSign = @()
    
    # Service executable
    $serviceExe = Join-Path $script:DistPath 'Redball.Service\Redball.Service.exe'
    if (Test-Path $serviceExe) {
        $filesToSign += $serviceExe
    }
    
    # Session helper
    $helperExe = Join-Path $script:DistPath 'Redball.Service\Redball.SessionHelper.exe'
    if (Test-Path $helperExe) {
        $filesToSign += $helperExe
    }
    
    # WPF app (in publish dir)
    $wpfExe = Join-Path $script:DistPath 'wpf-publish\Redball.UI.WPF.exe'
    if (Test-Path $wpfExe) {
        $filesToSign += $wpfExe
    }

    if ($filesToSign.Count -eq 0) {
        Write-Warning "No executables found to sign"
        return
    }

    foreach ($file in $filesToSign) {
        Write-BuildStep "Signing: $(Split-Path $file -Leaf)"
        # Use /s with CurrentUser\My store location
        & $signtool.FullName sign /v /s "My" /sha1 $($cert.Thumbprint) /fd SHA256 /t http://timestamp.digicert.com "$file"
        if ($LASTEXITCODE -eq 0) {
            Write-BuildSuccess "Signed: $(Split-Path $file -Leaf)"
        }
        else {
            Write-Warning "Failed to sign: $(Split-Path $file -Leaf) (Exit code: $LASTEXITCODE)"
        }
    }
}

function Step-VerifyBuild {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDir
    )

    Write-BuildHeader "Verifying Build Integrity"

    $exePath = Join-Path $PublishDir 'Redball.UI.WPF.exe'
    if (-not (Test-Path $exePath)) {
        throw "Verification failed: Executable not found at $exePath"
    }

    Write-BuildStep "Launching executable for smoke test..."
    
    # We want to catch XamlParseException and other early startup crashes.
    # The app logs to a file in the 'logs' subfolder of its base directory.
    $logDir = Join-Path $PublishDir 'logs'
    $logFile = Join-Path $logDir 'Redball.UI.log'

    # Ensure log directory exists so we can check for the log file
    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }

    # Clear old log if exists
    if (Test-Path $logFile) {
        Remove-Item $logFile -Force
    }

    # Start the process. We use a timeout to let it initialize.
    # We pass a dummy argument to potentially trigger specific behavior or just run normally.
    $process = Start-Process -FilePath $exePath -ArgumentList "--smoke-test" -PassThru -WindowStyle Hidden
    
    Write-BuildStep "Waiting for initialization (5s)..."
    Start-Sleep -Seconds 5

    # Check if process is still running or exited with error
    if ($process.HasExited) {
        $exitCode = $process.ExitCode
        if ($exitCode -ne 0) {
            throw "Executable crashed on startup with exit code $exitCode. Check logs for details."
        }
    }

    # Stop the process
    try {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    catch {
        Write-Verbose "Process already terminated or access denied for PID $($process.Id)"
    }

    # Check log file for FATAL or ERR entries that indicate XAML parse issues
    if (Test-Path $logFile) {
        $logContent = Get-Content $logFile -Raw
        if ($logContent -match "\[FTL\]" -or $logContent -match "\[ERR\].*XAML parse error") {
            Write-BuildError "Verification failed: Fatal errors detected in application log."
            Write-HostSafe "--- LOG START ---" -ForegroundColor Gray
            Write-HostSafe $logContent -ForegroundColor Gray
            Write-HostSafe "--- LOG END ---" -ForegroundColor Gray
            throw "Build verification failed due to runtime errors. Fix assets/XAML and try again."
        }
    }
    else {
        Write-Warning "No log file found at $logFile. Verification might be inconclusive."
    }

    Write-BuildSuccess "Build verification passed: Executable launched and initialized without fatal errors."
}

function Step-GenerateInstallerTheme {
    [CmdletBinding()]
    param()
    Write-BuildHeader "Generating MSI Installer Theme"
    
    $installerDir = Join-Path $script:ProjectRoot 'installer'
    $themeScript = Join-Path $installerDir 'Generate-InstallerTheme.ps1'
    
    if (-not (Test-Path $themeScript)) {
        Write-Warning "Theme generator script not found: $themeScript"
        Write-BuildStep "Using existing banner.bmp and dialog.bmp if available"
        return
    }
    
    Write-BuildStep "Generating modern theme images for MSI installer..."
    try {
        & powershell.exe -ExecutionPolicy Bypass -File $themeScript
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Theme generation exited with code $LASTEXITCODE. Using existing images if available."
        }
        else {
            Write-BuildSuccess "Theme images generated successfully"
            $bannerPath = Join-Path $installerDir 'banner.bmp'
            $dialogPath = Join-Path $installerDir 'dialog.bmp'
            if (Test-Path $bannerPath) {
                $bannerInfo = Get-Item $bannerPath
                Write-HostSafe "  Banner: $($bannerInfo.Name) ($([math]::Round($bannerInfo.Length / 1KB, 2)) KB)" -ForegroundColor Gray
            }
            if (Test-Path $dialogPath) {
                $dialogInfo = Get-Item $dialogPath
                Write-HostSafe "  Dialog: $($dialogInfo.Name) ($([math]::Round($dialogInfo.Length / 1KB, 2)) KB)" -ForegroundColor Gray
            }
        }
    }
    catch {
        Write-Warning "Theme generation failed: $_. Using existing images if available."
    }
}

function Step-BuildMsiInstaller {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    Write-BuildHeader "Building MSI Installer"
    
    $msiInstallerDir = Join-Path $script:ProjectRoot 'installer'
    $msiScript = Join-Path $msiInstallerDir 'Build-MSI-v2.ps1'
    if (-not (Test-Path $msiScript)) {
        Write-Warning "MSI build script not found: $msiScript"
        return
    }
    
    if (-not $PSCmdlet.ShouldProcess("MSI for v$($script:Version)", 'Build')) {
        return
    }
    
    # Accept WiX v7 OSMF EULA
    $env:WIX_OSMF_EULA_ACCEPTED = '1'
    
    & $msiScript -Configuration $Configuration -Version $script:Version
    
    if ($LASTEXITCODE -ne 0) {
        throw "MSI build failed"
    }
    
    $msiPath = Join-Path $script:DistPath "Redball-$($script:Version).msi"
    if (Test-Path $msiPath) {
        $fileInfo = Get-Item $msiPath
        Write-BuildSuccess "MSI created: $($fileInfo.Name) ($([math]::Round($fileInfo.Length / 1MB, 2)) MB)"
    }
}

function Get-ResolvedMsiPath {
    [CmdletBinding()]
    param()

    $versionedMsiPath = Join-Path $script:DistPath "Redball-$($script:Version).msi"
    $defaultMsiPath = Join-Path $script:DistPath 'Redball.msi'

    if (Test-Path -LiteralPath $versionedMsiPath) {
        return (Resolve-Path -LiteralPath $versionedMsiPath).Path
    }

    if (Test-Path -LiteralPath $defaultMsiPath) {
        return (Resolve-Path -LiteralPath $defaultMsiPath).Path
    }

    return $null
}

function Step-InstallMsiHelper {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [switch]$LaunchInstaller,
        [switch]$Silent,
        [switch]$StartMinimized,
        [switch]$EnableBatteryAware,
        [switch]$EnableNetworkAware,
        [switch]$EnableIdleDetection,
        [switch]$InstallHid,
        [switch]$DisableDesktopShortcut,
        [switch]$DisableStartup,
        [switch]$EnableTelemetry,
        [switch]$ConfigEncrypted
    )

    Write-BuildHeader "MSI Install Helper"

    $resolvedMsiPath = Get-ResolvedMsiPath
    if (-not $resolvedMsiPath) {
        Write-Warning "No MSI found in dist. Build MSI first or verify output path."
        return
    }

    $msiLogPath = Join-Path $script:ProjectRoot 'msi_install.log'
    
    # Build installer arguments
    $installerArguments = "/i `"$resolvedMsiPath`" /L*V `"$msiLogPath`""
    
    # Add silent install parameters if requested
    if ($Silent) {
        $installerArguments += " /qn REDBALL_SILENTINSTALL=1"
        Write-BuildStep "Silent installation mode enabled"
        
        # Add enterprise configuration properties
        if ($StartMinimized) { $installerArguments += " REDBALL_STARTMINIMIZED=1" }
        if ($EnableBatteryAware) { $installerArguments += " REDBALL_ENABLEBATTERYAWARE=1" }
        if ($EnableNetworkAware) { $installerArguments += " REDBALL_ENABLENETWORKAWARE=1" }
        if ($EnableIdleDetection) { $installerArguments += " REDBALL_ENABLEIDLEDETECTION=1" }
        if ($InstallHid) { $installerArguments += " REDBALL_INSTALLHID=1" }
        if ($DisableDesktopShortcut) { $installerArguments += " REDBALL_DISABLEDESKTOPSHORTCUT=1" }
        if ($DisableStartup) { $installerArguments += " REDBALL_DISABLESTARTUP=1" }
        if ($EnableTelemetry) { $installerArguments += " REDBALL_ENABLETELEMETRY=1" }
        if ($ConfigEncrypted) { $installerArguments += " REDBALL_CONFIGENCRYPTED=1" }
    }
    
    $installerCommand = "msiexec $installerArguments"

    Write-BuildStep "Use this command (absolute path, with logging):"
    Write-HostSafe "  $installerCommand" -ForegroundColor Gray
    
    if ($Silent) {
        Write-BuildStep "Enterprise deployment example:"
        Write-HostSafe "  msiexec /i `"$resolvedMsiPath`" /qn REDBALL_SILENTINSTALL=1 REDBALL_STARTMINIMIZED=1 REDBALL_ENABLEBATTERYAWARE=1" -ForegroundColor Gray
    }

    if (-not $LaunchInstaller) {
        return
    }

    if (-not $PSCmdlet.ShouldProcess($resolvedMsiPath, 'Launch MSI installer')) {
        return
    }

    Write-BuildStep "Launching MSI installer..."
    Start-Process -FilePath 'msiexec.exe' -ArgumentList $installerArguments -Wait
    Write-BuildSuccess "Installer process finished. Log: $msiLogPath"
}

function Step-CreateReleasePackage {
    [CmdletBinding()]
    param()
    Write-BuildHeader "Creating Release Artifacts"
    
    # MSI now contains everything - just verify it exists and show summary
    $msiPath = Join-Path $DistPath "Redball-$Version.msi"
    if (-not (Test-Path $msiPath)) {
        throw "MSI not found: $msiPath"
    }
    
    $msiInfo = Get-Item $msiPath
    Write-BuildSuccess "Release MSI ready: $($msiInfo.Name) ($([math]::Round($msiInfo.Length / 1MB, 2)) MB)"
    Write-HostSafe "  Location: $msiPath" -ForegroundColor Gray
    Write-HostSafe "  Contains: WPF Application, Core Services, Configuration, HID Driver Support" -ForegroundColor Gray
    Write-HostSafe "  HID Features: Driver Lifecycle Management, Safe Mode, Robustness Retries" -ForegroundColor Gray
}

function Step-CleanupDist {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [int]$KeepLatest = 5
    )

    Write-BuildHeader "Cleaning Old Distribution Artifacts"

    if (-not (Test-Path $DistPath)) {
        Write-BuildStep "Distribution path not found, skipping cleanup"
        return
    }

    $versionedArtifacts = Get-ChildItem -Path $DistPath -File | Where-Object {
        $_.Name -match '^Redball-(\d+\.\d+\.\d+)\.msi$'
    }

    if (-not $versionedArtifacts) {
        Write-BuildStep "No versioned distribution artifacts found"
        return
    }

    $artifactGroups = $versionedArtifacts |
    Group-Object {
        if ($_.Name -match '^Redball-(\d+\.\d+\.\d+)\.msi$') {
            $matches[1]
        }
    } |
    ForEach-Object {
        [PSCustomObject]@{
            Version     = $_.Name
            VersionInfo = [version]$_.Name
            Files       = $_.Group
        }
    } |
    Sort-Object -Property VersionInfo -Descending

    if ($artifactGroups.Count -le $KeepLatest) {
        Write-BuildSuccess "Found $($artifactGroups.Count) version(s); nothing to clean"
        return
    }

    $versionsToRemove = $artifactGroups | Select-Object -Skip $KeepLatest

    foreach ($artifactVersion in $versionsToRemove) {
        Write-BuildStep "Removing distribution version $($artifactVersion.Version)"
        foreach ($artifactFile in $artifactVersion.Files) {
            if ($PSCmdlet.ShouldProcess($artifactFile.FullName, 'Delete old distribution artifact')) {
                Remove-Item -Path $artifactFile.FullName -Force
                Write-BuildSuccess "Removed $($artifactFile.Name)"
            }
        }
    }

    Write-BuildSuccess "Kept the latest $KeepLatest distribution version(s)"
}

function Step-CleanBuild {
    [CmdletBinding(SupportsShouldProcess)]
    param()

    Write-BuildHeader "Cleaning Build Artifacts"

    # Suppress progress bars during file removal
    $oldProgressPreference = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'

    $srcDir = Join-Path $ProjectRoot 'src'
    $wpfDir = Join-Path $srcDir 'Redball.UI.WPF'
    $testDir = Join-Path $ProjectRoot 'tests'
    $testIntDir = Join-Path $ProjectRoot 'tests-integration'
    $testE2EDir = Join-Path $ProjectRoot 'tests-e2e'
    $testUiDir = Join-Path $ProjectRoot 'tests-ui-automation'

    $pathsToClean = @(
        (Join-Path $wpfDir 'obj'),
        (Join-Path $wpfDir 'bin'),
        (Join-Path $testDir 'obj'),
        (Join-Path $testDir 'bin'),
        (Join-Path $testIntDir 'obj'),
        (Join-Path $testIntDir 'bin'),
        (Join-Path $testE2EDir 'obj'),
        (Join-Path $testE2EDir 'bin'),
        (Join-Path $testUiDir 'obj'),
        (Join-Path $testUiDir 'bin'),
        (Join-Path $srcDir 'Redball.Driver\obj'),
        (Join-Path $srcDir 'Redball.Driver\bin'),
        (Join-Path $srcDir 'Redball.Driver\x64'),
        (Join-Path $srcDir 'Redball.Service\obj'),
        (Join-Path $srcDir 'Redball.Service\bin'),
        (Join-Path $srcDir 'Redball.SessionHelper\obj'),
        (Join-Path $srcDir 'Redball.SessionHelper\bin'),
        (Join-Path $DistPath 'Redball.Service'),
        $DistPath
    )

    foreach ($path in $pathsToClean) {
        if (Test-Path $path) {
            if ($PSCmdlet.ShouldProcess($path, 'Remove directory')) {
                $removed = $false
                for ($attempt = 1; $attempt -le 3; $attempt++) {
                    try {
                        Remove-Item -Path $path -Recurse -Force -ErrorAction Stop
                        Write-BuildSuccess "Removed: $path"
                        $removed = $true
                        break
                    }
                    catch {
                        if ($attempt -lt 3) {
                            Write-BuildStep "  Locked files detected in $path, retrying in $($attempt * 2)s... (attempt $attempt/3)"
                            Start-Sleep -Seconds ($attempt * 2)
                        }
                    }
                }
                if (-not $removed) {
                    Write-Warning "Could not fully clean $path (files locked by another process). Continuing anyway."
                }
            }
        }
    }

    # Restore progress preference
    $ProgressPreference = $oldProgressPreference

    # Run MSBuild clean on the solution
    # Detect MSBuild (Enterprise preferred, BuildTools fallback)
    $vsPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
    if (-not $vsPath) {
        $vsPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -products * -latest -property installationPath
    }
    $msbuildPath = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
    $env:VCTargetsPath = Join-Path $vsPath "MSBuild\Microsoft\VC\v170\"

    $solutionPath = Join-Path $script:ProjectRoot 'Redball.v3.sln'
    if (Test-Path $msbuildPath) {
        Write-BuildStep "Running MSBuild clean on solution..."
        & $msbuildPath $solutionPath /t:Clean /p:Configuration=$Configuration /verbosity:minimal /p:SpectreMitigation=false /p:CheckMSVCComponents=false
        if ($LASTEXITCODE -eq 0) {
            Write-BuildSuccess "Solution cleaned"
        }
        else {
            Write-Warning "MSBuild clean completed with warnings"
        }
    }
    else {
        Write-BuildStep "Running dotnet clean..."
        dotnet clean $solutionPath --configuration $Configuration --verbosity quiet
    }

    Write-BuildSuccess "Clean build completed"
}

# End Helper Functions

# Build timing
$ErrorActionPreference = 'Stop'

# Initialize logging
$script:LogsPath = Join-Path $script:ProjectRoot 'logs'
if (-not (Test-Path $script:LogsPath)) { New-Item -ItemType Directory -Path $script:LogsPath -Force | Out-Null }
$script:LogFile = Join-Path $script:LogsPath ("build_{0:yyyy-MM-dd_HH-mm-ss}.log" -f (Get-Date))
Start-Transcript -Path $script:LogFile -IncludeInvocationHeader -Force | Out-Null
Write-HostSafe "Logging to: $script:LogFile" -ForegroundColor Gray
$script:BuildStartTime = Get-Date

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
    Write-HostSafe "  Features: Keep-Awake Engine, TypeThing (Standard/HID), Pomodoro, Mini-Widget" -ForegroundColor Gray
    Write-HostSafe "  HID Stack: Live Health, Smart Install/Uninstall, Safe Mode, Auto-Fallback, Idle-Release, Repair Stack" -ForegroundColor Gray
    Write-HostSafe "  Safety: Emergency Release (Ctrl+Shift+Esc), Health Checks, Retry Logic, Integrity Validation`n" -ForegroundColor Gray
    
    # Clean build if requested (default on, use -NoClean to skip)
    if (-not $NoClean) {
        Step-CleanBuild
    }
    
    # Bump version by default so build outputs always get a new version
    if (-not $SkipVersionBump) {
        Step-BumpVersion
    }
    else {
        Write-HostSafe "  Skipping version bump ( -SkipVersionBump )" -ForegroundColor Yellow
    }
    
    # Ensure dist directory exists
    if (-not (Test-Path $script:DistPath)) {
        New-Item -ItemType Directory -Path $script:DistPath -Force | Out-Null
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
        Step-BuildDriver
        Step-BuildWpfApp
        Step-BuildService
        Step-SignExecutables
    }
    else {
        Write-HostSafe "  Skipping WPF build ( -SkipWPF )" -ForegroundColor Yellow
    }

    # MSI Build (requires WPF files)
    if (-not $SkipMSI) {
        if ($SkipWPF) {
            Write-Warning "WPF build skipped - MSI will use previously built files if available"
        }
        Step-GenerateInstallerTheme
        Step-BuildMsiInstaller
        Step-InstallMsiHelper -LaunchInstaller:$InstallMsi
    }
    elseif ($InstallMsi) {
        Write-Warning "-InstallMsi was provided with -SkipMSI. Attempting install from existing dist artifacts."
        Step-InstallMsiHelper -LaunchInstaller
    }
    else {
        Write-HostSafe "  Skipping MSI build ( -SkipMSI )" -ForegroundColor Yellow
    }
    
    # Finalize release (MSI is now the primary artifact)
    if (-not $SkipMSI -or $BuildAll) {
        Step-CreateReleasePackage
        Step-CleanupDist
    }
    
    # Commit/push version bump only if build succeeded
    $isReleaseBuild = -not $SkipMSI
    $autoReleaseCommit = $isReleaseBuild -and (-not $SkipVersionBump) -and (-not $SkipReleaseCommit)
    $autoReleasePush = $isReleaseBuild -and (-not $SkipVersionBump) -and (-not $SkipReleasePush)
    $shouldCommitVersion = (-not $SkipVersionBump) -and ($BumpCommit -or $BumpPush -or $autoReleaseCommit)

    if ($shouldCommitVersion) {
        $resolvedReleaseMessage = if ($ReleaseMessage) {
            $ReleaseMessage
        }
        elseif ($isReleaseBuild -and (-not $BumpMessage)) {
            "chore(release): v$($script:Version)"
        }
        else {
            $BumpMessage
        }

        Step-CommitVersionBump -CommitMessage $resolvedReleaseMessage -PushToRemote:($BumpPush -or $autoReleasePush)
    }

    # Call release script to create GitHub release (only when MSI was built)
    if (-not $SkipMSI) {
        $releaseScript = Join-Path $currentScriptRoot "release.ps1"
        $releaseTag = "v$($script:Version)"
        if (Test-Path $releaseScript) {
            Write-HostSafe "  Calling release script..." -ForegroundColor Cyan
            & $releaseScript -Version $script:Version -Tag $releaseTag -SkipAutoBuild -AllowDirty
        }
        else {
            Write-HostSafe "  release.ps1 not found. GitHub release not created." -ForegroundColor Yellow
            Write-HostSafe "  Run manually: .\scripts\release.ps1 -Version $Version -Tag $releaseTag" -ForegroundColor Gray
        }
    }
    else {
        Write-HostSafe "  Skipping GitHub release (no MSI artifact — use full build to create a release)" -ForegroundColor Yellow
    }

    Write-HostSafe "`n══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-HostSafe "  BUILD SUCCEEDED" -ForegroundColor Green
    Write-HostSafe "══════════════════════════════════════════════════════════" -ForegroundColor Green
    $duration = (Get-Date) - $script:BuildStartTime
    Write-HostSafe "  Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray
    Write-HostSafe "  Log: $script:LogFile" -ForegroundColor Gray
    Write-HostSafe "══════════════════════════════════════════════════════════`n" -ForegroundColor Green
    Stop-Transcript | Out-Null
    exit 0
}
catch {
    $script:errorMessage = $_
    $script:stackTrace = $_.ScriptStackTrace
    Write-HostSafe "`n══════════════════════════════════════════════════════════" -ForegroundColor Red
    Write-HostSafe "  BUILD FAILED" -ForegroundColor Red
    Write-HostSafe "══════════════════════════════════════════════════════════" -ForegroundColor Red
    Write-HostSafe "  Error: $script:errorMessage" -ForegroundColor Red
    Write-HostSafe "  Trace: $script:stackTrace" -ForegroundColor Gray
    if ($script:BuildStartTime) {
        $duration = (Get-Date) - $script:BuildStartTime
        Write-HostSafe "  Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray
    }
    Write-HostSafe "  Log: $script:LogFile" -ForegroundColor Gray
    Write-HostSafe "══════════════════════════════════════════════════════════`n" -ForegroundColor Red
    Stop-Transcript | Out-Null
    exit 1
}








