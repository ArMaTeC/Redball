#!/usr/bin/env pwsh
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()]
    [string]$ServicePath = "$PSScriptRoot\..\dist\Redball.Service\Redball.Service.exe",

    [Parameter()]
    [string]$HelperPath = "$PSScriptRoot\..\dist\Redball.SessionHelper\Redball.SessionHelper.exe"
)

#Requires -RunAsAdministrator

$ServiceName = "RedballInputService"
$DisplayName = "Redball Input Service"

Write-Host "Installing Redball Input Service..." -ForegroundColor Cyan

# Validate paths
if (-not (Test-Path $ServicePath)) {
    Write-Error "Service executable not found: $ServicePath"
    Write-Host "Build the service first with: .\scripts\build.ps1" -ForegroundColor Yellow
    exit 1
}

$ServicePath = Resolve-Path $ServicePath
$serviceDir = Split-Path $ServicePath -Parent

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service already exists. Stopping and removing..." -ForegroundColor Yellow

    if ($existingService.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Write-Host "Waiting for service to stop..." -NoNewline
        do {
            Start-Sleep -Milliseconds 500
            $existingService = Get-Service -Name $ServiceName
            Write-Host "." -NoNewline
        } while ($existingService.Status -ne 'Stopped')
        Write-Host ""
    }

    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# Copy helper to service directory if specified
if ($HelperPath -and (Test-Path $HelperPath)) {
    $targetHelper = Join-Path $serviceDir "Redball.SessionHelper.exe"
    if ($HelperPath -ne $targetHelper) {
        Write-Host "Copying session helper to service directory..."
        Copy-Item $HelperPath $targetHelper -Force
    }
}

# Install the service
Write-Host "Creating service: $ServiceName"
$quotedPath = '"{0}"' -f $ServicePath
& sc.exe create $ServiceName binPath= $quotedPath start= auto DisplayName= "$DisplayName" | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service (sc.exe exit code: $LASTEXITCODE)"
    exit 1
}

# Configure service to interact with desktop (needed for input injection)
& sc.exe config $ServiceName type= interact | Out-Null

# Configure recovery options
& sc.exe failure $ServiceName reset= 60 actions= restart/0/restart/0/run/0 | Out-Null

# Start the service
Write-Host "Starting service..."
Start-Service -Name $ServiceName

# Verify
$service = Get-Service -Name $ServiceName
if ($service.Status -eq 'Running') {
    Write-Host "Service installed and running successfully!" -ForegroundColor Green
    Write-Host "  Service Name: $ServiceName"
    Write-Host "  Path: $ServicePath"
    Write-Host "  Status: $($service.Status)"
}
else {
    Write-Warning "Service installed but not running. Status: $($service.Status)"
    Write-Host "Check Event Viewer > Windows Logs > Application for details."
}

Write-Host ""
Write-Host "To manage the service:" -ForegroundColor Cyan
Write-Host "  Start:  Start-Service $ServiceName"
Write-Host "  Stop:   Stop-Service $ServiceName"
Write-Host "  Remove: .\scripts\Uninstall-Service.ps1"
























