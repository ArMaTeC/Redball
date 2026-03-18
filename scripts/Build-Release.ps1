#Requires -Version 7
<#
.SYNOPSIS
    Local build script that replicates the GitHub Actions release workflow.

.DESCRIPTION
    This script performs the same build, sign, and package steps as the GitHub Actions workflow.
    It builds the WPF application, creates an MSI installer, and optionally signs the artifacts.

.PARAMETER Version
    Optional version override. If not specified, extracts version from the csproj file.

.PARAMETER SkipTag
    Skip creating/pushing git tags.

.PARAMETER SkipSign
    Skip code signing (build unsigned artifacts).

.PARAMETER ForceBuild
    Force build even if tag already exists.

.PARAMETER CodeSigningCertPath
    Path to a PFX certificate file for code signing. If not provided, creates a self-signed cert.

.PARAMETER CodeSigningPassword
    Password for the code signing certificate.

.EXAMPLE
    .\Build-Release.ps1

.EXAMPLE
    .\Build-Release.ps1 -Version "2.1.81" -SkipTag

.EXAMPLE
    .\Build-Release.ps1 -CodeSigningCertPath "C:\certs\mycert.pfx" -CodeSigningPassword "secret123"
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Version = "",
    [switch]$SkipTag,
    [switch]$SkipSign,
    [switch]$ForceBuild,
    [string]$CodeSigningCertPath = "",
    [string]$CodeSigningPassword = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "Continue"

# Configuration
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$CsprojPath = Join-Path $ProjectRoot "src\Redball.UI.WPF\Redball.UI.WPF.csproj"
$InstallerDir = Join-Path $ProjectRoot "installer"
$DistDir = Join-Path $ProjectRoot "dist"
$WixExtensions = @(
    "WixToolset.UI.wixext",
    "WixToolset.Netfx.wixext",
    "WixToolset.Bal.wixext"
)

#region Helper Functions

function Write-Step {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠ $Message" -ForegroundColor Yellow
}

function Test-CommandExists {
    param([string]$Command)
    $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Get-SigntoolPath {
    $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1

    if (-not $signtool) {
        $signtool = Get-ChildItem "C:\Program Files\Windows Kits\10\bin\*\x64\signtool.exe" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    }

    return $signtool
}

function Install-WixIfNeeded {
    if (Test-CommandExists "wix") {
        Write-Success "WiX already installed"
        return
    }

    Write-Step "Installing WiX Toolset"

    if (-not (Test-CommandExists "dotnet")) {
        throw ".NET SDK not found. Please install .NET 8.0 SDK first."
    }

    Write-Host "Installing WiX tool..."
    dotnet tool install --global wix --version 4.0.5

    if ($LASTEXITCODE -ne 0) {
        # Tool might already be installed
        Write-Warning "WiX tool install returned exit code $LASTEXITCODE (may already be installed)"
    }

    # Add to PATH for this session
    $wixDir = Join-Path $env:USERPROFILE ".dotnet\tools"
    if ($env:PATH -notlike "*$wixDir*") {
        $env:PATH = "$wixDir;$env:PATH"
    }

    # Install extensions
    $wix = Join-Path $wixDir "wix.exe"
    foreach ($ext in $WixExtensions) {
        Write-Host "Installing WiX extension: $ext"
        & $wix extension add -g $ext
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Failed to install extension $ext (may already be installed)"
        }
    }

    if (Test-CommandExists "wix") {
        $wixVersion = (wix --version)
        Write-Success "WiX installed: $wixVersion"
    }
    else {
        throw "WiX installation failed. Please restart your shell and try again."
    }
}

function Get-Version {
    if ($Version) {
        return $Version
    }

    # Extract version from csproj
    if (-not (Test-Path $CsprojPath)) {
        throw "Project file not found: $CsprojPath"
    }

    [xml]$csproj = Get-Content $CsprojPath
    $versionNode = $csproj.Project.PropertyGroup.Version

    if ($versionNode) {
        return $versionNode
    }

    Write-Warning "Could not extract version from csproj, using default 2.1.80"
    return "2.1.80"
}

function Invoke-GitTag {
    param([string]$Tag)

    if ($SkipTag) {
        Write-Host "Skipping git tag creation (SkipTag specified)"
        return $true
    }

    Write-Step "Creating Git Tag: $Tag"

    # Check if tag exists
    $existingTag = git tag -l $Tag 2>$null
    if ($existingTag) {
        Write-Warning "Tag $Tag already exists"
        if (-not $ForceBuild) {
            Write-Host "Use -ForceBuild to rebuild anyway"
            return $false
        }
        Write-Host "Force build enabled - continuing with existing tag"
        return $true
    }

    if ($PSCmdlet.ShouldProcess($Tag, "Create and push git tag")) {
        git tag -a $Tag -m "Release $Tag"
        git push origin $Tag
        Write-Success "Created and pushed tag: $Tag"
    }
    else {
        Write-Host "Would create tag: $Tag (WhatIf)"
    }

    return $true
}

