<#
.SYNOPSIS
    Redball Unified Build Script for Windows

.DESCRIPTION
    Orchestrates all build operations: Windows, Linux (via WSL), Update Server, Website

.PARAMETER Command
    Build command to execute:
    - all: Build everything (Windows, update-server, website)
    - windows: Build Windows artifacts (WPF, Service, Setup, ZIP)
    - linux: Build Linux artifacts via WSL (if available)
    - update-server: Build/validate update-server
    - website: Build website (validates files)
    - clean: Clean all build artifacts
    - publish: Publish release to update-server
    - serve: Start update-server
    - status: Show build status and available artifacts

.PARAMETER Channel
    Release channel (stable, beta, dev) - default: stable

.PARAMETER Beta
    Shortcut for -Channel beta

.PARAMETER Version
    Specify version for publish

.PARAMETER SkipWindows
    Skip Windows build in 'all' command

.PARAMETER SkipLinux
    Skip Linux build in 'all' command

.PARAMETER DryRun
    Show what would happen without making changes

.EXAMPLE
    .\scripts\build.ps1 all
    Build everything

.EXAMPLE
    .\scripts\build.ps1 windows
    Build only Windows artifacts

.EXAMPLE
    .\scripts\build.ps1 publish -Beta -Version "2.1.80"
    Publish beta release

.EXAMPLE
    .\scripts\build.ps1 serve
    Start update-server

.EXAMPLE
    .\scripts\build.ps1 status
    Show build status
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('all', 'windows', 'linux', 'update-server', 'website', 'clean', 'publish', 'serve', 'status')]
    [string]$Command,
    
    [Parameter()]
    [ValidateSet('stable', 'beta', 'dev')]
    [string]$Channel = 'stable',
    
    [Parameter()]
    [switch]$Beta,
    
    [Parameter()]
    [string]$Version = '',
    
    [Parameter()]
    [switch]$SkipWindows,
    
    [Parameter()]
    [switch]$SkipLinux,
    
    [Parameter()]
    [switch]$DryRun
)

# === Configuration ===
$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$DistDir = Join-Path $ProjectRoot 'dist'
$UpdateServerDir = Join-Path $ProjectRoot 'update-server'

# Apply Beta flag
if ($Beta) {
    $Channel = 'beta'
}

# === Logging Functions ===
function Write-Step { Write-Host "[STEP] $args" -ForegroundColor Cyan }
function Write-Success { Write-Host "[OK] $args" -ForegroundColor Green }
function Write-Warn { Write-Host "[WARN] $args" -ForegroundColor Yellow }
function Write-Error { Write-Host "[ERROR] $args" -ForegroundColor Red }
function Write-Info { Write-Host "[INFO] $args" -ForegroundColor Blue }
function Write-Debug { Write-Host "[DEBUG] $args" -ForegroundColor Gray }

# === Helper Functions ===

function Get-ProjectVersion {
    if ($Version) {
        return $Version
    }
    
    $versionFile = Join-Path $ScriptDir 'version.txt'
    if (Test-Path $versionFile) {
        return (Get-Content $versionFile -Raw).Trim()
    }
    
    return '2.1.19'
}

function Test-Dependencies {
    $missing = @()
    
    # Check for node/npm
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        $missing += 'node'
    }
    
    # Check for dotnet
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        $missing += 'dotnet'
    }
    
    if ($missing.Count -gt 0) {
        Write-Warn "Missing dependencies: $($missing -join ', ')"
        Write-Info "Some build operations may fail"
    }
}

# === Build Commands ===

function Build-Windows {
    Write-Step "Building Windows artifacts..."
    
    $buildScript = Join-Path $ScriptDir 'windows\build.ps1'
    
    if ($DryRun) {
        Write-Info "[DRY RUN] Would run: $buildScript"
        return
    }
    
    if (-not (Test-Path $buildScript)) {
        Write-Error "Windows build script not found: $buildScript"
        throw "Build script not found"
    }
    
    & $buildScript
    
    if ($LASTEXITCODE -ne 0) {
        throw "Windows build failed with exit code $LASTEXITCODE"
    }
    
    Write-Success "Windows build completed"
}

function Build-Linux {
    Write-Step "Building Linux artifacts..."
    
    if ($DryRun) {
        Write-Info "[DRY RUN] Would run Linux build via WSL"
        return
    }
    
    # Check if WSL is available
    if (-not (Get-Command wsl -ErrorAction SilentlyContinue)) {
        Write-Warn "WSL not available - skipping Linux build"
        Write-Info "Install WSL to build Linux artifacts on Windows"
        return
    }
    
    $linuxBuildScript = 'scripts/linux/build-linux.sh'
    
    Write-Info "Running Linux build via WSL..."
    wsl bash -c "cd /mnt/c/$(($ProjectRoot -replace '\\','/') -replace 'C:','') && ./$linuxBuildScript -a"
    
    if ($LASTEXITCODE -ne 0) {
        throw "Linux build failed with exit code $LASTEXITCODE"
    }
    
    Write-Success "Linux build completed"
}

