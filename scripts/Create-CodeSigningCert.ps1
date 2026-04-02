#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$CertificateName = "RedballDevCert",
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Persistent storage for certificate export
$certStorageDir = Join-Path $env:USERPROFILE '.redball'
if (-not (Test-Path $certStorageDir)) {
    New-Item -ItemType Directory -Path $certStorageDir -Force | Out-Null
}

function Write-Step { param($msg) Write-Host "  → $msg" -ForegroundColor Cyan }
function Write-Success { param($msg) Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn { param($msg) Write-Host "  ⚠ $msg" -ForegroundColor Yellow }

Write-Host "`n=== Redball Code Signing Certificate ===" -ForegroundColor White
Write-Host "Storage: $certStorageDir`n" -ForegroundColor Gray

$certStore = "Cert:\CurrentUser\My"

# Check if cert already exists
$existingCert = Get-ChildItem -Path $certStore | Where-Object { $_.Subject -eq "CN=$CertificateName" } | Select-Object -First 1

if ($existingCert -and -not $Force) {
    Write-Success "Certificate already exists: $CertificateName"
    Write-Host "  Thumbprint: $($existingCert.Thumbprint)" -ForegroundColor Gray
    Write-Host "  Expires: $($existingCert.NotAfter)" -ForegroundColor Gray
    Write-Host "`nTo recreate, run with -Force`n" -ForegroundColor Yellow
    exit 0
}

if ($existingCert -and $Force) {
    Write-Step "Removing existing certificate..."
    Remove-Item -Path "$certStore\$($existingCert.Thumbprint)" -Force
    Write-Success "Existing certificate removed"
}

# Create new certificate with proper code signing EKU
Write-Step "Creating code signing certificate: $CertificateName"
$cert = New-SelfSignedCertificate `
    -Subject "CN=$CertificateName" `
    -CertStoreLocation $certStore `
    -Type CodeSigningCert `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -NotAfter (Get-Date).AddYears(5) `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

Write-Success "Certificate created successfully!"
Write-Host "  Subject: $($cert.Subject)" -ForegroundColor Gray
Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
Write-Host "  Valid Until: $($cert.NotAfter)" -ForegroundColor Gray

# Trust certificate locally and export to persistent location
Write-Step "Exporting certificate to persistent storage..."
$exportPath = Join-Path $certStorageDir 'RedballDevCert.cer'
Export-Certificate -Cert $cert -FilePath $exportPath -Force | Out-Null
Write-Success "Certificate exported to: $exportPath"

Write-Step "Trusting certificate in local machine stores..."
try {
    # Import to Trusted Root and Trusted Publisher
    Import-Certificate -FilePath $exportPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
    Import-Certificate -FilePath $exportPath -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null
    Write-Success "Certificate trusted in Root and TrustedPublisher stores"
}
catch {
    Write-Warn "Could not trust certificate in local machine stores (requires admin)"
    Write-Host "  To trust manually, run as administrator:" -ForegroundColor Gray
    Write-Host "    Import-Certificate -FilePath '$exportPath' -CertStoreLocation Cert:\LocalMachine\Root" -ForegroundColor Gray
    Write-Host "    Import-Certificate -FilePath '$exportPath' -CertStoreLocation Cert:\LocalMachine\TrustedPublisher" -ForegroundColor Gray
}

Write-Host "`nCertificate ready for code signing!`n" -ForegroundColor Green
Write-Host "Certificate Details:" -ForegroundColor Gray
Write-Host "  Subject: $($cert.Subject)" -ForegroundColor Gray
Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
Write-Host "  Valid Until: $($cert.NotAfter)" -ForegroundColor Gray
Write-Host "  Export Path: $exportPath" -ForegroundColor Gray
Write-Host "`nUsage:" -ForegroundColor Gray
Write-Host "  .\scripts\build.ps1" -ForegroundColor Gray
Write-Host "`nManual signtool:" -ForegroundColor Gray
Write-Host "  signtool sign /s My /sha1 $($cert.Thumbprint) /fd SHA256 /t http://timestamp.digicert.com yourfile.exe" -ForegroundColor Gray
Write-Host ""


