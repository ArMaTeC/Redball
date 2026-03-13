#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$WixBinPath = "C:\Program Files\WiX Toolset v4.0\bin",
    [string]$Configuration = "Release",
    [string]$AddLocalFeatures = '',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$wxsPath = Join-Path $scriptRoot 'Redball.wxs'
$iconPath = Join-Path $scriptRoot 'Redball.ico'
$licenseSourcePath = Join-Path $projectRoot 'LICENSE'
$licenseRtfPath = Join-Path $scriptRoot 'Redball-License.rtf'
$outputDir = Join-Path $projectRoot 'dist'
$versionFilePath = Join-Path $projectRoot 'version.txt'

# Read version from version.txt if not provided
if (-not $Version -and (Test-Path $versionFilePath)) {
    $Version = Get-Content $versionFilePath -Raw
    $Version = $Version.Trim()
    Write-Host "Using version from version.txt: $Version" -ForegroundColor Cyan
}

# Use version in MSI filename if provided
if ($Version) {
    $outputMsi = Join-Path $outputDir "Redball-$Version.msi"
}
else {
    $outputMsi = Join-Path $outputDir 'Redball.msi'
}

$requiredExtensions = @('WixToolset.UI.wixext', 'WixToolset.Netfx.wixext', 'WixToolset.Bal.wixext')

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
    $escaped = $licenseText -replace '\\', '\\\\' -replace '{', '\{' -replace '}', '\}'
    $escaped = $escaped -replace "`r`n", '\par ' -replace "`n", '\par '
    $rtfContent = "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs20 $escaped}"

    Set-Content -LiteralPath $OutputPath -Value $rtfContent -Encoding Ascii -NoNewline
    Write-Host "Generated installer license at $OutputPath" -ForegroundColor DarkGreen
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
    Write-Host 'Install WiX v4 and retry.' -ForegroundColor Yellow
    Write-Host 'Expected paths checked:' -ForegroundColor Yellow
    $wixCandidates | ForEach-Object { if ($_ ) { Write-Host "  $_" -ForegroundColor DarkYellow } }
    Write-Host 'You can also pass -WixBinPath "<path-to-wix-bin>".' -ForegroundColor Yellow
    return
}

Write-Host "Building MSI from $wxsPath ..." -ForegroundColor Cyan

foreach ($extension in $requiredExtensions) {
    if (-not (Install-WixExtensionIfMissing -WixExe $wixExe -ExtensionId $extension)) {
        Write-Error "Required WiX extension is missing: $extension"
    }
}

$buildArgs = @(
    'build'
    $wxsPath
    '-o', $outputMsi
)

foreach ($extension in $requiredExtensions) {
    $buildArgs += '-ext'
    $buildArgs += $extension
}

if ($AddLocalFeatures) {
    $buildArgs += '-d'
    $buildArgs += "DEFAULT_ADDLOCAL=$AddLocalFeatures"
}

# Pass version to WiX if provided
if ($Version) {
    $buildArgs += '-d'
    $buildArgs += "ProductVersion=$Version"
}

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

Write-Host "MSI created: $outputMsi" -ForegroundColor Green

# Build the bundle (EXE installer that chains .NET 8 + MSI)
Write-Host "Building bundle EXE..." -ForegroundColor Cyan

$bundleWxsPath = Join-Path $scriptRoot 'Bundle.wxs'
if (Test-Path $bundleWxsPath) {
    if ($Version) {
        $outputBundle = Join-Path $outputDir "Redball-$Version.exe"
    }
    else {
        $outputBundle = Join-Path $outputDir 'Redball.exe'
    }
    
    $bundleArgs = @(
        'build'
        $bundleWxsPath
        '-o', $outputBundle
        '-ext', 'WixToolset.Bal.wixext'
        '-ext', 'WixToolset.Netfx.wixext'
    )
    
    if ($Version) {
        $bundleArgs += '-d'
        $bundleArgs += "ProductVersion=$Version"
    }
    
    # Pass MSI path to bundle
    $bundleArgs += '-d'
    $bundleArgs += "RedballMsiPath=$outputMsi"
    
    Push-Location $scriptRoot
    try {
        & $wixExe @bundleArgs
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Bundle EXE created: $outputBundle" -ForegroundColor Green
        }
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Warning "Bundle.wxs not found, skipping bundle creation"
}