function Build-UpdateServer {
    Write-Step "Building update-server..."
    
    if (-not (Test-Path $UpdateServerDir)) {
        Write-Error "Update server directory not found: $UpdateServerDir"
        throw "Update server not found"
    }
    
    if ($DryRun) {
        Write-Info "[DRY RUN] Would run: npm install in $UpdateServerDir"
        return
    }
    
    Push-Location $UpdateServerDir
    try {
        # Install dependencies if needed
        if (-not (Test-Path 'node_modules')) {
            Write-Info "Installing npm dependencies..."
            npm install --silent
        }
        
        # Validate server
        Write-Info "Validating server..."
        npm run build
        
        Write-Success "Update server ready"
    }
    finally {
        Pop-Location
    }
}

function Build-Website {
    Write-Step "Building website..."
    
    $websiteFile = Join-Path $UpdateServerDir 'public\index.html'
    
    if (-not (Test-Path $websiteFile)) {
        Write-Error "Website file not found: $websiteFile"
        throw "Website not found"
    }
    
    if ($DryRun) {
        Write-Info "[DRY RUN] Would validate: $websiteFile"
        return
    }
    
    # Validate HTML (basic check)
    $content = Get-Content $websiteFile -Raw
    if ($content -match 'TypeThing') {
        Write-Success "Website validated: $websiteFile"
    }
    else {
        Write-Warn "Website may need updates"
    }
}

function Build-All {
    Write-Step "Building all components..."
    
    $startTime = Get-Date
    
    # Build update server first
    Build-UpdateServer
    
    # Build website
    Build-Website
    
    # Build Windows if not skipped
    if (-not $SkipWindows) {
        Build-Windows
    }
    else {
        Write-Info "Skipping Windows build (--SkipWindows)"
    }
    
    # Build Linux if not skipped
    if (-not $SkipLinux) {
        Build-Linux
    }
    else {
        Write-Info "Skipping Linux build (--SkipLinux)"
    }
    
    $duration = (Get-Date) - $startTime
    
    Write-Host ""
    Write-Host "  ╔══════════════════════════════════════════════════╗"
    Write-Host "  ║  BUILD COMPLETED                                 ║"
    Write-Host "  ╚══════════════════════════════════════════════════╝"
    Write-Host ""
    Write-Host "  Duration: $($duration.Minutes)m $($duration.Seconds)s"
    Write-Host ""
}

function Clear-BuildArtifacts {
    Write-Step "Cleaning build artifacts..."
    
    if ($DryRun) {
        Write-Info "[DRY RUN] Would remove: $DistDir"
        return
    }
    
    # Clean dist directory
    if (Test-Path $DistDir) {
        Remove-Item -Path $DistDir -Recurse -Force
        Write-Success "Removed: $DistDir"
    }
    
    Write-Success "Clean completed"
}

function Publish-Release {
    Write-Step "Publishing release (channel: $Channel)..."
    
    $projectVersion = Get-ProjectVersion
    
    if ($DryRun) {
        Write-Info "[DRY RUN] Would publish version $projectVersion to channel $Channel"
        return
    }
    
    # Check if WSL is available for running release script
    if (-not (Get-Command wsl -ErrorAction SilentlyContinue)) {
        Write-Error "WSL required for publishing releases"
        Write-Info "Install WSL or run release script manually on Linux"
        throw "WSL not available"
    }
    
    $releaseScript = 'scripts/linux/release.sh'
    
    Write-Info "Running release script via WSL..."
    wsl bash -c "cd /mnt/c/$(($ProjectRoot -replace '\\','/') -replace 'C:','') && ./$releaseScript -v $projectVersion --channel $Channel"
    
    if ($LASTEXITCODE -ne 0) {
        throw "Release failed with exit code $LASTEXITCODE"
    }
    
    Write-Success "Release published: $projectVersion ($Channel)"
}

function Start-UpdateServer {
    Write-Step "Starting update-server..."
    
    if (-not (Test-Path $UpdateServerDir)) {
        Write-Error "Update server directory not found: $UpdateServerDir"
        throw "Update server not found"
    }
    
    Push-Location $UpdateServerDir
    try {
        # Ensure dependencies are installed
        if (-not (Test-Path 'node_modules')) {
            Write-Info "Installing npm dependencies..."
            npm install --silent
        }
        
        Write-Info "Server starting on http://localhost:3500"
        Write-Info "Press Ctrl+C to stop"
        Write-Host ""
        
        npm start
    }
    finally {
        Pop-Location
    }
}

