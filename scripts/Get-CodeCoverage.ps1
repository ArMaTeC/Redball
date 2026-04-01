param(
    [string]$OutputPath = "coverage.xml",
    [switch]$HtmlReport = $false
)

# Code Coverage Script for Redball WPF
# Generates coverage reports using coverlet and reportgenerator

$ErrorActionPreference = 'Stop'

function Write-HostSafe {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'Build script requires console output for user feedback')]
    param(
        [Parameter(Mandatory, Position = 0)]
        [object]$Object,
        [System.ConsoleColor]$ForegroundColor
    )
    if ($ForegroundColor) {
        Write-Host $Object -ForegroundColor $ForegroundColor
    }
    else {
        Write-Host $Object
    }
}

Write-HostSafe "=== Redball Code Coverage Report ===" -ForegroundColor Cyan

# Check prerequisites
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Error "dotnet CLI not found. Please install .NET SDK."
    exit 1
}

# Install required tools if not present
Write-HostSafe "Checking for coverage tools..." -ForegroundColor Yellow

$coverletInstalled = dotnet tool list --global | Select-String "coverlet.console"
if (-not $coverletInstalled) {
    Write-HostSafe "Installing coverlet.console..." -ForegroundColor Yellow
    dotnet tool install --global coverlet.console
}

$reportGenInstalled = dotnet tool list --global | Select-String "dotnet-reportgenerator-globaltool"
if (-not $reportGenInstalled) {
    Write-HostSafe "Installing reportgenerator..." -ForegroundColor Yellow
    dotnet tool install --global dotnet-reportgenerator-globaltool
}

# Build project first
Write-HostSafe "Building project..." -ForegroundColor Yellow
$srcDir = Join-Path $PSScriptRoot ".."
$srcDir = Join-Path $srcDir "src"
$wpfDir = Join-Path $srcDir "Redball.UI.WPF"
$projectPath = Join-Path $wpfDir "Redball.UI.WPF.csproj"
dotnet build $projectPath --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# Check if test project exists
$testPath = Join-Path (Join-Path $PSScriptRoot "..") (Join-Path "tests" "Redball.Tests.csproj")
if (Test-Path $testPath) {
    Write-HostSafe "Running tests with coverage..." -ForegroundColor Yellow
    
    # Run coverlet
    $coverageFile = Join-Path (Join-Path $PSScriptRoot "..") $OutputPath
    dotnet test $testPath --configuration Release --collect:"XPlat Code Coverage" --results-directory:$coverageFile
    
    if ($HtmlReport) {
        Write-HostSafe "Generating HTML report..." -ForegroundColor Yellow
        $reportDir = Join-Path (Join-Path $PSScriptRoot "..") "coverage-report"
        reportgenerator -reports:"$coverageFile\**\coverage.cobertura.xml" -targetdir:$reportDir -reporttypes:Html
        
        Write-HostSafe "Coverage report generated: $reportDir\index.html" -ForegroundColor Green
        
        # Open report if on Windows
        if ($IsWindows -or ($env:OS -eq "Windows_NT")) {
            Start-Process "$reportDir\index.html"
        }
    }
    
    # Parse coverage percentage
    $coverageXml = Get-ChildItem -Path $coverageFile -Filter "coverage.cobertura.xml" -Recurse | Select-Object -First 1
    if ($coverageXml) {
        [xml]$xml = Get-Content $coverageXml.FullName
        $lineRate = [double]$xml.coverage.'line-rate' * 100
        $branchRate = [double]$xml.coverage.'branch-rate' * 100
        
        Write-HostSafe "" 
        Write-HostSafe "=== Coverage Summary ===" -ForegroundColor Cyan
        Write-HostSafe "Line Coverage:    $([math]::Round($lineRate, 2))%" -ForegroundColor $(if ($lineRate -ge 80) { 'Green' } elseif ($lineRate -ge 60) { 'Yellow' } else { 'Red' })
        Write-HostSafe "Branch Coverage:  $([math]::Round($branchRate, 2))%" -ForegroundColor $(if ($branchRate -ge 80) { 'Green' } elseif ($branchRate -ge 60) { 'Yellow' } else { 'Red' })
        
        # Quality gate check
        if ($lineRate -lt 60) {
            Write-Warning "Line coverage is below 60% threshold!"
        }
        if ($branchRate -lt 50) {
            Write-Warning "Branch coverage is below 50% threshold!"
        }
    }
}
else {
    Write-Warning "No test project found at $testPath"
    Write-HostSafe "To add code coverage, create a test project with MSTest, NUnit, or xUnit." -ForegroundColor Yellow
}

Write-HostSafe "" 
Write-HostSafe "Coverage analysis complete!" -ForegroundColor Green














































































































































































