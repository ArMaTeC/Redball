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
    [switch]$BuildAll,

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
    [switch]$NoClean,

    [Parameter()]
    [switch]$Parallel
)

$ErrorActionPreference = 'Stop'

# Resolve paths safely
$currentScriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path $MyInvocation.MyCommand.Path -Parent }
if (-not $currentScriptRoot) { $currentScriptRoot = (Get-Item .).FullName }

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
        $wpfProjectPath = Join-Path $script:ProjectRoot 'src' 'Redball.UI.WPF' 'Redball.UI.WPF.csproj'
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
    Import-Module PSScriptAnalyzer -Force
    
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
    [CmdletBinding()]
    param()
    Write-BuildHeader "Running PSScriptAnalyzer"
    
    # Fix BOM encoding for all ps1 files in scripts directory before linting
    # This addresses the PSUseBOMForUnicodeEncodedFile rule warnings
    # Exclude the currently running script to avoid self-modification/file lock issues
    Write-HostSafe "  Fixing UTF-8 BOM encoding for scripts..." -ForegroundColor Gray
    $scriptsToUpdate = Get-ChildItem -Path $PSScriptRoot -Filter *.ps1 -Recurse | Where-Object { $_.FullName -ne $PSCommandPath }
    foreach ($scriptToUpdate in $scriptsToUpdate) {
        $contentToFix = Get-Content -Path $scriptToUpdate.FullName -Raw
        $contentToFix | Set-Content -Path $scriptToUpdate.FullName -Encoding utf8BOM
    }
    
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
    [CmdletBinding()]
    param()
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

