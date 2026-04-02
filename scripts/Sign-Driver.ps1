#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DriverPath,
    
    [string]$CertificateName = "RedballDevCert"
)

$ErrorActionPreference = 'Stop'

function Write-Step { param($msg) Write-Information "  → $msg" -InformationAction Continue }
function Write-Success { param($msg) Write-Information "  ✓ $msg" -InformationAction Continue }

Write-Information "`n=== Driver Signing Tool (Test Mode) ===" -InformationAction Continue

# 1. Locate Signtool (WDK/SDK)
$sdkPath = Join-Path "${env:ProgramFiles(x86)}" "Windows Kits\10\bin"
$signtool = Get-ChildItem -Path $sdkPath -Filter "signtool.exe" -Recurse | Where-Object { $_.FullName -like "*\x64\signtool.exe" } | Select-Object -First 1
if (-not $signtool) {
    throw "signtool.exe not found. Please install the Windows SDK."
}
Write-Success "Found signtool: $($signtool.FullName)"

# 2. Create/Find Self-Signed Certificate
$cert = Get-ChildItem -Path Cert:\CurrentUser\My, Cert:\LocalMachine\My | Where-Object { $_.Subject -like "*CN=$CertificateName*" } | Select-Object -First 1
if (-not $cert) {
    Write-Step "Creating new self-signed certificate: $CertificateName"
    $cert = New-SelfSignedCertificate -Type CodeSigning -Subject "CN=$CertificateName" -KeyExportPolicy Exportable -KeySpec Signature
    Write-Success "Certificate created"
}
else {
    Write-Success "Using existing certificate: $CertificateName"
}

# 2.1 Add to Trusted Root and Trusted Publisher (Required for driver loading)
Write-Step "Trusting certificate locally..."
$tmpCertPath = [System.IO.Path]::GetTempFileName()
Export-Certificate -Cert $cert -FilePath $tmpCertPath | Out-Null
Import-Certificate -FilePath $tmpCertPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
Import-Certificate -FilePath $tmpCertPath -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null
Remove-Item $tmpCertPath
Write-Success "Certificate trusted in Root and TrustedPublisher stores"

# 3. Sign the driver
Write-Step "Signing: $DriverPath"
$filesToSign = @($DriverPath)
$catPath = [System.IO.Path]::ChangeExtension($DriverPath, "cat")
if (Test-Path $catPath) {
    $filesToSign += $catPath
}
else {
    # Check for same filename but with .cat in the same dir
    $potentialCat = Join-Path (Split-Path $DriverPath) "*.cat"
    $filesToSign += Get-ChildItem $potentialCat | Select-Object -ExpandProperty FullName -ErrorAction SilentlyContinue
}

foreach ($file in $filesToSign | Select-Object -Unique) {
    Write-Step "Signing file: $file"
    & $signtool.FullName sign /v /sm /fd SHA256 /sha1 $($cert.Thumbprint) /t http://timestamp.digicert.com $file
}

if ($LASTEXITCODE -eq 0) {
    Write-Success "Driver signed successfully!"
    Write-Information "`nIMPORTANT: Remember to enable Test Signing mode if you haven't already:" -InformationAction Continue
    Write-Information "  bcdedit /set testsigning on" -InformationAction Continue
    Write-Information "  (Requires Reboot)`n" -InformationAction Continue
}
else {
    Write-Error "Signing failed."
}




































































