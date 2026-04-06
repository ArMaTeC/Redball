# Redball Unified Build System

## Overview

The Redball project now has unified build scripts that orchestrate all build operations across platforms, including Windows artifacts, Linux packages, update-server, and website deployment.

## Quick Start

### Linux/macOS

```bash
# Build everything
./scripts/build.sh all

# Build only Windows artifacts
./scripts/build.sh windows

# Build only Linux packages
./scripts/build.sh linux

# Check build status
./scripts/build.sh status

# Start update-server
./scripts/build.sh serve
```

### Windows (PowerShell)

```powershell
# Build everything
.\scripts\build.ps1 all

# Build only Windows artifacts
.\scripts\build.ps1 windows

# Check build status
.\scripts\build.ps1 status

# Start update-server
.\scripts\build.ps1 serve
```

## Build Scripts

### `scripts/build.sh` (Linux/macOS)

Unified build orchestration for Linux/macOS environments.

**Commands:**

- `all` - Build everything (Windows, Linux, update-server, website)
- `windows` - Build Windows artifacts via Wine
- `linux` - Build Linux packages (GTK app, deb, flatpak, tarball)
- `update-server` - Install dependencies and validate update-server
- `website` - Validate website files
- `clean` - Remove all build artifacts
- `publish` - Publish release to update-server with channel support
- `serve` - Start update-server on <http://localhost:3500>
- `status` - Show build status and available artifacts

**Options:**

- `--channel CHANNEL` - Release channel (stable, beta, dev)
- `--beta` - Shortcut for `--channel beta`
- `--version VERSION` - Specify version for publish
- `--skip-windows` - Skip Windows build in 'all' command
- `--skip-linux` - Skip Linux build in 'all' command
- `--dry-run` - Preview actions without executing

**Examples:**

```bash
# Full build
./scripts/build.sh all

# Build and publish beta release
./scripts/build.sh all
./scripts/build.sh publish --beta --version 2.1.81

# Build only what you need
./scripts/build.sh windows
./scripts/build.sh update-server

# Check what's built
./scripts/build.sh status

# Clean and rebuild
./scripts/build.sh clean
./scripts/build.sh all --skip-linux
```

### `scripts/build.ps1` (Windows PowerShell)

Unified build orchestration for Windows environments.

**Commands:**
Same as Linux version (all, windows, linux, update-server, website, clean, publish, serve, status)

**Parameters:**

- `-Command` - Build command (required)
- `-Channel` - Release channel (stable, beta, dev)
- `-Beta` - Switch for beta channel
- `-Version` - Version for publish
- `-SkipWindows` - Skip Windows build
- `-SkipLinux` - Skip Linux build (requires WSL)
- `-DryRun` - Preview mode

**Examples:**

```powershell
# Full build
.\scripts\build.ps1 all

# Build and publish beta
.\scripts\build.ps1 all
.\scripts\build.ps1 publish -Beta -Version "2.1.81"

# Build only Windows
.\scripts\build.ps1 windows

# Check status
.\scripts\build.ps1 status

# Start server
.\scripts\build.ps1 serve
```

**Note:** Linux builds on Windows require WSL (Windows Subsystem for Linux).

## Build Outputs

### Windows Artifacts (`dist/`)

After running `./scripts/build.sh windows`:

- `Redball-{version}-Setup.exe` - NSIS installer with auto-start, service install
- `Redball-Setup.exe` - Copy without version in filename
- `Redball-{version}.zip` - Portable ZIP package
- `wpf-publish/Redball.UI.WPF.exe` - Standalone executable
- `SHA256SUMS` - Checksums for all artifacts

**Build Time:** ~14 seconds (WPF + Service)

### Linux Artifacts (`dist/linux/`)

After running `./scripts/build.sh linux`:

- `redball-{version}-linux.tar.gz` - Tarball archive
- `redball_{version}_amd64.deb` - Debian package
- `redball-{version}.flatpak` - Flatpak package

**Build Time:** ~30-60 seconds

### Update Server

After running `./scripts/build.sh update-server`:

- Dependencies installed in `update-server/node_modules/`
- Server validated and ready to run
- Database at `update-server/data/releases.json`

## Publishing Releases

The unified build system integrates with the release workflow:

### Stable Release

```bash
# Build everything
./scripts/build.sh all

# Publish to stable channel
./scripts/build.sh publish --version 2.1.80
```

This will:

1. Copy all Windows artifacts to `update-server/releases/2.1.80/`
2. Copy all Linux artifacts to `update-server/releases/2.1.80/`
3. Create `release.json` with channel metadata
4. Generate file list with SHA256 hashes
5. Create GitHub release (if configured)

### Beta Release

```bash
# Build everything
./scripts/build.sh all

# Publish to beta channel
./scripts/build.sh publish --beta --version 2.1.81-beta
```

### Dev Release

```bash
./scripts/build.sh publish --channel dev --version 2.1.82-dev
```

## Architecture

### Build Flow

