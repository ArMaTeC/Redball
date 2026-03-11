# CI Simulation Script
$ErrorActionPreference = 'Stop'

Write-Host "=== CI Simulation Starting ===" -ForegroundColor Cyan

# Simulate: Install Pester
Write-Host "`n[1] Installing Pester..." -ForegroundColor Yellow
try {
    Remove-Module Pester -Force -ErrorAction SilentlyContinue
    Import-Module Pester -RequiredVersion 5.5.0 -ErrorAction Stop
    Write-Host "  Pester $(Get-Module Pester | Select-Object -ExpandProperty Version) loaded" -ForegroundColor Green
}
catch {
    Write-Host "  Installing Pester..." -ForegroundColor Yellow
    Install-Module -Name Pester -Force -SkipPublisherCheck -RequiredVersion 5.5.0
    Import-Module Pester -RequiredVersion 5.5.0
}

# Simulate: Run Pester Tests
Write-Host "`n[2] Running Pester tests from ./tests/Redball.Tests.ps1..." -ForegroundColor Yellow
try {
    $config = New-PesterConfiguration
    $config.Run.Path = "./tests/Redball.Tests.ps1"
    $config.Run.PassThru = $true
    $config.Output.Verbosity = "Normal"
    $config.CodeCoverage.Enabled = $false
    $result = Invoke-Pester -Configuration $config
    
    if ($result.FailedCount -gt 0) {
        Write-Host "  FAILED: $($result.FailedCount) tests failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "  PASSED: $($result.PassedCount) tests" -ForegroundColor Green
}
catch {
    Write-Host "  ERROR: $_" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== CI Simulation PASSED ===" -ForegroundColor Green
