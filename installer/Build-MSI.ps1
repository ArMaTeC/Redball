#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$WixBinPath = "C:\Program Files\WiX Toolset v4.0\bin",
    [string]$Configuration = "Release",
    [string]$AddLocalFeatures = '',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

# Accept WiX v7 OSMF EULA
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

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$wxsPath = Join-Path $scriptRoot 'Redball.wxs'
$iconPath = Join-Path $scriptRoot 'Redball.ico'
$licenseSourcePath = Join-Path $projectRoot 'LICENSE'
$licenseRtfPath = Join-Path $scriptRoot 'Redball-License.rtf'
$outputDir = Join-Path $projectRoot 'dist'
$versionFilePath = Join-Path $projectRoot 'scripts\version.txt'

# Read version from version.txt if not provided
if (-not $Version -and (Test-Path $versionFilePath)) {
    $Version = Get-Content $versionFilePath -Raw
    $Version = $Version.Trim()
    Write-HostSafe "Using version from version.txt: $Version" -ForegroundColor Cyan
}

# Use version in MSI filename if provided
if ($Version) {
    $outputMsi = Join-Path $outputDir "Redball-$Version.msi"
}
else {
    $outputMsi = Join-Path $outputDir 'Redball.msi'
}

$requiredExtensions = @('WixToolset.UI.wixext', 'WixToolset.Netfx.wixext', 'WixToolset.Bal.wixext')

function New-RedballBannerBmp {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string]$IconPath
    )

    # Always regenerate to ensure clean state (no stale text)
    Add-Type -AssemblyName System.Drawing

    $bmp = New-Object System.Drawing.Bitmap(493, 58)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

    # White background
    $g.Clear([System.Drawing.Color]::White)

    # Subtle red accent line at the bottom (Redball brand color)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 30, 30), 2)
    $g.DrawLine($pen, 0, 56, 493, 56)
    $pen.Dispose()

    # Draw icon on the left if available
    if ($IconPath -and (Test-Path $IconPath)) {
        try {
            $icon = New-Object System.Drawing.Icon($IconPath, 40, 40)
            $g.DrawIcon($icon, 8, 9)
            $icon.Dispose()
        }
        catch {
            Write-Verbose "Could not draw icon on banner: $_"
        }
    }

    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()

    Write-HostSafe "Generated clean installer banner at $Path" -ForegroundColor DarkGreen
}

function New-RedballInstallerIconFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        return
    }
    Add-Type -AssemblyName System.Drawing
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        [System.Drawing.SystemIcons]::Information.Save($stream)
    }
    finally {
        $stream.Dispose()
    }

    Write-Host "Generated installer icon at $Path" -ForegroundColor DarkGreen
}

function New-RedballInstallerLicenseRtf {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        throw "License source file not found: $SourcePath"
    }

    $licenseText = Get-Content -LiteralPath $SourcePath -Raw -ErrorAction Stop
    # Strip markdown heading markers (# ) that appear as literal text in the RTF
    $licenseText = $licenseText -replace '(?m)^#+\s*', ''
    $escaped = $licenseText -replace '\\', '\\\\' -replace '{', '\{' -replace '}', '\}'
    $escaped = $escaped -replace "`r`n", '\par ' -replace "`n", '\par '
    $rtfContent = "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs20 $escaped}"

    Set-Content -LiteralPath $OutputPath -Value $rtfContent -Encoding Ascii -NoNewline
    Write-HostSafe "Generated installer license at $OutputPath" -ForegroundColor DarkGreen
}

