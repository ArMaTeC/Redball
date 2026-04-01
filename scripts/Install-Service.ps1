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

Write-Information "Installing Redball Input Service..." -InformationAction Continue

# Validate paths
if (-not (Test-Path $ServicePath)) {
    Write-Error "Service executable not found: $ServicePath"
    Write-Warning "Build the service first with: .\scripts\build.ps1"
    exit 1
}

$ServicePath = Resolve-Path $ServicePath
$serviceDir = Split-Path $ServicePath -Parent

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Warning "Service already exists. Stopping and removing..."

    if ($existingService.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Write-Information "Waiting for service to stop..." -NoNewline -InformationAction Continue
        do {
            Start-Sleep -Milliseconds 500
            $existingService = Get-Service -Name $ServiceName
            Write-Information "." -NoNewline -InformationAction Continue
        } while ($existingService.Status -ne 'Stopped')
        Write-Information "" -InformationAction Continue
    }

    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# Copy helper to service directory if specified
if ($HelperPath -and (Test-Path $HelperPath)) {
    $targetHelper = Join-Path $serviceDir "Redball.SessionHelper.exe"
    if ($HelperPath -ne $targetHelper) {
        Write-Information "Copying session helper to service directory..." -InformationAction Continue
        Copy-Item $HelperPath $targetHelper -Force
    }
}

# Install the service
Write-Information "Creating service: $ServiceName" -InformationAction Continue
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
Write-Information "Starting service..." -InformationAction Continue
Start-Service -Name $ServiceName

# Verify
$service = Get-Service -Name $ServiceName
if ($service.Status -eq 'Running') {
    Write-Information "Service installed and running successfully!" -InformationAction Continue
    Write-Information "  Service Name: $ServiceName" -InformationAction Continue
    Write-Information "  Path: $ServicePath" -InformationAction Continue
    Write-Information "  Status: $($service.Status)" -InformationAction Continue
}
else {
    Write-Warning "Service installed but not running. Status: $($service.Status)"
    Write-Warning "Check Event Viewer > Windows Logs > Application for details."
}

Write-Information "" -InformationAction Continue
Write-Information "To manage the service:" -InformationAction Continue
Write-Information "  Start:  Start-Service $ServiceName" -InformationAction Continue
Write-Information "  Stop:   Stop-Service $ServiceName" -InformationAction Continue
Write-Information "  Remove: .\scripts\Uninstall-Service.ps1" -InformationAction Continue



























































