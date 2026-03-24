#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$WixBinPath = "C:\Program Files\WiX Toolset v4.0\bin",
    [string]$Configuration = "Release",
    [string]$AddLocalFeatures = '',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

# Accept WiX v4+ OSMF EULA
$env:WIX_OSMF_EULA_ACCEPTED = '1'

function Write-HostSafe {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'Build script requires console output for user feedback')]
    param(
        [Parameter(Mandatory, Position = 0)]
        [object]$Object,
        [System.ConsoleColor]$ForegroundColor
    )
    Write-Host $Object -ForegroundColor $ForegroundColor
}

$scriptRoot = $PSScriptRoot
if (-not $scriptRoot -and $MyInvocation.MyCommand.Path) { 
    $scriptRoot = Split-Path $MyInvocation.MyCommand.Path -Parent 
}
if (-not $scriptRoot) { $scriptRoot = Get-Location }

$projectRoot = Split-Path $scriptRoot -Parent
$wxsPath = Join-Path $scriptRoot 'Redball.v2.wxs'
$iconPath = Join-Path $scriptRoot 'Redball.ico'
$licenseSourcePath = Join-Path $projectRoot 'LICENSE'
$licenseRtfPath = Join-Path $scriptRoot 'Redball-License.rtf'
$outputDir = Join-Path $projectRoot 'dist'
$scriptsDir = Join-Path $projectRoot 'scripts'
$versionFilePath = Join-Path $scriptsDir 'version.txt'

# Custom Action project paths
$customActionDir = Join-Path $scriptRoot 'Redball.Installer.CustomActions'
$customActionProj = Join-Path $customActionDir 'Redball.Installer.CustomActions.csproj'
# Output goes to root bin folder per Directory.Build.props - use the .CA.dll native wrapper
$customActionDll = Join-Path $projectRoot "bin\$Configuration\net472\Redball.Installer.CustomActions.CA.dll"

Write-HostSafe "=== Redball MSI Builder v2.0 ===" -ForegroundColor Cyan
Write-HostSafe "Building self-contained installer with native custom actions...`n" -ForegroundColor Gray

# Read version
if (-not $Version -and (Test-Path $versionFilePath)) {
    $Version = Get-Content $versionFilePath -Raw
    $Version = $Version.Trim()
    Write-HostSafe "Version: $Version" -ForegroundColor Cyan
}

if ($Version) {
    $outputMsi = Join-Path $outputDir "Redball-$Version.msi"
}
else {
    $outputMsi = Join-Path $outputDir 'Redball.msi'
    $Version = '2.1.0'
}

$requiredExtensions = @('WixToolset.UI.wixext', 'WixToolset.Util.wixext')

# === GRAPHICS GENERATION ===
function New-RedballBannerBmp {
    param([Parameter(Mandatory)] [string]$Path)
    
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap(493, 58)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    
    # White background with subtle gradient
    $g.Clear([System.Drawing.Color]::White)
    
    # Redball brand accent line
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 30, 30), 2)
    $g.DrawLine($pen, 0, 56, 493, 56)
    $pen.Dispose()
    $g.Dispose()
    
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    Write-HostSafe "Generated banner: $Path" -ForegroundColor DarkGreen
}

function New-RedballDialogBmp {
    param([Parameter(Mandatory)] [string]$Path)
    
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap(493, 312)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    
    # Gradient background
    $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Rectangle 0, 0, 493, 312),
        [System.Drawing.Color]::White,
        [System.Drawing.Color]::FromArgb(248, 248, 248),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical
    )
    $g.FillRectangle($background, 0, 0, 493, 312)
    $background.Dispose()
    
    # Left accent bar
    $accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 30, 30))
    $g.FillRectangle($accentBrush, 0, 0, 18, 312)
    $accentBrush.Dispose()
    $g.Dispose()
    
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    Write-HostSafe "Generated dialog: $Path" -ForegroundColor DarkGreen
}

function New-RedballInstallerIconFile {
    param([Parameter(Mandatory)] [string]$Path)
    if (Test-Path $Path) { return }
    
    Add-Type -AssemblyName System.Drawing
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    try {
        [System.Drawing.SystemIcons]::Information.Save($stream)
    }
    finally {
        $stream.Dispose()
    }
    Write-HostSafe "Generated icon: $Path" -ForegroundColor DarkGreen
}

function New-RedballInstallerLicenseRtf {
    param(
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [string]$OutputPath
    )
    if (-not (Test-Path $SourcePath)) {
        throw "License source not found: $SourcePath"
    }
    
    $licenseText = Get-Content -LiteralPath $SourcePath -Raw
    $licenseText = $licenseText -replace '(?m)^#+\s*', ''
    $escaped = $licenseText -replace '\\', '\\\\' -replace '{', '\{' -replace '}', '\}'
    $escaped = $escaped -replace "`r`n", '\par ' -replace "`n", '\par '
    $rtfContent = "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs20 $escaped}"
    
    Set-Content -LiteralPath $OutputPath -Value $rtfContent -Encoding Ascii -NoNewline
    Write-HostSafe "Generated license: $OutputPath" -ForegroundColor DarkGreen
}

