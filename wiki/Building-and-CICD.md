# Building & CI/CD

## MSI Installer

The MSI is built with [WiX Toolset v4](https://wixtoolset.org/).

### Build Scripts

```powershell
# Full deploy pipeline (MSI via WiX, with code signing)
.\installer\Deploy-Redball.ps1

# Recommended local build pipeline
.\scripts\build.ps1

# Build MSI only
.\installer\Build-MSI.ps1 -Version "3.0.0"

# Build with specific WiX path
.\installer\Build-MSI.ps1 -WixBinPath "C:\Tools\wix"
```

### Build Script Release Behavior

For release builds (when MSI is enabled), `scripts/build.ps1` commits and pushes the version bump before calling `scripts/release.ps1` so GitHub release notes always include commit history.

```powershell
# Release build with default release commit message
.\scripts\build.ps1

# Override release commit message
.\scripts\build.ps1 -ReleaseMessage "chore(release): v3.1.0 + HID fixes"

# Opt out of auto release commit/push behavior
.\scripts\build.ps1 -SkipReleaseCommit
.\scripts\build.ps1 -SkipReleasePush
```

### Deploy Pipeline

`Deploy-Redball.ps1` automatically:

1. Increments the build number (stored in `scripts/version.txt`)
2. Builds the WPF application via `dotnet publish`
3. Builds an MSI via WiX
4. Signs both artifacts with a code-signing certificate (creates a self-signed cert if none exists)

`scripts/build.ps1` also builds the WPF application and generates the MSI installer.

### Installer Features

The MSI provides:

- Per-user installation to `%LocalAppData%\Redball`
- Start Menu and Desktop shortcuts
- Optional "Start with Windows" shortcut
- Optional default behavior features via registry defaults:
  - Battery-Aware Mode
  - Network-Aware Mode
  - Idle Detection
  - Start Minimized
  - Exit on Timer Complete
- "Launch Redball" checkbox on the finish page

### MSI Theme Generation

The installer uses modern dark-themed graphics generated programmatically:

```powershell
# Generate theme images manually (optional - build.ps1 does this automatically)
.\installer\Generate-InstallerTheme.ps1
```

**Generated Images:**

- `banner.bmp` (493x58) — Dark gradient header with Redball branding
- `dialog.bmp` (493x312) — Dark gradient background with sidebar accent

**Features:**

- Professional dark theme matching Redball's UI
- Anti-aliased Segoe UI typography
- Red accent bar with decorative circle elements
- Automatically regenerated during `build.ps1` MSI builds

### Installer Files

| File | Description |
| ---- | ----------- |
| `installer/Build-MSI.ps1` | WiX MSI build script (legacy v1) |
| `installer/Build-MSI-v2.ps1` | WiX MSI build script (modern v2 with WixUI_InstallDir) |
| `installer/Deploy-Redball.ps1` | Full deploy pipeline (WPF + MSI + signing) |
| `installer/Generate-InstallerTheme.ps1` | PowerShell theme generator for MSI banner/dialog images |
| `installer/Launch-Redball.vbs` | Hidden-window launcher for MSI post-install |
| `installer/Redball.wxs` | WiX v4 installer definition (legacy) |
| `installer/Redball.v2.wxs` | WiX v4 installer definition (modern UI) |
| `installer/banner.bmp` | MSI installer banner image (493x58) |
| `installer/dialog.bmp` | MSI installer dialog image (493x312) |
| `installer/Redball.ico` | Application icon |
| `installer/Redball-License.rtf` | License for installer UI |
| `installer/redball.png` | Readme icon image |
| `installer/signpath.json` | Code signing configuration |

---

## CI/CD — GitHub Actions

### Release Workflow (`release.yml`)

**Trigger:** Push to `main` branch

**Steps:**

1. Check out repository
2. Extract version from `src/Redball.UI.WPF/Redball.UI.WPF.csproj`
3. Create a Git tag for the version
4. Build the WPF application on `windows-latest`
5. Build the MSI on `windows-latest`
6. Create a GitHub Release with the MSI attached

### CI Workflow (`ci.yml`)

**Trigger:** Push or pull request to any branch

**Steps:**

1. **WPF Build** — Build the .NET 10 WPF application
2. **Unit Tests** — Run MSTest suite
3. **JSON Validation** — Validate `Redball.json` and `locales.json`
4. **PSScriptAnalyzer** — Lint build scripts
5. **Security Scan** — Basic security checks

### Build Output

Build artifacts are placed in the `dist/` directory:

| File | Description |
| ---- | ----------- |
| `Redball.UI.WPF.exe` | Self-contained WPF executable |
| `Redball-{version}.msi` | WiX MSI installer |

---

## Version Management

The version is stored in two places:

1. `src/Redball.UI.WPF/Redball.UI.WPF.csproj` — the canonical version
2. `scripts/version.txt` — auto-incremented build number used by the deploy pipeline

The CI release workflow reads the version from the project file to create the Git tag and GitHub Release.