function Publish-WpfApp {
    Write-Step "Publishing WPF Application"

    if (-not (Test-Path $DistDir)) {
        New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
    }

    $publishDir = Join-Path $DistDir "wpf-publish"

    Write-Host "Running dotnet publish..."
    dotnet publish $CsprojPath --configuration Release -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    $exePath = Join-Path $publishDir "Redball.UI.WPF.exe"
    if (-not (Test-Path $exePath)) {
        throw "Published EXE not found at $exePath"
    }

    $exeSize = (Get-Item $exePath).Length / 1MB
    Write-Success "Published WPF app to $publishDir ($( [math]::Round($exeSize, 1) ) MB)"

    # Copy Assets folder for WiX installer (single file publish doesn't copy content files separately)
    $assetsSource = Join-Path (Split-Path $CsprojPath -Parent) "Assets"
    $assetsDest = Join-Path $publishDir "Assets"
    if (Test-Path $assetsSource) {
        Copy-Item $assetsSource $assetsDest -Recurse -Force
        Write-Host "Copied Assets to publish directory"
    }
}

function Invoke-CodeSign {
    param(
        [string]$FilePath,
        [string]$Description = ""
    )

    if ($SkipSign) {
        Write-Host "Skipping code signing (SkipSign specified)"
        return
    }

    if (-not (Test-Path $FilePath)) {
        Write-Warning "File not found for signing: $FilePath"
        return
    }

    Write-Step "Signing: $(Split-Path $FilePath -Leaf)"

    $signtool = Get-SigntoolPath
    if (-not $signtool) {
        Write-Warning "signtool.exe not found. Install Windows SDK to enable signing."
        return
    }

    Write-Host "Using signtool: $($signtool.FullName)"

    $certPath = $null
    $password = $null
    $isSelfSigned = $false
    $certThumbprint = $null

    try {
        if ($CodeSigningCertPath -and (Test-Path $CodeSigningCertPath)) {
            # Use provided certificate
            Write-Host "Using provided certificate: $CodeSigningCertPath"
            $certPath = $CodeSigningCertPath
            $password = $CodeSigningPassword
        }
        else {
            # Create self-signed certificate
            Write-Host "No production certificate found - creating self-signed certificate..."
            $isSelfSigned = $true

            $cert = New-SelfSignedCertificate `
                -Type CodeSigningCert `
                -Subject "CN=Redball Development, O=ArMaTeC" `
                -CertStoreLocation Cert:\CurrentUser\My `
                -NotAfter (Get-Date).AddYears(1)

            $certThumbprint = $cert.Thumbprint
            $certPath = Join-Path $env:TEMP "dev-signing.pfx"
            $password = "RedballDev2024"
            $securePwd = ConvertTo-SecureString -String $password -Force -AsPlainText

            Export-PfxCertificate -Cert $cert -FilePath $certPath -Password $securePwd | Out-Null
            Write-Host "Created self-signed certificate"
        }

        # Sign the file
        $signArgs = @(
            "sign",
            "/f", $certPath,
            "/p", $password,
            "/tr", "http://timestamp.digicert.com",
            "/td", "sha256",
            "/fd", "sha256"
        )

        if ($Description) {
            $signArgs += "/d"
            $signArgs += $Description
        }

        $signArgs += $FilePath

        Write-Host "Signing with signtool..."
        & $signtool.FullName @signArgs

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Signing failed (exit code $LASTEXITCODE)"
        }
        else {
            if ($isSelfSigned) {
                Write-Success "File signed with development certificate"
            }
            else {
                Write-Success "File signed with production certificate"
            }
        }

        # Verify signature (may fail for self-signed)
        Write-Host "Verifying signature..."
        $verifyResult = & $signtool.FullName verify /pa $FilePath 2>&1
        $verifyResult | ForEach-Object { Write-Host "  $_" }

        if ($LASTEXITCODE -ne 0) {
            Write-Host "Note: Signature verification may fail for self-signed certificates (expected)"
        }

    }
    finally {
        # Cleanup
        if ($certPath -and $isSelfSigned -and (Test-Path $certPath)) {
            Remove-Item $certPath -Force -ErrorAction SilentlyContinue
        }
        if ($certThumbprint) {
            Remove-Item "Cert:\CurrentUser\My\$certThumbprint" -Force -ErrorAction SilentlyContinue
        }
    }
}

