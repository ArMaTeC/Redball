param(
    [string]$ProjectPath = "..\src\Redball.UI.WPF\Redball.UI.WPF.csproj",
    [string]$TestProjectPath = ".\Redball.Tests.csproj",
    [switch]$InstallTool = $false
)

# Mutation Testing Script using Stryker.NET
# Helps identify weak spots in test suite by mutating source code

$ErrorActionPreference = 'Stop'

Write-Host "=== Redball Mutation Testing ===" -ForegroundColor Cyan

# Check prerequisites
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Error "dotnet CLI not found. Please install .NET SDK."
    exit 1
}

# Install Stryker if requested
if ($InstallTool) {
    Write-Host "Installing Stryker.NET..." -ForegroundColor Yellow
    dotnet tool install --global dotnet-stryker
}

# Check if Stryker is installed
$stryker = Get-Command dotnet-stryker -ErrorAction SilentlyContinue
if (-not $stryker) {
    Write-Host "Stryker.NET not found. Installing..." -ForegroundColor Yellow
    dotnet tool install --global dotnet-stryker
}

# Create stryker config if it doesn't exist
$strykerConfig = @"
{
  \"stryker-config\": {
    \"project-info\": {
      \"name\": \"Redball\",
      \"version\": \"2.1.22\",
      \"module\": \"Redball.UI.WPF\"
    },
    \"test-projects\": [
      \"$TestProjectPath\"
    ],
    \"mutation-level\": \"Standard\",
    \"target-framework\": \"net8.0-windows\",
    \"reporters\": [
      \"html\",
      \"progress\",
      \"dashboard\"
    ],
    \"thresholds\": {
      \"high\": 80,
      \"low\": 60,
      \"break\": 40
    },
    \"mutators\": [
      \"Arithmetic\",
      \"Equality\",
      \"Logical\",
      \"Boolean\",
      \"String\",
      \"Collection\"
    ],
    \"excluded-mutations\": [
      \"Linq\"
    ],
    \"ignore-methods\": [
      \"Logger.*\",
      \"ToString\",
      \"GetHashCode\"
    ]
  }
}
"@

$configPath = Join-Path $PSScriptRoot "stryker-config.json"
if (-not (Test-Path $configPath)) {
    $strykerConfig | Out-File -FilePath $configPath -Encoding UTF8
    Write-Host "Created Stryker configuration file" -ForegroundColor Green
}

# Run mutation testing
Write-Host "Running mutation tests..." -ForegroundColor Yellow
Write-Host "This may take several minutes as Stryker creates and tests mutants..." -ForegroundColor Yellow

try {
    Push-Location $PSScriptRoot
    
    # Run stryker
    dotnet stryker --config-file $configPath
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "" 
        Write-Host "=== Mutation Testing Complete ===" -ForegroundColor Green
        Write-Host "Check the HTML report for detailed results" -ForegroundColor Green
        
        # Try to open the report
        $reportPath = Get-ChildItem -Path "StrykerOutput" -Filter "*.html" -Recurse | Select-Object -First 1
        if ($reportPath) {
            Write-Host "Report location: $($reportPath.FullName)" -ForegroundColor Cyan
            
            if ($IsWindows -or $env:OS -eq "Windows_NT") {
                Start-Process $reportPath.FullName
            }
        }
    }
    else {
        Write-Warning "Mutation testing found issues. Check the report for details."
    }
}
catch {
    Write-Error "Mutation testing failed: $_"
}
finally {
    Pop-Location
}

Write-Host "" 
Write-Host "Mutation testing complete!" -ForegroundColor Green
Write-Host "" 
Write-Host "Interpretation Guide:" -ForegroundColor Cyan
Write-Host "  - Mutation Score: % of mutants killed by tests" -ForegroundColor White
Write-Host "  - >80%: Excellent test coverage" -ForegroundColor Green
Write-Host "  - 60-80%: Good, but can improve" -ForegroundColor Yellow
Write-Host "  - <60%: Tests need strengthening" -ForegroundColor Red
Write-Host "  - Survived mutants indicate missing test cases" -ForegroundColor White