# === CUSTOM ACTION BUILD ===
function Step-BuildCustomActions {
    Write-HostSafe "Building Custom Action DLL..." -ForegroundColor Cyan
    
    if (-not (Test-Path $customActionProj)) {
        throw "Custom action project not found: $customActionProj"
    }
    
    # Build the custom action DLL
    $buildArgs = @(
        'build',
        $customActionProj,
        '--configuration', $Configuration,
        '--verbosity', 'minimal'
    )
    
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw ".NET SDK not found"
    }
    
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Custom action build failed"
    }
    
    if (-not (Test-Path $customActionDll)) {
        throw "Custom action DLL not found after build: $customActionDll"
    }
    
    Write-HostSafe "Custom Action DLL built: $customActionDll" -ForegroundColor Green
}

# === WIX SETUP ===
function Install-WixExtensionIfMissing {
    param(
        [Parameter(Mandatory)] [string]$WixExe,
        [Parameter(Mandatory)] [string]$ExtensionId
    )
    
    $installed = $false
    $extensionList = & $WixExe extension list -g 2>$null
    if ($LASTEXITCODE -eq 0 -and $extensionList) {
        $installed = [bool]($extensionList | Where-Object { $_ -match [regex]::Escape($ExtensionId) })
    }
    
    if (-not $installed) {
        Write-HostSafe "Installing WiX extension: $ExtensionId" -ForegroundColor Yellow
        & $WixExe extension add -g $ExtensionId
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Failed to install WiX extension '$ExtensionId'"
            return $false
        }
    }
    return $true
}

# === MAIN BUILD ===
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Generate graphics assets
New-RedballInstallerIconFile -Path $iconPath
New-RedballInstallerLicenseRtf -SourcePath $licenseSourcePath -OutputPath $licenseRtfPath
New-RedballBannerBmp -Path (Join-Path $scriptRoot 'banner.bmp')
New-RedballDialogBmp -Path (Join-Path $scriptRoot 'dialog.bmp')

# Build custom actions
Step-BuildCustomActions

# Find WiX
$wixCandidates = @(
    (Join-Path $WixBinPath 'wix.exe'),
    (Join-Path (Join-Path $env:USERPROFILE '.dotnet') 'tools\wix.exe'),
    'C:\Tools\wix\wix.exe',
    'C:\Users\Administrator\.dotnet\tools\wix.exe',
    (Join-Path $env:ProgramFiles 'WiX Toolset v4.0\bin\wix.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'WiX Toolset v4.0\bin\wix.exe')
)

$wixFromPath = (Get-Command wix.exe -ErrorAction SilentlyContinue).Source
if ($wixFromPath) { $wixCandidates += $wixFromPath }

$wixExe = $wixCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $wixExe) {
    throw "WiX CLI not found. Install WiX v4+ and retry."
}

Write-HostSafe "Using WiX: $wixExe" -ForegroundColor Gray

# Ensure required extensions
foreach ($extension in $requiredExtensions) {
    $null = Install-WixExtensionIfMissing -WixExe $wixExe -ExtensionId $extension
}

# Prepare WiX version format
$wixVersion = $Version
$versionParts = $Version.Split('.')
if ($versionParts.Count -eq 3) {
    $wixVersion = "$Version.0"
}

Write-HostSafe "Building MSI: Redball v$Version..." -ForegroundColor Cyan

# Build arguments
$buildArgs = @(
    'build',
    $wxsPath,
    '-o', $outputMsi,
    '-d', "ProductVersion=$wixVersion",
    '-d', "CA_PATH=$customActionDll"
)

foreach ($extension in $requiredExtensions) {
    $buildArgs += '-ext'
    $buildArgs += $extension
}

if ($AddLocalFeatures) {
    $buildArgs += '-d'
    $buildArgs += "DEFAULT_ADDLOCAL=$AddLocalFeatures"
}

# Build the MSI
Push-Location $scriptRoot
try {
    Write-HostSafe "Compiling MSI..." -ForegroundColor Yellow
    & $wixExe @buildArgs
}
finally {
    Pop-Location
}

if ($LASTEXITCODE -ne 0) {
    throw "WiX build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $outputMsi)) {
    throw "MSI build completed but output file not found: $outputMsi"
}

$msiInfo = Get-Item $outputMsi
Write-HostSafe "" -ForegroundColor Green
Write-HostSafe "══════════════════════════════════════════════════════════" -ForegroundColor Green
Write-HostSafe "  MSI BUILD SUCCESSFUL" -ForegroundColor Green
Write-HostSafe "══════════════════════════════════════════════════════════" -ForegroundColor Green
Write-HostSafe "  File: $($msiInfo.Name)" -ForegroundColor White
Write-HostSafe "  Size: $([math]::Round($msiInfo.Length / 1MB, 2)) MB" -ForegroundColor White
Write-HostSafe "  Path: $outputMsi" -ForegroundColor Gray
Write-HostSafe "  Features: Self-contained, Native Custom Actions, Live Log UI" -ForegroundColor Gray
Write-HostSafe "══════════════════════════════════════════════════════════" -ForegroundColor Green

# Also copy to Redball.msi for convenience
$defaultMsiPath = Join-Path $outputDir 'Redball.msi'
Copy-Item $outputMsi $defaultMsiPath -Force -ErrorAction SilentlyContinue

exit 0
