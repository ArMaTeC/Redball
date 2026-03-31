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
# Output is in the project bin folder
$customActionDll = Join-Path $customActionDir "bin\$Configuration\net472\Redball.Installer.CustomActions.dll"

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
    # WiX v4+ banner size: 493x58 pixels
    $width = 493
    $height = 58
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    
    # Modern gradient background: Dark professional gradient
    $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        ([System.Drawing.Rectangle]::new(0, 0, $width, $height)),
        [System.Drawing.Color]::FromArgb(32, 32, 40),
        [System.Drawing.Color]::FromArgb(24, 24, 32),
        [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal
    )
    $g.FillRectangle($background, 0, 0, $width, $height)
    $background.Dispose()
    
    # Add subtle pattern overlay for texture
    for ($i = 0; $i -lt $width; $i += 20) {
        $opacity = 3
        $linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($opacity, 255, 255, 255), 1)
        $g.DrawLine($linePen, $i, 0, $i + 10, $height)
        $linePen.Dispose()
    }
    
    # Red accent gradient line at bottom
    $accentGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        ([System.Drawing.Rectangle]::new(0, $height - 3, $width, 3)),
        [System.Drawing.Color]::FromArgb(220, 53, 69),
        [System.Drawing.Color]::FromArgb(180, 40, 50),
        [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal
    )
    $g.FillRectangle($accentGradient, 0, $height - 3, $width, 3)
    $accentGradient.Dispose()
    
    # Draw Redball logo text with shadow
    $shadowOffset = 1
    $titleFont = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
    $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(60, 0, 0, 0))
    $titleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    
    $titleText = 'Redball'
    $titleX = 20
    $titleY = 14
    
    # Shadow
    $g.DrawString($titleText, $titleFont, $shadowBrush, $titleX + $shadowOffset, $titleY + $shadowOffset)
    # Main text
    $g.DrawString($titleText, $titleFont, $titleBrush, $titleX, $titleY)
    
    $titleFont.Dispose()
    $shadowBrush.Dispose()
    $titleBrush.Dispose()
    
    # Draw subtle "powered by .NET" badge on right
    $badgeFont = New-Object System.Drawing.Font('Segoe UI', 7, [System.Drawing.FontStyle]::Regular)
    $badgeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
    $badgeText = 'Windows Desktop Application'
    $badgeSize = $g.MeasureString($badgeText, $badgeFont)
    $g.DrawString($badgeText, $badgeFont, $badgeBrush, $width - $badgeSize.Width - 15, $height - 20)
    $badgeFont.Dispose()
    $badgeBrush.Dispose()
    
    # Left accent bar (gradient)
    $leftBarGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        ([System.Drawing.Rectangle]::new(0, 0, 6, $height)),
        [System.Drawing.Color]::FromArgb(220, 53, 69),
        [System.Drawing.Color]::FromArgb(180, 40, 50),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical
    )
    $g.FillRectangle($leftBarGradient, 0, 0, 6, $height)
    $leftBarGradient.Dispose()
    
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    Write-HostSafe "Generated banner: $Path" -ForegroundColor DarkGreen
}

function New-RedballDialogBmp {
    param([Parameter(Mandatory)] [string]$Path)
    
    Add-Type -AssemblyName System.Drawing
    # WiX v4+ dialog size: 493x312 pixels
    $width = 493
    $height = 312
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    
    # Modern dark gradient background
    $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        ([System.Drawing.Rectangle]::new(0, 0, $width, $height)),
        [System.Drawing.Color]::FromArgb(28, 28, 36),
        [System.Drawing.Color]::FromArgb(20, 20, 28),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical
    )
    $g.FillRectangle($background, 0, 0, $width, $height)
    $background.Dispose()
    
    # Add subtle dot pattern for texture
    $random = New-Object System.Random
    for ($i = 0; $i -lt 150; $i++) {
        $x = $random.Next(0, $width)
        $y = $random.Next(0, $height)
        $opacity = $random.Next(5, 15)
        $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($opacity, 255, 255, 255))
        $g.FillEllipse($dotBrush, $x, $y, 2, 2)
        $dotBrush.Dispose()
    }
    
    # Top accent bar with gradient
    $topGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        ([System.Drawing.Rectangle]::new(0, 0, $width, 4)),
        [System.Drawing.Color]::FromArgb(220, 53, 69),
        [System.Drawing.Color]::FromArgb(180, 40, 50),
        [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal
    )
    $g.FillRectangle($topGradient, 0, 0, $width, 4)
    $topGradient.Dispose()
    
    # Left accent bar (gradient red)
    $accentGradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        ([System.Drawing.Rectangle]::new(0, 0, 8, $height)),
        [System.Drawing.Color]::FromArgb(220, 53, 69),
        [System.Drawing.Color]::FromArgb(150, 35, 45),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical
    )
    $g.FillRectangle($accentGradient, 0, 0, 8, $height)
    $accentGradient.Dispose()
    
    # Draw Redball branding in upper area
    $brandFont = New-Object System.Drawing.Font('Segoe UI', 24, [System.Drawing.FontStyle]::Bold)
    $brandBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $brandShadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(40, 0, 0, 0))
    
    $brandText = 'Redball'
    $brandX = 35
    $brandY = 35
    
    # Shadow effect
    $g.DrawString($brandText, $brandFont, $brandShadowBrush, $brandX + 2, $brandY + 2)
    # Main text
    $g.DrawString($brandText, $brandFont, $brandBrush, $brandX, $brandY)
    
    $brandFont.Dispose()
    $brandBrush.Dispose()
    $brandShadowBrush.Dispose()
    
    # Tagline
    $taglineFont = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Regular)
    $taglineBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(180, 180, 190))
    $g.DrawString('Keep-Alive Automation for Windows', $taglineFont, $taglineBrush, 35, 70)
    $taglineFont.Dispose()
    $taglineBrush.Dispose()
    
    # Feature highlights with icons (text representation)
    $featureFont = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Regular)
    $featureBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(140, 140, 150))
    $bulletBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(220, 53, 69))
    
    $features = @(
        @('Keep-Awake Engine', 115),
        @('TypeThing Typing Automation', 135),
        @('Pomodoro Timer', 155),
        @('Mini Widget', 175)
    )
    
    foreach ($feature in $features) {
        $text = $feature[0]
        $y = $feature[1]
        
        # Draw bullet (small circle)
        $g.FillEllipse($bulletBrush, 35, $y + 4, 6, 6)
        
        # Draw text
        $g.DrawString($text, $featureFont, $featureBrush, 48, $y)
    }
    
    $featureFont.Dispose()
    $featureBrush.Dispose()
    $bulletBrush.Dispose()
    
    # Bottom area - copyright/version info
    $infoFont = New-Object System.Drawing.Font('Segoe UI', 8, [System.Drawing.FontStyle]::Regular)
    $infoBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100, 100, 110))
    $g.DrawString('© Redball Project  Microsoft Windows Compatible', $infoFont, $infoBrush, 35, $height - 30)
    $infoFont.Dispose()
    $infoBrush.Dispose()
    
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
