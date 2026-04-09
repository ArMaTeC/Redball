# Redball Package Manager Distribution

This directory contains the package manifests and automation for distributing Redball via Windows package managers:

- **[Winget](winget/)** - Windows Package Manager (Microsoft Store)
- **[Scoop](scoop/)** - Command-line package manager for Windows
- **[Chocolatey](chocolatey/)** - Windows software management automation

## Quick Reference

| Package Manager | Install Command                  | Status                     |
| --------------- | -------------------------------- | -------------------------- |
| Winget          | `winget install ArMaTeC.Redball` | ⏳ Pending first submission |
| Scoop           | `scoop install redball`          | ⏳ Pending bucket setup     |
| Chocolatey      | `choco install redball`          | ⏳ Pending first push       |

## Directory Structure

```text
packaging/
├── README.md                          # This file
├── winget/
│   ├── ArMaTeC.Redball.yaml           # Version manifest
│   ├── ArMaTeC.Redball.installer.yaml # Installer details
│   └── ArMaTeC.Redball.locale.en-US.yaml # Metadata
├── scoop/
│   └── redball.json                   # Scoop manifest
└── chocolatey/
    ├── redball.nuspec                 # Chocolatey package spec
    └── tools/
        ├── chocolateyinstall.ps1      # Installation script
        ├── chocolateyuninstall.ps1    # Uninstall script
        ├── LICENSE.txt                 # License file
        └── VERIFICATION.txt            # Verification info
```

## Automation Scripts

Located in `scripts/packaging/`:

### `update-package-managers.sh`

Updates all package manifests when a new version is released:

```bash
# Update manifests to current project version
./scripts/packaging/update-package-managers.sh

# Update to specific version
./scripts/packaging/update-package-managers.sh -v 2.1.456

# Preview changes without applying
./scripts/packaging/update-package-managers.sh --dry-run
```

This script automatically:

- Fetches the current version from `Directory.Build.props`
- Calculates SHA256 hashes for release artifacts
- Updates all manifest files with new version and hashes
- Updates URLs and release dates

### `publish-chocolatey.sh`

Builds and publishes the Chocolatey package:

```bash
# Build and publish
./scripts/packaging/publish-chocolatey.sh -v 2.1.456

# Just build (dry run)
./scripts/packaging/publish-chocolatey.sh --dry-run

# With explicit API key
./scripts/packaging/publish-chocolatey.sh --api-key xxxxxx
```

## GitHub Actions Automation

The `.github/workflows/package-managers.yml` workflow automatically publishes to all package managers when a GitHub release is published.

### Required Secrets

Configure these in your GitHub repository settings:

| Secret                 | Description                                                        | Used For                 |
| ---------------------- | ------------------------------------------------------------------ | ------------------------ |
| `CHOCO_API_KEY`        | Chocolatey API key from <https://community.chocolatey.org/account> | Publishing to Chocolatey |
| `WINGET_GITHUB_TOKEN`  | Personal access token with `public_repo` scope                     | Creating winget-pkgs PR  |
| `REDBALL_GITHUB_TOKEN` | GitHub token for repository access                                 | Updating Scoop bucket    |

## Manual Setup Guide

### Winget

Winget uses the community-driven [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) repository.

**First-time submission (automated):**

```bash
# Run the automated submission script
./scripts/packaging/publish-winget.sh

# Or with specific version
./scripts/packaging/publish-winget.sh -v 2.1.456

# Dry run to validate without submitting
./scripts/packaging/publish-winget.sh --dry-run
```

This script automatically:

1. Creates a fork of `microsoft/winget-pkgs` (if needed)
2. Validates the manifests
3. Creates the proper directory structure
4. Submits a PR to microsoft/winget-pkgs

**First-time submission (manual):**

If you prefer manual submission:

1. Fork <https://github.com/microsoft/winget-pkgs>
2. Create directory: `manifests/a/ArMaTeC/Redball/2.1.455/`
3. Copy the three YAML files from `packaging/winget/`
4. Submit PR to microsoft/winget-pkgs

**Subsequent updates:**

The GitHub Action will automatically submit updates using the same script.

### Scoop

Scoop requires a "bucket" repository to host manifests.

**Setup:**

1. Create repository `ArMaTeC/scoop-bucket`
2. Add this repository as a Scoop bucket:

   ```powershell
   scoop bucket add redball https://github.com/ArMaTeC/scoop-bucket
   ```

3. Copy `packaging/scoop/redball.json` to `bucket/redball.json`

**Automated updates:**

The GitHub Action automatically pushes manifest updates to the bucket repository.

### Chocolatey

**Prerequisites:**

1. Create account at <https://community.chocolatey.org/>
2. Get API key from <https://community.chocolatey.org/account>
3. Store API key in `CHOCO_API_KEY` secret

**First-time publish:**

```powershell
cd packaging/chocolatey
choco pack redball.nuspec
choco push redball.2.1.455.nupkg --api-key YOUR_API_KEY --source https://push.chocolatey.org/
```

**Subsequent updates:**

The GitHub Action automatically builds and pushes new versions.

## Testing Packages Locally

### Winget1

```powershell
# Install manifest locally
winget install --manifest packaging/winget/

# Validate manifest
winget validate --manifest packaging/winget/
```

### Scoop1

```powershell
# Install from local manifest
scoop install packaging/scoop/redball.json

# Update
scoop update redball
```

### Chocolatey1

```powershell
cd packaging/chocolatey

# Build package
choco pack

# Install locally
choco install redball -s . --pre

# Uninstall
choco uninstall redball
```

## Manifest Maintenance

### When releasing a new version

1. **Automatic** (recommended):
   - Create GitHub release
   - GitHub Actions automatically updates manifests and publishes

2. **Semi-automatic**:

   ```bash
   # Run the update script
   ./scripts/packaging/update-package-managers.sh -v 2.1.456
   
   # Review changes
   git diff
   
   # Commit and push
   git add packaging/
   git commit -m "Update manifests for v2.1.456"
   git push
   ```

3. **Manual**:
   - Edit each manifest file
   - Update version, URLs, and SHA256 hashes
   - Test locally
   - Submit to respective package managers

### Version Number Format

All package managers use the same version format: `MAJOR.MINOR.PATCH`

Example: `2.1.455`

- `2` - Major version (breaking changes)
- `1` - Minor version (new features)
- `455` - Patch version (bug fixes)

## Troubleshooting

### SHA256 Mismatches

If you get SHA256 hash mismatches:

1. Download the actual release artifact:

   ```bash
   curl -L -o setup.exe https://github.com/ArMaTeC/Redball/releases/download/v2.1.455/Redball-2.1.455-Setup.exe
   ```

2. Calculate hash:

   ```bash
   sha256sum setup.exe
   ```

3. Update manifest with correct hash

### Chocolatey Moderation

Chocolatey packages go through automated moderation. Common issues:

- **Verification failed**: Ensure `VERIFICATION.txt` is accurate
- **Icon not accessible**: Icon URL must be publicly accessible
- **License not included**: Must include `LICENSE.txt`

### Winget Validation

Validate manifests before submission:

```bash
winget validate --manifest packaging/winget/
```

## Support

For package-specific issues:

- **Winget**: <https://github.com/microsoft/winget-pkgs/issues>
- **Scoop**: <https://github.com/ScoopInstaller/Scoop/issues>
- **Chocolatey**: <https://github.com/chocolatey/choco/issues>

For Redball-specific issues:

- <https://github.com/ArMaTeC/Redball/issues>
