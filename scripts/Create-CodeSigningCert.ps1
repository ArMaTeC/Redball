#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$CertificateName = "RedballDevCert",
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

# Persistent storage for certificate export
$certStorageDir = Join-Path $env:USERPROFILE '.redball'
if (-not (Test-Path $certStorageDir)) {
    New-Item -ItemType Directory -Path $certStorageDir -Force | Out-Null
}

# ANSI color codes for Information stream
$GRAY = "`e[90m"
$WHITE = "`e[97m"
$RESET = "`e[0m"

function Write-Step { param($msg) Write-Information "  → $msg" -InformationAction Continue }
function Write-Success { param($msg) Write-Information "  ✓ $msg" -InformationAction Continue }
function Write-Warn { param($msg) Write-Warning "  ⚠ $msg" }

Write-Information "`n${WHITE}=== Redball Code Signing Certificate ===${RESET}"
Write-Information "${GRAY}Storage: $certStorageDir${RESET}`n"

$certStore = "Cert:\CurrentUser\My"

# Check if cert already exists
$existingCert = Get-ChildItem -Path $certStore | Where-Object { $_.Subject -eq "CN=$CertificateName" } | Select-Object -First 1

if ($existingCert -and -not $Force) {
    Write-Success "Certificate already exists: $CertificateName"
    Write-Information "${GRAY}  Thumbprint: $($existingCert.Thumbprint)${RESET}"
    Write-Information "${GRAY}  Expires: $($existingCert.NotAfter)${RESET}"
    Write-Information "${GRAY}`nTo recreate, run with -Force`${RESET}`n"
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
Write-Information "${GRAY}  Subject: $($cert.Subject)${RESET}"
Write-Information "${GRAY}  Thumbprint: $($cert.Thumbprint)${RESET}"
Write-Information "${GRAY}  Valid Until: $($cert.NotAfter)${RESET}"

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
    Write-Information "${GRAY}  To trust manually, run as administrator:${RESET}"
    Write-Information "${GRAY}    Import-Certificate -FilePath '$exportPath' -CertStoreLocation Cert:\LocalMachine\Root${RESET}"
    Write-Information "${GRAY}    Import-Certificate -FilePath '$exportPath' -CertStoreLocation Cert:\LocalMachine\TrustedPublisher${RESET}"
}

Write-Information "${WHITE}`nCertificate ready for code signing!`${RESET}`n"
Write-Information "${GRAY}Certificate Details:${RESET}"
Write-Information "${GRAY}  Subject: $($cert.Subject)${RESET}"
Write-Information "${GRAY}  Thumbprint: $($cert.Thumbprint)${RESET}"
Write-Information "${GRAY}  Valid Until: $($cert.NotAfter)${RESET}"
Write-Information "${GRAY}  Export Path: $exportPath${RESET}"
Write-Information "${GRAY}`nUsage:${RESET}"
Write-Information "${GRAY}  .\scripts\build.ps1${RESET}"
Write-Information "${GRAY}`nManual signtool:${RESET}"
Write-Information "${GRAY}  signtool sign /s My /sha1 $($cert.Thumbprint) /fd SHA256 /t http://timestamp.digicert.com yourfile.exe${RESET}"
Write-Information ""















































































