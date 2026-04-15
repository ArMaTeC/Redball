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
- [ ] **NSIS v3.0+**: For EXE installer creation (install via package manager)
- [ ] **Code Signing Certificate**: PFX file with password (optional for dev, required for release)
- [ ] **GitHub Secrets**: `CODE_SIGNING_CERT` and `CODE_SIGNING_PASSWORD` for CI signing

### Build Verification

- [ ] **Run Tests**: `dotnet test tests/`
- [ ] **Build WPF**: `dotnet build src/Redball.UI.WPF/`
- [ ] **PSScriptAnalyzer**: Check PowerShell scripts (optional)

## Deployment Methods

### Method 1: NSIS Installer (Recommended)

```powershell
# Full deploy pipeline (build WPF, create installer, sign)
.\scripts\build.ps1 all

# Build installer only with specific version
.\scripts\build.ps1 windows -Version "2.1.654"
```

The NSIS installer:
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

# Build specific targets
.\scripts\build.ps1 windows     # Build Windows artifacts only
.\scripts\build.ps1 update-server # Build update-server only
.\scripts\build.ps1 website     # Build website only

# Release with custom message
.\scripts\build.ps1 -ReleaseMessage "chore(release): v3.1.0"

# Skip auto-commit/push
.\scripts\build.ps1 -SkipReleaseCommit
.\scripts\build.ps1 -SkipReleasePush
```

### Version Management

Version is centralized in `Directory.Build.props` and shared across all projects.

```powershell
# Bump version using the version script
.\scripts\windows\Bump-Version.ps1 -NewVersion "2.1.655"

# Or manually edit Directory.Build.props:
# <Version>2.1.654</Version>
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

# Sign installer
signtool.exe sign /f cert.pfx /p password `
  /tr http://timestamp.digicert.com /td sha256 `
  /fd sha256 Redball-*-Setup.exe
```

## CI/CD Workflows

### `ci.yml` - Pull Request Validation

- Builds WPF application
- Runs xUnit tests
- Validates JSON configs
- PSScriptAnalyzer on PowerShell scripts

### `release.yml` - Release Automation

- Triggers on push to `main`
- Builds and signs WPF EXE
- Builds branded NSIS installer
- Signs installer
- Creates GitHub Release with installer attached

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

- **NSIS not found**: Install NSIS from https://nsis.sourceforge.io/
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
| `.\scripts\build.ps1 windows` | Build Windows artifacts |
| `.\scripts\build.ps1 all` | Full build with signing |
| `.\scripts\windows\Bump-Version.ps1` | Update version |

---

**Note**: Always test deployments on a clean Windows VM before releasing.