function Build-Msi {
    param([string]$Version)

    Write-Step "Building MSI Installer v$Version"

    if (-not (Test-Path $InstallerDir)) {
        throw "Installer directory not found: $InstallerDir"
    }

    $wxsPath = Join-Path $InstallerDir "Redball.wxs"
    if (-not (Test-Path $wxsPath)) {
        throw "WiX source file not found: $wxsPath"
    }

    $msiOutput = Join-Path $DistDir "Redball-$Version.msi"
    $logPath = Join-Path $DistDir "wix.log"

    $wixDir = Join-Path $env:USERPROFILE ".dotnet\tools"
    $wix = Join-Path $wixDir "wix.exe"

    Push-Location $InstallerDir
    try {
        Write-Host "Building MSI with WiX..."

        $wixArgs = @(
            "build",
            "Redball.wxs",
            "-o", $msiOutput,
            "-ext", "WixToolset.UI.wixext",
            "-ext", "WixToolset.Netfx.wixext",
            "-d", "ProductVersion=$Version"
        )

        & $wix @wixArgs 2>&1 | Tee-Object -FilePath $logPath

        $wixExit = $LASTEXITCODE
        Write-Host "WiX exit code: $wixExit"

        if ($wixExit -ne 0) {
            Get-Content $logPath -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
            throw "MSI build failed (WiX exit code $wixExit)"
        }

        if (-not (Test-Path $msiOutput)) {
            throw "MSI not produced at $msiOutput"
        }

        $msiSize = (Get-Item $msiOutput).Length / 1MB
        Write-Success "MSI built: $msiOutput ($( [math]::Round($msiSize, 1) ) MB)"

        # Copy to Redball.msi for consistency
        $genericMsi = Join-Path $DistDir "Redball.msi"
        Copy-Item $msiOutput $genericMsi -Force
        Write-Host "Copied to: $genericMsi"

    }
    finally {
        Pop-Location
    }
}

function Build-Bundle {
    param([string]$Version)

    Write-Step "Building Bundle EXE v$Version"

    $wxsPath = Join-Path $InstallerDir "Bundle.wxs"
    if (-not (Test-Path $wxsPath)) {
        Write-Warning "Bundle.wxs not found - skipping bundle creation"
        return
    }

    $bundleOutput = Join-Path $DistDir "Redball-Setup-$Version.exe"
    $logPath = Join-Path $DistDir "bundle.log"

    $wixDir = Join-Path $env:USERPROFILE ".dotnet\tools"
    $wix = Join-Path $wixDir "wix.exe"

    Push-Location $InstallerDir
    try {
        Write-Host "Building Bundle with WiX..."

        $msiPath = "../dist/Redball-$Version.msi"

        $wixArgs = @(
            "build",
            "Bundle.wxs",
            "-o", $bundleOutput,
            "-ext", "WixToolset.Bal.wixext",
            "-ext", "WixToolset.Netfx.wixext",
            "-d", "ProductVersion=$Version",
            "-d", "RedballMsiPath=$msiPath"
        )

        & $wix @wixArgs 2>&1 | Tee-Object -FilePath $logPath

        $wixExit = $LASTEXITCODE
        Write-Host "Bundle exit code: $wixExit"

        if ($wixExit -ne 0) {
            Get-Content $logPath -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
            Write-Warning "Bundle build failed (continuing with MSI only)"
            return
        }

        if (-not (Test-Path $bundleOutput)) {
            Write-Warning "Bundle not produced - continuing with MSI only"
            return
        }

        $bundleSize = (Get-Item $bundleOutput).Length / 1MB
        Write-Success "Bundle built: $bundleOutput ($( [math]::Round($bundleSize, 1) ) MB)"

        # Copy to generic name
        $genericBundle = Join-Path $DistDir "Redball-Setup.exe"
        Copy-Item $bundleOutput $genericBundle -Force
        Write-Host "Copied to: $genericBundle"

    }
    finally {
        Pop-Location
    }
}

#endregion

#region Main Script

Write-Step "Redball Local Build Script"
Write-Host "Project root: $ProjectRoot"
Write-Host "PowerShell version: $($PSVersionTable.PSVersion)"

# Check prerequisites
if (-not (Test-CommandExists "dotnet")) {
    throw ".NET SDK not found. Please install .NET 8.0 SDK."
}

$dotnetVersion = (dotnet --version)
Write-Host ".NET SDK: $dotnetVersion"

# Extract version
$version = Get-Version
$tag = "v$version"
Write-Host "Version: $version"
Write-Host "Tag: $tag"

# Create tag if needed
$shouldBuild = Invoke-GitTag -Tag $tag
if (-not $shouldBuild) {
    Write-Host "Build skipped (tag already exists, use -ForceBuild to override)"
    exit 0
}

# Install WiX
Install-WixIfNeeded

# Clean and create dist directory
if (Test-Path $DistDir) {
    Remove-Item $DistDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# Build steps
Publish-WpfApp

# Sign the EXE
$exePath = Join-Path $DistDir "wpf-publish\Redball.UI.WPF.exe"
Invoke-CodeSign -FilePath $exePath -Description "Redball v$version"

# Build MSI
Build-Msi -Version $version

# Sign the MSI
$msiPath = Join-Path $DistDir "Redball.msi"
Invoke-CodeSign -FilePath $msiPath -Description "Redball v$version"

# Build Bundle
Build-Bundle -Version $version

# Sign the Bundle if it exists
$bundlePath = Join-Path $DistDir "Redball-Setup.exe"
if (Test-Path $bundlePath) {
    Invoke-CodeSign -FilePath $bundlePath -Description "Redball v$version Setup"
}

# Summary
Write-Step "Build Complete"
Write-Host ""
Write-Host "Output files in $DistDir :" -ForegroundColor Cyan
Get-ChildItem $DistDir -File | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  - $($_.Name) ($size MB)"
}

Write-Host ""
Write-Success "Build finished successfully!"

#endregion
