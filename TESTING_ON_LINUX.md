# Running Redball Tests on Linux

Since Redball is a WPF application requiring Windows Desktop runtime, full test execution needs special handling on Linux.

## Current Status

✅ **Build**: Successful — All tests compile without errors  
⚠️ **Execution**: Limited — WPF tests require Windows Desktop runtime

## Options for Running Tests on Linux

### Option 1: Wine + Windows .NET SDK (Most Compatible)

If you have Wine set up from the build process:

```bash
# Make the script executable and run
chmod +x scripts/run-tests-via-wine.sh
bash scripts/run-tests-via-wine.sh
```

**Requirements:**
- Wine installed (`apt install wine`)
- Windows .NET SDK in Wine (`~/.wine-redball/dotnet.exe`)

### Option 2: Filter Non-WPF Tests Only

Run tests that don't require WPF/Windows Desktop:

```bash
# Run only non-WPF service tests
cd /root/Redball
dotnet test tests/Redball.Tests.csproj \
  --filter "FullyQualifiedName!~WPF" \
  --no-build
```

### Option 3: GitHub Actions (Recommended for CI)

Create `.github/workflows/tests.yml`:

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
          
      - name: Restore dependencies
        run: dotnet restore tests/Redball.Tests.csproj
        
      - name: Build
        run: dotnet build tests/Redball.Tests.csproj --no-restore
        
      - name: Test with coverage
        run: |
          dotnet test tests/Redball.Tests.csproj \
            --no-build \
            --verbosity normal \
            --collect:"XPlat Code Coverage" \
            --logger trx
            
      - name: Upload coverage
        uses: codecov/codecov-action@v4
        with:
          files: '**/coverage.cobertura.xml'
```

### Option 4: Docker Windows Container

```bash
# Requires Docker with Windows containers
docker run --rm -v $(pwd):C:/src \
  mcr.microsoft.com/dotnet/framework/sdk:4.8-windowsservercore-ltsc2022 \
  powershell -Command "cd C:/src; dotnet test tests/Redball.Tests.csproj"
```

### Option 5: Azure DevOps Pipeline

```yaml
trigger:
  - main

pool:
  vmImage: 'windows-latest'

steps:
  - task: UseDotNet@2
    inputs:
      version: '10.0.x'
      
  - script: dotnet test tests/Redball.Tests.csproj --collect:"XPlat Code Coverage"
    displayName: 'Run Tests'
    
  - task: PublishTestResults@2
    inputs:
      testResultsFormat: 'VSTest'
      testResultsFiles: '**/*.trx'
      
  - task: PublishCodeCoverageResults@2
    inputs:
      codeCoverageTool: 'Cobertura'
      summaryFileLocation: '**/coverage.cobertura.xml'
```

## What Works on Linux Now

✅ **Build verification**: `dotnet build` succeeds  
✅ **Static analysis**: Roslyn analyzers, style checks  
✅ **Code coverage compilation**: Coverlet collector builds  

## What Requires Windows

⚠️ **WPF UI tests**: Require Windows Desktop runtime  
⚠️ **Integration tests**: Some use Windows-specific APIs  
⚠️ **Visual tests**: Require actual Windows UI subsystem

## Quick Commands

```bash
# 1. Verify build (works on Linux)
export PATH="/usr/share/dotnet:$PATH"
dotnet build tests/Redball.Tests.csproj -p:EnableWindowsTargeting=true

# 2. Check test count
grep -r "\[TestMethod\]" tests/*.cs | wc -l

# 3. List all test files
ls -1 tests/*Tests.cs

# 4. View test summary
bash scripts/run-tests-linux.sh
```

## Summary

| Method | Platform | Coverage | Setup Time |
|--------|----------|----------|------------|
| Wine + .NET SDK | Linux | Full | 10 min |
| GitHub Actions | Cloud | Full | 0 min (managed) |
| Docker Windows | Linux | Full | 5 min |
| Local Linux | Linux | Partial | 0 min |

**Recommendation**: Use GitHub Actions for CI with `windows-latest` runner. This gives you full test execution with zero local setup.