function Install-WixExtensionIfMissing {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WixExe,

        [Parameter(Mandatory = $true)]
        [string]$ExtensionId
    )

    $installed = $false
    $extensionList = & $WixExe extension list -g 2>$null
    if ($LASTEXITCODE -eq 0 -and $extensionList) {
        $installed = [bool]($extensionList | Where-Object { $_ -match [regex]::Escape($ExtensionId) })
    }

    if (-not $installed) {
        Write-Host "Installing missing WiX extension: $ExtensionId" -ForegroundColor Yellow
        & $WixExe extension add -g $ExtensionId
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Failed to install WiX extension '$ExtensionId'."
            Write-Host "Try running manually: `"$WixExe`" extension add $ExtensionId" -ForegroundColor DarkYellow
            return $false
        }
    }

    return $true
}

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

New-RedballInstallerIconFile -Path $iconPath
New-RedballInstallerLicenseRtf -SourcePath $licenseSourcePath -OutputPath $licenseRtfPath
New-RedballBannerBmp -Path (Join-Path $scriptRoot 'banner.bmp') -IconPath $iconPath

$wixCandidates = @(
    (Join-Path $WixBinPath 'wix.exe'),
    (Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'),
    'C:\Tools\wix\wix.exe',
    'C:\Users\Administrator\.dotnet\tools\wix.exe',
    (Join-Path ${env:ProgramFiles} 'WiX Toolset v4.0\bin\wix.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'WiX Toolset v4.0\bin\wix.exe')
)

$wixFromPath = (Get-Command wix.exe -ErrorAction SilentlyContinue).Source
if ($wixFromPath) {
    $wixCandidates += $wixFromPath
}

$wixExe = $wixCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $wixExe) {
    Write-Warning 'WiX CLI not found.'
    Write-HostSafe 'Install WiX v4 and retry.' -ForegroundColor Yellow
    Write-HostSafe 'Expected paths checked:' -ForegroundColor Yellow
    $wixCandidates | ForEach-Object { if ($_ ) { Write-HostSafe "  $_" -ForegroundColor DarkYellow } }
    Write-HostSafe 'You can also pass -WixBinPath "<path-to-wix-bin>".' -ForegroundColor Yellow
    return
}

Write-HostSafe "Building MSI from $wxsPath ..." -ForegroundColor Cyan

# Skip extension installation check - assume extensions are available via wix.exe
foreach ($extension in $requiredExtensions) {
    Write-HostSafe "Using WiX extension: $extension" -ForegroundColor Gray
}

$buildArgs = @(
    'build'
    $wxsPath
    '-o', $outputMsi
)

# Use explicit extension paths
$projectRoot = Split-Path -Parent $scriptRoot
$wixExtPath = Join-Path (Join-Path $projectRoot '.wix') 'extensions'
$uiExtPath = Join-Path (Join-Path $env:USERPROFILE '.wix') 'extensions'
$buildArgs += '-ext'
$buildArgs += (Join-Path (Join-Path (Join-Path (Join-Path $uiExtPath 'WixToolset.UI.wixext') '7.0.0-rc.2') 'wixext7') 'WixToolset.UI.wixext.dll')

if ($AddLocalFeatures) {
    $buildArgs += '-d'
    $buildArgs += "DEFAULT_ADDLOCAL=$AddLocalFeatures"
}

# Pass version to WiX
$cleanVersion = $Version.Trim()
$versionParts = $cleanVersion.Split('.')
if ($versionParts.Count -eq 3) {
    $wixVersion = "$cleanVersion.0"
}
else {
    $wixVersion = $cleanVersion
}
Write-HostSafe "Using WiX version: $wixVersion" -ForegroundColor Gray
$buildArgs += '-d'
$buildArgs += "ProductVersion=$wixVersion"

Push-Location $scriptRoot
try {
    & $wixExe @buildArgs
}
finally {
    Pop-Location
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "WiX build failed with exit code $LASTEXITCODE"
}

Write-HostSafe "MSI created: $outputMsi" -ForegroundColor Green

# Build the bundle (EXE installer that chains .NET 8 + MSI)
Write-HostSafe "Building bundle EXE..." -ForegroundColor Cyan

$bundleWxsPath = Join-Path $scriptRoot 'Bundle.wxs'
if (Test-Path $bundleWxsPath) {
    # Download .NET 8 Desktop Runtime installer for bundle (required at build time for hash)
    $dotnetInstallerPath = Join-Path $scriptRoot 'windowsdesktop-runtime-win-x64.exe'
    if (-not (Test-Path $dotnetInstallerPath)) {
        Write-HostSafe "Downloading .NET 8 Desktop Runtime installer..." -ForegroundColor Yellow
        try {
            $dotnetUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe'
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $dotnetUrl -OutFile $dotnetInstallerPath -UseBasicParsing
            $ProgressPreference = 'Continue'
            $dlSize = [math]::Round((Get-Item $dotnetInstallerPath).Length / 1MB, 1)
            Write-HostSafe "Downloaded .NET 8 runtime ($dlSize MB)" -ForegroundColor Green
        }
        catch {
            Write-Warning "Failed to download .NET 8 runtime: $_"
            Write-Warning "Bundle build skipped. MSI was created successfully."
            return
        }
    }
    else {
        Write-HostSafe "Using cached .NET 8 runtime installer" -ForegroundColor Gray
    }

    if ($Version) {
        $outputBundle = Join-Path $outputDir "Redball-Setup-$Version.exe"
    }
    else {
        $outputBundle = Join-Path $outputDir 'Redball-Setup.exe'
    }
    
    $bundleArgs = @(
        'build'
        $bundleWxsPath
        '-o', $outputBundle
    )
    
    # Add explicit extension paths for bundle (Netfx for DotNetCoreSearch, Bal for bootstrapper UI)
    $bundleArgs += '-ext'
    $bundleArgs += (Join-Path (Join-Path (Join-Path (Join-Path $wixExtPath 'WixToolset.Netfx.wixext') '7.0.0-rc.2') 'wixext7') 'WixToolset.Netfx.wixext.dll')
    $bundleArgs += '-ext'
    $bundleArgs += (Join-Path (Join-Path (Join-Path (Join-Path $wixExtPath 'WixToolset.Bal.wixext') '7.0.0-rc.2') 'wixext7') 'WixToolset.BootstrapperApplications.wixext.dll')
    
    if ($Version) {
        # Bundle also needs 4-part version
        $bundleVersion = $wixVersion
        if (-not $bundleVersion) {
            $bv = $Version.Trim()
            $bvParts = $bv.Split('.')
            $bundleVersion = if ($bvParts.Count -eq 3) { "$bv.0" } else { $bv }
        }
        $bundleArgs += '-d'
        $bundleArgs += "ProductVersion=$bundleVersion"
    }
    
    # Pass MSI path to bundle
    $bundleArgs += '-d'
    $bundleArgs += "RedballMsiPath=$outputMsi"
    
    Push-Location $scriptRoot
    try {
        & $wixExe @bundleArgs
        if ($LASTEXITCODE -eq 0) {
            Write-HostSafe "Bundle EXE created: $outputBundle" -ForegroundColor Green
        }
        else {
            Write-Warning "Bundle build failed (exit code $LASTEXITCODE). MSI was created successfully."
        }
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Warning "Bundle.wxs not found, skipping bundle creation"
}
