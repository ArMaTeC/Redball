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

# Resolve project root
$script:ProjectRoot = $PSScriptRoot
$script:DistPath = Join-Path $ProjectRoot $OutputPath

# Import version from main script if not specified
if (-not $Version) {
    $mainScriptPath = Join-Path $ProjectRoot 'Redball.ps1'
    if (Test-Path $mainScriptPath) {
        $versionMatch = Get-Content $mainScriptPath | Select-String -Pattern "VERSION = '([\d.]+)'"
        if ($versionMatch) {
            $Version = $versionMatch.Matches.Groups[1].Value
        }
    }
}
if (-not $Version) {
    $Version = '2.1.4'
}

$script:Version = $Version

function Write-BuildHeader {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Write-BuildStep {
    param([string]$Message)
    Write-Host "  → $Message" -ForegroundColor Yellow
}

function Write-BuildSuccess {
    param([string]$Message)
    Write-Host "  ✓ $Message" -ForegroundColor Green
}

function Write-BuildError {
    param([string]$Message)
    Write-Host "  ✗ $Message" -ForegroundColor Red
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
        Name = $Name
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

function Step-RestoreDependencies {
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
        Where-Object { $_.Name -notmatch 'Tests\.ps1$' }
    
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
    
    $results = Invoke-ScriptAnalyzer -Path (Join-Path $ProjectRoot 'Redball.ps1') -Severity Warning, Error
    
    if ($results) {
        $results | Format-Table -AutoSize
        $errors = $results | Where-Object { $_.Severity -eq 'Error' }
        if ($errors) {
            throw "PSScriptAnalyzer found errors!"
        }
        $warnings = $results | Where-Object { $_.Severity -eq 'Warning' }
        Write-Host "  ⚠ $($warnings.Count) warnings (non-blocking)" -ForegroundColor Yellow
    }
    else {
        Write-BuildSuccess "No issues found"
    }
}

function Step-RunTests {
    Write-BuildHeader "Running Pester Tests"
    
    Import-Module Pester -RequiredVersion 5.5.0 -Force
    
    $testPath = Join-Path $ProjectRoot 'tests' 'Redball.Tests.ps1'
    if (-not (Test-Path $testPath)) {
        Write-Warning "Test file not found: $testPath"
        return
    }
    
    $config = New-PesterConfiguration
    $config.Run.Path = $testPath
    $config.Run.PassThru = $true
    $config.Output.Verbosity = 'Detailed'
    $config.TestResult.Enabled = $true
    $config.TestResult.OutputPath = Join-Path $DistPath 'test-results.xml'
    $config.CodeCoverage.Enabled = $true
    $config.CodeCoverage.OutputPath = Join-Path $DistPath 'coverage.xml'
    
    $result = Invoke-Pester -Configuration $config
    
    if ($result.FailedCount -gt 0) {
        foreach ($f in $result.Failed) {
            Write-BuildError "$($f.ExpandedName): $($f.ErrorRecord)"
        }
        throw "Pester tests failed: $($result.FailedCount)"
    }
    
    Write-BuildSuccess "All tests passed: $($result.PassedCount)"
    
    # Check coverage threshold
    if ($result.CodeCoverage) {
        $coverage = [math]::Round($result.CodeCoverage.CoveragePercent, 1)
        Write-Host "  Code coverage: $coverage%" -ForegroundColor Cyan
        if ($coverage -lt 40) {
            Write-Warning "Code coverage is below 40% threshold"
        }
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
        --self-contained true `
        --runtime win-x64 `
        --property:PublishSingleFile=true `
        --property:PublishTrimmed=false `
        --no-build
    
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed"
    }
    Write-BuildSuccess "Published to: $publishDir"
    
    # List output
    $exePath = Join-Path $publishDir 'Redball.UI.WPF.exe'
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        Write-Host "  Executable: $($fileInfo.Name) ($([math]::Round($fileInfo.Length / 1MB, 2)) MB)" -ForegroundColor Gray
    }
}

function Step-BuildMSI {
    Write-BuildHeader "Building MSI Installer"
    
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
    Write-BuildHeader "Creating Release Package"
    
    $releaseName = "Redball-v$Version"
    $releaseDir = Join-Path $DistPath $releaseName
    $zipPath = Join-Path $DistPath "$releaseName.zip"
    
    # Clean up existing
    if (Test-Path $releaseDir) {
        Remove-Item $releaseDir -Recurse -Force
    }
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    
    # Create release directory
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
    
    # Copy core files
    $filesToCopy = @(
        'Redball.ps1'
        'Redball.json'
        'locales.json'
        'README.md'
        'LICENSE'
        'docs/CHANGELOG.md'
        'docs/THIRD-PARTY-NOTICES.md'
    )
    
    foreach ($file in $filesToCopy) {
        $source = Join-Path $ProjectRoot $file
        if (Test-Path $source) {
            Copy-Item $source $releaseDir -Force
        }
    }
    
    # Copy WPF executable if built
    $wpfExe = Join-Path $DistPath 'wpf-publish' 'Redball.UI.WPF.exe'
    if (Test-Path $wpfExe) {
        $uiDir = Join-Path $releaseDir 'UI'
        New-Item -ItemType Directory -Path $uiDir -Force | Out-Null
        Copy-Item $wpfExe $uiDir -Force
        # Copy any additional WPF files
        $wpfPublishDir = Join-Path $DistPath 'wpf-publish'
        Get-ChildItem $wpfPublishDir -File | Where-Object { $_.Extension -in '.dll', '.pdb', '.json' } | 
            ForEach-Object { Copy-Item $_.FullName $uiDir -Force }
    }
    
    # Create zip
    Compress-Archive -Path $releaseDir -DestinationPath $zipPath -Force
    
    Write-BuildSuccess "Release package created: $zipPath"
    
    # Output summary
    $zipInfo = Get-Item $zipPath
    Write-Host "  Size: $([math]::Round($zipInfo.Length / 1MB, 2)) MB" -ForegroundColor Gray
}

#endregion

# Main Build Process
try {
    Write-Host @"
    ____           __    __         ____        _ _     _       
   / __ \___  ____/ /_  / /__      / __ )__  __(_) |   (_)___   
  / /_/ / _ \/ __  / / / / _ \    / __  / / / / / /   / / __ \  
 / _, _/  __/ /_/ / /_/ /  __/   / /_/ / /_/ / / /___/ / /_/ /  
/_/ |_|\___/\__,_/\__,_/\___/   /_____/\__,_/_/_/_____/ ____/   
                                                      /_/        
"@ -ForegroundColor Red
    
    Write-Host "  Building Redball v$Version ($Configuration)`n" -ForegroundColor Cyan
    
    # Ensure dist directory exists
    if (-not (Test-Path $DistPath)) {
        New-Item -ItemType Directory -Path $DistPath -Force | Out-Null
    }
    
    # Always run these
    Step-RestoreDependencies
    Step-ValidateJson
    
    # Security scan
    if (-not $SkipSecurity) {
        Step-RunSecurityScan
    }
    else {
        Write-Host "  Skipping security scan ( -SkipSecurity )" -ForegroundColor Yellow
    }
    
    # Lint
    if (-not $SkipLint) {
        Step-RunLinting
    }
    else {
        Write-Host "  Skipping linting ( -SkipLint )" -ForegroundColor Yellow
    }
    
    # Tests
    if (-not $SkipTests) {
        Step-RunTests
    }
    else {
        Write-Host "  Skipping tests ( -SkipTests )" -ForegroundColor Yellow
    }
    
    # WPF Build
    if (-not $SkipWPF) {
        Step-BuildWPF
    }
    else {
        Write-Host "  Skipping WPF build ( -SkipWPF )" -ForegroundColor Yellow
    }
    
    # MSI Build
    if (-not $SkipMSI) {
        Step-BuildMSI
    }
    else {
        Write-Host "  Skipping MSI build ( -SkipMSI )" -ForegroundColor Yellow
    }
    
    # Release package (only if not skipping everything)
    if ($BuildAll -or (-not $SkipWPF -and -not $SkipTests)) {
        Step-CreateReleasePackage
    }
    
    Write-Host "`n══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "  BUILD SUCCEEDED" -ForegroundColor Green
    Write-Host "══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "  Version:  $Version" -ForegroundColor Gray
    Write-Host "  Config:   $Configuration" -ForegroundColor Gray
    Write-Host "  Output:   $((Resolve-Path $DistPath).Path)" -ForegroundColor Gray
    Write-Host "══════════════════════════════════════════════════════════`n" -ForegroundColor Green
    
    exit 0
}
catch {
    Write-Host "`n══════════════════════════════════════════════════════════" -ForegroundColor Red
    Write-Host "  BUILD FAILED" -ForegroundColor Red
    Write-Host "══════════════════════════════════════════════════════════" -ForegroundColor Red
    Write-Host "  Error: $_" -ForegroundColor Red
    Write-Host "══════════════════════════════════════════════════════════`n" -ForegroundColor Red
    exit 1
}
