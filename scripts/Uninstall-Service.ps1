#!/usr/bin/env pwsh
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()]
    [string]$ServiceName = "RedballInputService"
)

#Requires -RunAsAdministrator

Write-Host "Uninstalling Redball Input Service..." -ForegroundColor Cyan

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $service) {
    Write-Host "Service '$ServiceName' not found. Nothing to uninstall." -ForegroundColor Yellow
    exit 0
}

# Stop the service if running
if ($service.Status -ne 'Stopped') {
    Write-Host "Stopping service..." -NoNewline
    Stop-Service -Name $ServiceName -Force

    do {
        Start-Sleep -Milliseconds 500
        $service = Get-Service -Name $ServiceName
        Write-Host "." -NoNewline
    } while ($service.Status -ne 'Stopped')
    Write-Host ""
}

# Remove the service
Write-Host "Removing service..."
& sc.exe delete $ServiceName | Out-Null

if ($LASTEXITCODE -eq 0) {
    Write-Host "Service uninstalled successfully!" -ForegroundColor Green
}
else {
    Write-Error "Failed to remove service (sc.exe exit code: $LASTEXITCODE)"
    exit 1
}


































