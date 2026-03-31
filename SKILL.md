---
name: redball-deployment-guide
description: Complete deployment guide for Redball Windows keep-awake utility
---

# Redball Deployment Guide

## Overview

Redball is a .NET 10 WPF desktop application for Windows that prevents system sleep. This guide covers build and deployment practices.

## Pre-deployment Checklist

### Environment Preparation

- [ ] **Windows Requirements**: Windows 10/11 (8.1+ minimum)
- [ ] **.NET 10 SDK**: Installed for building
- [ ] **WiX Toolset v4**: For MSI creation (install via `dotnet tool install --global wix`)
- [ ] **Code Signing Certificate**: PFX file with password (optional for dev, required for release)
- [ ] **GitHub Secrets**: `CODE_SIGNING_CERT` and `CODE_SIGNING_PASSWORD` for CI signing

### Build Verification

- [ ] **Run Tests**: `dotnet test tests/`
- [ ] **Build WPF**: `dotnet build src/Redball.UI.WPF/`
- [ ] **PSScriptAnalyzer**: Check PowerShell scripts (optional)

## Deployment Methods

### Method 1: MSI Installer (Recommended)

```powershell
# Full deploy pipeline (build WPF, create MSI, sign)
.\installer\Deploy-Redball.ps1

# Build MSI only with specific version
.\installer\Build-MSI.ps1 -Version "3.0.0"
```

The MSI:
- Installs to `%LocalAppData%\Redball` (per-user, no admin required)
- Creates Start Menu and Desktop shortcuts
- Optional startup shortcut via Registry Run key
- Branded installer UI with custom banner/dialog images
- Code-signed (if certificate configured)

### Method 2: Portable EXE

```powershell
# Build self-contained EXE
dotnet publish src/Redball.UI.WPF/Redball.UI.WPF.csproj `
  --configuration Release `
  --self-contained true `
  -r win-x64 `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -o dist/
```

Output is a single ~3.3MB EXE with embedded .NET runtime.

### Method 3: Development Setup

```powershell
# Clone repository
git clone https://github.com/ArMaTeC/Redball.git
cd Redball

# Build
dotnet build src/Redball.UI.WPF/

# Run
dotnet run --project src/Redball.UI.WPF/
```

## Configuration

### Build Script Options (`scripts/build.ps1`)

```powershell
# Full build with default version from version.txt
.\scripts\build.ps1

# Skip specific steps
.\scripts\build.ps1 -SkipTests       # Skip test execution
.\scripts\build.ps1 -SkipLint       # Skip PSScriptAnalyzer
.\scripts\build.ps1 -SkipWPF       # Skip WPF build
.\scripts\build.ps1 -SkipMSI       # Skip MSI creation

# Release with custom message
.\scripts\build.ps1 -ReleaseMessage "chore(release): v3.1.0"

# Skip auto-commit/push
.\scripts\build.ps1 -SkipReleaseCommit
.\scripts\build.ps1 -SkipReleasePush
```

### Version Management

Version is synchronized between:
1. `src/Redball.UI.WPF/Redball.UI.WPF.csproj` - `<Version>`, `<FileVersion>`, `<AssemblyVersion>`
2. `scripts/version.txt` - fallback version

```powershell
# Bump version across all files
.\scripts\Bump-Version.ps1 -NewVersion "3.1.0"
```

## Code Signing

### CI/CD Signing (GitHub Actions)

Set repository secrets:

```powershell
# Base64-encode your PFX certificate
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))
$base64 | gh secret set CODE_SIGNING_CERT --repo ArMaTeC/Redball
"password" | gh secret set CODE_SIGNING_PASSWORD --repo ArMaTeC/Redball
```

Signing happens automatically in `release.yml` workflow.

### Manual Signing

```powershell
# Sign EXE
signtool.exe sign /f cert.pfx /p password `
  /tr http://timestamp.digicert.com /td sha256 `
  /fd sha256 Redball.UI.WPF.exe

# Sign MSI
signtool.exe sign /f cert.pfx /p password `
  /tr http://timestamp.digicert.com /td sha256 `
  /fd sha256 Redball.msi
```

## CI/CD Workflows

### `ci.yml` - Pull Request Validation

- Builds WPF application
- Runs xUnit tests
- Validates JSON configs
- PSScriptAnalyzer on PowerShell scripts

### `release.yml` - Release Automation

- Triggers on push to `main`
- Builds and signs EXE
- Builds branded MSI with WiX
- Signs MSI
- Creates GitHub Release with MSI attached

### `virustotal.yml` - Security Scan

- Submits release artifacts to VirusTotal
- Reports scan results

## Installation Locations

| Component | Path |
|-----------|------|
| Application | `%LocalAppData%\Redball\` |
| Config | `%LocalAppData%\Redball\UserData\Redball.json` |
| Logs | `%LocalAppData%\Redball\Logs\` |
| Crash Flag | `%LocalAppData%\Redball\UserData\crash.flag` |
| Driver (optional) | `%SystemRoot%\System32\drivers\Redball.KMDF.sys` |

## Troubleshooting

### Build Issues

- **WiX not found**: `dotnet tool install --global wix`
- **Missing .NET 10**: Download from https://dotnet.microsoft.com/
- **Signing fails**: Check certificate validity and password

### Runtime Issues

- **Tray icon missing**: Check Windows notification settings
- **System still sleeps**: Verify `SetThreadExecutionState` not blocked by group policy
- **Config reset**: Check `%LocalAppData%\Redball\UserData\` permissions
- **Multiple instances**: Named mutex will prevent this; kill stale processes if needed

```powershell
# Kill stale Redball processes
Get-Process Redball.UI.WPF | Stop-Process -Force
```

## Quick Reference

| Command | Purpose |
|---------|---------|
| `dotnet build src/Redball.UI.WPF/` | Build WPF app |
| `dotnet test tests/` | Run unit tests |
| `.\scripts\build.ps1` | Full build pipeline |
| `.\installer\Build-MSI.ps1` | Create MSI only |
| `.\installer\Deploy-Redball.ps1` | Deploy with signing |
| `.\scripts\Bump-Version.ps1` | Update version |

---

**Note**: Always test deployments on a clean Windows VM before releasing.
