#!/usr/bin/env pwsh
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()]
    [string]$ServiceName = "RedballInputService"
)

#Requires -RunAsAdministrator

Write-Information "Uninstalling Redball Input Service..." -InformationAction Continue

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $service) {
    Write-Warning "Service '$ServiceName' not found. Nothing to uninstall."
    exit 0
}

# Stop the service if running
if ($service.Status -ne 'Stopped') {
    Write-Information "Stopping service..." -NoNewline -InformationAction Continue
    Stop-Service -Name $ServiceName -Force

    do {
        Start-Sleep -Milliseconds 500
        $service = Get-Service -Name $ServiceName
        Write-Information "." -NoNewline -InformationAction Continue
    } while ($service.Status -ne 'Stopped')
    Write-Information "" -InformationAction Continue
}

# Remove the service
Write-Information "Removing service..." -InformationAction Continue
& sc.exe delete $ServiceName | Out-Null

if ($LASTEXITCODE -eq 0) {
    Write-Information "Service uninstalled successfully!" -InformationAction Continue
}
else {
    Write-Error "Failed to remove service (sc.exe exit code: $LASTEXITCODE)"
    exit 1
}


















































































































