```text
scripts/build.sh all
├── update-server (npm install, validate)
├── website (validate HTML)
├── windows (via build-windows-on-linux.sh)
│   ├── WPF app
│   ├── Service
│   ├── ZIP package
│   └── NSIS installer
└── linux (via build-linux.sh)
    ├── GTK app
    ├── Debian package
    ├── Flatpak
    └── Tarball
```

### Publish Flow

```text
scripts/build.sh publish
├── Get version from version.txt
├── Copy Windows artifacts
│   ├── Redball-{version}-Setup.exe
│   ├── Redball-{version}.zip
│   └── Redball-{version}.exe
├── Copy Linux artifacts
│   ├── redball-{version}.tar.gz
│   ├── redball_{version}.deb
│   └── redball-{version}.flatpak
├── Create release.json with channel
└── Rebuild database
```

## Integration with Existing Scripts

The unified build scripts delegate to existing specialized scripts:

- **Windows builds:** `scripts/linux/build-windows-on-linux.sh`
- **Linux builds:** `scripts/linux/build-linux.sh`
- **Releases:** `scripts/linux/release.sh`
- **Version management:** `scripts/linux/bump-version.sh`

This maintains backward compatibility while providing a simpler interface.

## Status Command

The `status` command provides a comprehensive overview:

```bash
./scripts/build.sh status
```

**Output:**

```text
Redball Build Status

Version: 2.1.443

Windows Artifacts:
  ✓ Redball-2.1.443-Setup.exe (8.1M)
  ✓ Redball-2.1.443.zip (7.2M)
  ✓ Redball.UI.WPF.exe (368K)

Linux Artifacts:
  ✓ redball-2.1.443-linux.tar.gz (248K)
  ✓ redball_2.1.443_amd64.deb (245K)

Update Server:
  ✓ Dependencies installed
  ✓ Database: 3 releases

Website:
  ✓ index.html exists
```

## Clean Command

Remove all build artifacts:

```bash
./scripts/build.sh clean
```

This removes:

- `dist/` directory (all build outputs)
- Optionally `update-server/node_modules/` (uncomment in script)

## Serve Command

Start the update-server locally:

```bash
./scripts/build.sh serve
```

This will:

1. Install npm dependencies if needed
2. Start server on <http://localhost:3500>
3. Serve website and API endpoints

**API Endpoints:**

- `GET /api/releases` - List all releases
- `GET /api/releases/latest?channel=stable` - Get latest stable release
- `GET /api/releases/latest?channel=beta` - Get latest beta release
- `GET /downloads/{version}/{filename}` - Download file

## Environment Requirements

### Linux/macOSs

**Required:**

- Bash 4.0+
- Node.js 18+ (for update-server)
- Wine (for Windows builds)
- .NET SDK 10.0 (for Windows builds)

**Optional:**

- Python 3.9+ (for Linux builds)
- GTK 4.0 (for Linux builds)
- Flatpak (for Flatpak packages)

### Windows

**Required:**

- PowerShell 5.1+
- Node.js 18+ (for update-server)
- .NET SDK 10.0 (for Windows builds)

**Optional:**

- WSL (for Linux builds)

## Troubleshooting

### "Missing dependencies" warning

Install required tools:

```bash
# Ubuntu/Debian
sudo apt-get install nodejs npm wine dotnet-sdk-10.0

# macOS
brew install node wine dotnet
```

### Windows build fails on Linux

Ensure Wine and Windows .NET SDK are set up:

```bash
./scripts/linux/build-windows-on-linux.sh --setup
```

### Update server won't start

Install dependencies:

```bash
cd update-server
npm install
```

### "No artifacts found" in status

Run a build first:

```bash
./scripts/build.sh all
```

## CI/CD Integration

The unified build scripts are designed for CI/CD:

### GitHub Actions Example

```yaml
name: Build

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup dependencies
        run: |
          ./scripts/linux/build-windows-on-linux.sh --setup
      
      - name: Build all
        run: ./scripts/build.sh all
      
      - name: Publish release
        if: startsWith(github.ref, 'refs/tags/')
        run: |
          VERSION=${GITHUB_REF#refs/tags/v}
          ./scripts/build.sh publish --version $VERSION
```

## Migration from Old Workflow

**Before:**

```bash
# Multiple commands needed
cd scripts/linux
./build-windows-on-linux.sh
./build-linux.sh -a
cd ../../update-server
npm install
npm start
```

**After:**

```bash
# Single unified interface
./scripts/build.sh all
./scripts/build.sh serve
```

## Summary

The unified build system provides:

✅ **Single entry point** for all build operations  
✅ **Cross-platform** support (Linux, macOS, Windows)  
✅ **Channel-based** publishing (stable, beta, dev)  
✅ **Status monitoring** of build artifacts  
✅ **Dry-run mode** for testing  
✅ **Backward compatible** with existing scripts  
✅ **CI/CD ready** with clear exit codes  

Use `./scripts/build.sh --help` or `.\scripts\build.ps1 -?` for detailed usage.
