# Requires -Version 5.1
# Install-HID-NoRestart.ps1
# Installation script for the Interception driver that attempts to avoid a Windows restart
# by restarting all keyboard device nodes after installation.

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    # Using Write-Output to satisfy PSScriptAnalyzer while still being readable.
    Write-Output "`n>>> [STEP] $Message"
}

# 1. Check for Administrative Privileges
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."
}

Write-Step "Checking for Interception Driver resources..."
$DriverDir = Join-Path $PSScriptRoot "..\resources\drivers\interception"
$InstallExe = Join-Path $DriverDir "install-interception.exe"

if (-not (Test-Path $InstallExe)) {
    # Fallback: Check if we can find it in the bin folder if this is a dev environment
    $InstallExe = Get-ChildItem -Path ".." -Filter "install-interception.exe" -Recurse | Select-Object -ExpandProperty FullName -First 1
}

if (-not $InstallExe) {
    Write-Error "Could not find install-interception.exe"
}

Write-Step "Installing Interception Driver via official installer..."
# /install is the official flag.
Start-Process -FilePath $InstallExe -ArgumentList "/install" -Wait -NoNewWindow

Write-Step "Verifying Registry filters..."
$FilterKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e96b-e325-11ce-bfc1-08002be10318}"
$UpperFilters = Get-ItemProperty -Path $FilterKey -Name "UpperFilters"
if ($UpperFilters.UpperFilters -notcontains "keyboard") {
    Write-Output "Manually adding keyboard filter to registry..."
    $CurrentFilters = $UpperFilters.UpperFilters
    Set-ItemProperty -Path $FilterKey -Name "UpperFilters" -Value ($CurrentFilters + "keyboard")
}

Write-Step "Attempting to reload Keyboard stack without restart..."
Write-Output "NOTE: Your keyboard may briefly disconnect."

# Use pnputil to restart all instances of keyboard devices.
# This forces the driver stack to rebuild and evaluate the new UpperFilters.
$KbdDevices = pnputil /enum-devices /class Keyboard /connected | Select-String "Instance ID"
foreach ($Line in $KbdDevices) {
    if ($Line -match "Instance ID:\s+(.+)") {
        $InstanceId = $Matches[1].Trim()
        Write-Output "Restarting device: $InstanceId"
        pnputil /restart-device $InstanceId | Out-Null
    }
}

Write-Step "Installation Complete!"
Write-Output "If the HID mode still reports 'Driver Not Installed' in Redball, a full restart is still required."
