function Step-BumpVersion {
    [CmdletBinding()]
    param()
    
    Write-BuildHeader "Bumping Version ($BumpComponent)"
    
    $propsPath = Join-Path $ProjectRoot 'Directory.Build.props'
    $wpfProjectPath = Join-Path $ProjectRoot 'src' 'Redball.UI.WPF' 'Redball.UI.WPF.csproj'
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
    param()
    
    Write-BuildHeader "Committing Version Bump"
    
    $propsPath = Join-Path $ProjectRoot 'Directory.Build.props'
    $wpfProjectPath = Join-Path $ProjectRoot 'src' 'Redball.UI.WPF' 'Redball.UI.WPF.csproj'
    $versionFilePath = Join-Path $PSScriptRoot 'version.txt'
    
    $targetPath = if (Test-Path $propsPath) { $propsPath } else { $wpfProjectPath }
    
    $commitMessage = if ($BumpMessage) { $BumpMessage } else { "Bump version to $Version" }
    
    Write-BuildStep "Committing with message: $commitMessage"
    git add $versionFilePath
    git add $targetPath
    git commit -m $commitMessage
    
    if ($BumpPush) {
        Write-BuildStep "Pushing to remote..."
        git push
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

function Step-BuildWpfApp {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    Write-BuildHeader "Building WPF Application"
    
    $solutionPath = Join-Path $script:ProjectRoot 'Redball.v3.sln'
    $projectPath = Join-Path $script:ProjectRoot 'src' 'Redball.UI.WPF' 'Redball.UI.WPF.csproj'
    $publishDir = Join-Path $script:DistPath 'wpf-publish'
    
    if (-not (Test-Path $projectPath)) {
        Write-Warning "WPF project not found: $projectPath"
        return
    }
    
    if (-not $PSCmdlet.ShouldProcess("WPF App v$Version", 'Build')) {
        return
    }
    
    # Stop Redball processes running from the project folder early to prevent locks
    $runningProcesses = Get-Process -Name 'Redball.UI.WPF', 'Redball' -ErrorAction SilentlyContinue
    if ($runningProcesses) {
        $projectProcesses = $runningProcesses | Where-Object {
            try { $_.Path -and $_.Path.StartsWith($ProjectRoot, [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
        }
        if ($projectProcesses) {
            Write-BuildStep "Stopping Redball processes from project folder to prevent file locks..."
            $projectProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            Write-BuildSuccess "Project processes stopped"
        }
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
                Write-Warning "Cannot resolve file lock automatically. Build might fail."
            }
        }
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
    
    # Publish modular application
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
    Write-BuildStep "Finalizing modular publish assets (moving DLLs to bin/)..."
    Start-Sleep -Seconds 1 # Give OS time to release locks
    
    $binDir = Join-Path $publishDir "bin"
    if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }
    
    # Move DLLs to bin folder (excluding the main executable's primary assembly)
    $dllsToMove = Get-ChildItem -Path $publishDir -Filter "*.dll" | Where-Object { $_.Name -ne "Redball.UI.WPF.dll" }
    
    if ($null -ne $dllsToMove -and $dllsToMove.Count -gt 0) {
        foreach ($file in $dllsToMove) {
            $moved = $false
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                try {
                    Move-Item -Path $file.FullName -Destination $binDir -Force -ErrorAction Stop
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
        Write-BuildSuccess "Successfully moved dependencies to $binDir"
    }

    # Note: Assembly resolution from bin/ is handled by Program.cs at runtime
    # via AssemblyLoadContext.Default.Resolving - no runtimeconfig patching needed

    # Unblock InputInterceptor.dll (prevents Windows security blocks)
    $interceptorFile = Join-Path $binDir "InputInterceptor.dll"
    if (Test-Path $interceptorFile) {
        Write-HostSafe "  Unblocking InputInterceptor.dll..." -ForegroundColor Gray
        Unblock-File -Path $interceptorFile -ErrorAction SilentlyContinue
    }
    
    # Create logs folder placeholder
    $logsDir = Join-Path $publishDir "logs"
    if (-not (Test-Path $logsDir)) { New-Item -ItemType Directory -Path $logsDir -Force | Out-Null }
    New-Item -ItemType File -Path (Join-Path $logsDir ".keep") -Force | Out-Null

    # Generate Update Manifest
    Write-BuildStep "Generating Update Manifest..."
    $pubFiles = Get-ChildItem -Path $publishDir -File -Recurse
    $manifestFiles = @()
    foreach ($file in $pubFiles) {
        $relativePath = $file.FullName.Substring($publishDir.Length).TrimStart("\")
        if ($relativePath -eq "manifest.json") { continue }
        $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
        $manifestFiles += @{
            name = $relativePath
            hash = $hash
            size = $file.Length
        }
    }

    $manifest = @{
        version = $Version
        timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        files = $manifestFiles
    }

    $manifestPath = Join-Path $publishDir "manifest.json"
    $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding UTF8
    Write-BuildSuccess "Publish completed and manifest generated in $publishDir"
    
    # Copy Assets folder to publish directory for MSI packaging
    $assetsSource = Join-Path (Split-Path $projectPath -Parent) 'Assets'
    $assetsDest = Join-Path $publishDir 'Assets'
    if (Test-Path $assetsSource) {
        if (-not (Test-Path $assetsDest)) {
            New-Item -ItemType Directory -Path $assetsDest -Force | Out-Null
        }
        Copy-Item -Path (Join-Path $assetsSource '*') -Destination $assetsDest -Recurse -Force
        Write-BuildSuccess "Copied Assets to publish directory"
    }
    
    # List output
    $exePath = Join-Path $publishDir 'Redball.UI.WPF.exe'
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        Write-HostSafe "  Executable: $($fileInfo.Name) ($([math]::Round($fileInfo.Length / 1MB, 2)) MB)" -ForegroundColor Gray
    }
}

function Step-BuildMsiInstaller {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    Write-BuildHeader "Building MSI Installer"
    
    $msiScript = Join-Path $script:ProjectRoot 'installer' 'Build-MSI.ps1'
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
    Write-HostSafe "  Contains: WPF Application + Core Files + Configuration" -ForegroundColor Gray
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

    $pathsToClean = @(
        (Join-Path $ProjectRoot 'src' 'Redball.UI.WPF' 'obj'),
        (Join-Path $ProjectRoot 'src' 'Redball.UI.WPF' 'bin'),
        (Join-Path $ProjectRoot 'tests' 'obj'),
        (Join-Path $ProjectRoot 'tests' 'bin'),
        (Join-Path $ProjectRoot 'tests-integration' 'obj'),
        (Join-Path $ProjectRoot 'tests-integration' 'bin'),
        (Join-Path $ProjectRoot 'tests-e2e' 'obj'),
        (Join-Path $ProjectRoot 'tests-e2e' 'bin'),
        (Join-Path $ProjectRoot 'tests-ui-automation' 'obj'),
        (Join-Path $ProjectRoot 'tests-ui-automation' 'bin'),
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

    # Run dotnet clean on the solution
    $solutionPath = Join-Path $script:ProjectRoot 'Redball.v3.sln'
    if (Test-Path $solutionPath) {
        Write-BuildStep "Running dotnet clean..."
        dotnet clean $solutionPath --verbosity quiet
        if ($LASTEXITCODE -eq 0) {
            Write-BuildSuccess "Solution cleaned"
        }
        else {
            Write-Warning "dotnet clean completed with warnings"
        }
    }

    Write-BuildSuccess "Clean build completed"
}

# End Helper Functions

# Build timing
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
        Step-BuildWpfApp
    }
    else {
        Write-HostSafe "  Skipping WPF build ( -SkipWPF )" -ForegroundColor Yellow
    }
    
    # MSI Build (requires WPF files)
    if (-not $SkipMSI) {
        if ($SkipWPF) {
            Write-Warning "WPF build skipped - MSI will use previously built files if available"
        }
        Step-BuildMsiInstaller
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
    if ((-not $SkipVersionBump) -and ($BumpCommit -or $BumpPush)) {
        Step-CommitVersionBump
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
    Write-HostSafe "══════════════════════════════════════════════════════════`n" -ForegroundColor Green
    
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
    Write-HostSafe "══════════════════════════════════════════════════════════`n" -ForegroundColor Red
    exit 1
}








