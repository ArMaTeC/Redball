# Release System Updates

## Overview

Updated the Redball build and release system to:

1. **Produce all required download artifacts** (Setup.exe, ZIP, standalone EXE)
2. **Add beta channel support** for publishing pre-release versions
3. **Fix website download display** to show all available download types

## Changes Made

### 1. Windows Build System (`build-windows-on-linux.sh`)

#### Added ZIP Package Creation

- New function `step_build_zip()` creates portable ZIP package from published WPF directory
- ZIP excludes NSIS build artifacts (*.nsi,*.bmp files)
- Automatically called during build process
- Added to checksum generation

**Location:** Lines 478-511

#### Updated Build Flow

```bash
# Build steps now include:
step_restore
step_build_wpf
step_build_service
step_build_zip        # NEW: Creates portable ZIP
step_build_nsis       # Creates Setup.exe installer
```

#### Artifacts Produced

After a successful build, the following files are created in `/dist`:

- `Redball-{version}-Setup.exe` - NSIS installer
- `Redball-Setup.exe` - Copy of installer without version
- `Redball-{version}.zip` - Portable ZIP package
- `Redball.UI.WPF.exe` - Standalone executable (in wpf-publish/)
- `SHA256SUMS` - Checksums for all artifacts

### 2. Release Script (`release.sh`)

#### Added Channel Support

New parameters:

- `-c, --channel CHANNEL` - Specify release channel (stable, beta, dev)
- `--beta` - Shortcut for `--channel beta`
- `--no-publish` - Skip publishing to update-server

**Default:** `stable` channel

#### New Function: `publish_to_update_server()`

Automatically copies build artifacts to update-server with metadata:

**Location:** Lines 384-454

**Features:**

- Copies Windows artifacts (Setup.exe, ZIP, standalone EXE)
- Copies Linux artifacts (tar.gz, flatpak, deb)
- Creates `release.json` with channel metadata
- Generates file list with sizes and SHA256 hashes

**Release Directory Structure:**

```text
update-server/releases/{version}/
├── release.json              # Metadata with channel info
├── Redball-{version}-Setup.exe
├── Redball-{version}.zip
├── Redball-{version}.exe
└── [other artifacts]
```

#### Example Usage

**Stable Release:**

```bash
./scripts/linux/release.sh -v 2.1.80
```

**Beta Release:**

```bash
./scripts/linux/release.sh -v 2.1.81-beta --beta
```

**Dev Release:**

```bash
./scripts/linux/release.sh -v 2.1.82-dev -c dev
```

**Dry Run:**

```bash
./scripts/linux/release.sh --beta --dry-run
```

### 3. Update Server Database

#### New Script: `rebuild-db.js`

Scans releases directory and rebuilds database with channel information.

**Location:** `update-server/scripts/rebuild-db.js`

**Usage:**

```bash
cd update-server
node scripts/rebuild-db.js
```

**Features:**

- Reads `release.json` from each version directory
- Extracts channel metadata
- Calculates SHA256 hashes for all files
- Rebuilds `data/releases.json` database

#### API Already Supports Channels

The update-server API already had channel support:

- `/api/releases/latest?channel=stable` - Get latest stable release
- `/api/releases/latest?channel=beta` - Get latest beta release
- `/api/releases/latest?channel=dev` - Get latest dev release

### 4. Linux Build System Fix

#### Fixed VERSION Variable Collision

**Issue:** `check_os()` function was sourcing `/etc/os-release` which overwrote the `VERSION` variable with OS version (e.g., "24.04.4 LTS (Noble Numbat)") instead of project version.

**Fix:** Preserve and restore VERSION variable around OS detection

```bash
check_os() {
    if [ -f /etc/os-release ]; then
        local PROJECT_VERSION="$VERSION"
        . /etc/os-release
        VERSION="$PROJECT_VERSION"  # Restore project version
        ...
    fi
}
```

**Location:** `scripts/linux/build-linux.sh` lines 153-166

## Website Integration

The website (`update-server/public/index.html`) already has JavaScript to:

1. Fetch releases from `/api/releases`
2. Filter by channel (stable/beta/dev tabs)
3. Update download cards with version, size, and download links
4. Support multiple file types (Setup.exe, ZIP, standalone EXE)

The JavaScript looks for files matching:

- `*-Setup.exe` or `*Setup*` → NSIS Installer card
- `*.zip` → Portable ZIP card
- `*.exe` (excluding Service and Setup) → Standalone EXE card

## Testing Checklist

### Build System

- [ ] Run Windows build: `./scripts/linux/build-windows-on-linux.sh`
- [ ] Verify all artifacts created in `/dist`:
  - [ ] Redball-{version}-Setup.exe
  - [ ] Redball-{version}.zip
  - [ ] wpf-publish/Redball.UI.WPF.exe
  - [ ] SHA256SUMS

### Release System

- [ ] Test stable release: `./scripts/linux/release.sh -v X.X.X`
- [ ] Test beta release: `./scripts/linux/release.sh -v X.X.X-beta --beta`
- [ ] Verify files copied to `update-server/releases/{version}/`
- [ ] Verify `release.json` has correct channel
- [ ] Run `node update-server/scripts/rebuild-db.js`
- [ ] Check `update-server/data/releases.json` has channel info

### Website

- [ ] Start update-server: `cd update-server && npm start`
- [ ] Open <http://localhost:3500>
- [ ] Verify all three download cards show:
  - [ ] NSIS Installer with version and size
  - [ ] Portable ZIP with version and size
  - [ ] Standalone EXE with version and size
- [ ] Test channel tabs (Stable/Beta/Dev)
- [ ] Verify download links work

## Migration Notes

### Existing Releases

To add channel metadata to existing releases:

1. Create `release.json` in each version directory:

```json
{
  "version": "2.1.443",
  "channel": "stable",
  "date": "2026-04-06T07:00:00Z",
  "notes": "Release notes here"
}
```

1. Rebuild database:

```bash
cd update-server
node scripts/rebuild-db.js
```

### Future Releases

Simply use the release script with appropriate channel flag:

```bash
# Stable
./scripts/linux/release.sh -v 2.2.0

# Beta
./scripts/linux/release.sh -v 2.2.0-beta --beta
```

The script will automatically:

- Build if needed
- Create release.json with channel metadata
- Copy all artifacts to update-server
- Update the database

## Summary

The release system now:
✅ Produces Setup.exe, ZIP, and standalone EXE for Windows
✅ Supports beta/dev channel releases
✅ Automatically publishes to update-server with metadata
✅ Website displays all download types correctly
✅ Fixed Debian package version collision issue