function Show-Status {
    Write-Host ""
    Write-Host "  ╔══════════════════════════════════════════════════╗"
    Write-Host "  ║  Redball Build Status                            ║"
    Write-Host "  ╚══════════════════════════════════════════════════╝"
    Write-Host ""
    
    $projectVersion = Get-ProjectVersion
    Write-Host "  Version: $projectVersion"
    Write-Host ""
    
    # Check Windows artifacts
    Write-Host "  Windows Artifacts:"
    if (Test-Path $DistDir) {
        $found = $false
        
        $setupExe = Get-ChildItem -Path $DistDir -Filter 'Redball-*-Setup.exe' -ErrorAction SilentlyContinue
        $zipFile = Get-ChildItem -Path $DistDir -Filter 'Redball-*.zip' -ErrorAction SilentlyContinue
        $standaloneExe = Join-Path $DistDir 'wpf-publish\Redball.UI.WPF.exe'
        
        if ($setupExe) {
            $size = [math]::Round($setupExe.Length / 1MB, 2)
            Write-Host "    ✓ $($setupExe.Name) ($size MB)"
            $found = $true
        }
        
        if ($zipFile) {
            $size = [math]::Round($zipFile.Length / 1MB, 2)
            Write-Host "    ✓ $($zipFile.Name) ($size MB)"
            $found = $true
        }
        
        if (Test-Path $standaloneExe) {
            $size = [math]::Round((Get-Item $standaloneExe).Length / 1MB, 2)
            Write-Host "    ✓ Redball.UI.WPF.exe ($size MB)"
            $found = $true
        }
        
        if (-not $found) {
            Write-Host "    ✗ No Windows artifacts found"
        }
    }
    else {
        Write-Host "    ✗ No dist directory"
    }
    Write-Host ""
    
    # Check Linux artifacts
    Write-Host "  Linux Artifacts:"
    $linuxDistDir = Join-Path $DistDir 'linux'
    if (Test-Path $linuxDistDir) {
        $artifacts = Get-ChildItem -Path $linuxDistDir -Filter 'redball-*' -ErrorAction SilentlyContinue
        
        if ($artifacts) {
            foreach ($artifact in $artifacts) {
                $size = [math]::Round($artifact.Length / 1MB, 2)
                Write-Host "    ✓ $($artifact.Name) ($size MB)"
            }
        }
        else {
            Write-Host "    ✗ No Linux artifacts found"
        }
    }
    else {
        Write-Host "    ✗ No Linux dist directory"
    }
    Write-Host ""
    
    # Check update-server
    Write-Host "  Update Server:"
    $nodeModules = Join-Path $UpdateServerDir 'node_modules'
    if (Test-Path $nodeModules) {
        Write-Host "    ✓ Dependencies installed"
    }
    else {
        Write-Host "    ✗ Dependencies not installed (run: npm install)"
    }
    
    $dbFile = Join-Path $UpdateServerDir 'data\releases.json'
    if (Test-Path $dbFile) {
        $content = Get-Content $dbFile -Raw | ConvertFrom-Json
        $releaseCount = $content.releases.Count
        Write-Host "    ✓ Database: $releaseCount releases"
    }
    else {
        Write-Host "    ✗ No database found"
    }
    Write-Host ""
    
    # Check website
    Write-Host "  Website:"
    $websiteFile = Join-Path $UpdateServerDir 'public\index.html'
    if (Test-Path $websiteFile) {
        Write-Host "    ✓ index.html exists"
    }
    else {
        Write-Host "    ✗ index.html not found"
    }
    Write-Host ""
}

# === Main ===

function Main {
    Write-Host ""
    Write-Host "  ╔══════════════════════════════════════════════════╗"
    Write-Host "  ║  Redball Unified Build System                    ║"
    Write-Host "  ╚══════════════════════════════════════════════════╝"
    Write-Host ""
    
    # Default to 'all' if no command specified
    if (-not $Command) {
        $Command = 'all'
        Write-Info "No command specified, defaulting to 'all'"
        Write-Host ""
    }
    
    if ($DryRun) {
        Write-Warn "DRY RUN MODE - no changes will be made"
        Write-Host ""
    }
    
    Test-Dependencies
    
    try {
        switch ($Command) {
            'all' { Build-All }
            'windows' { Build-Windows }
            'linux' { Build-Linux }
            'update-server' { Build-UpdateServer }
            'website' { Build-Website }
            'clean' { Clear-BuildArtifacts }
            'publish' { Publish-Release }
            'serve' { Start-UpdateServer }
            'status' { Show-Status }
            default {
                Write-Error "Unknown command: $Command"
                exit 1
            }
        }
    }
    catch {
        Write-Error "Build failed: $_"
        exit 1
    }
}

Main
