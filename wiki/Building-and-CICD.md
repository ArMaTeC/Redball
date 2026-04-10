# Building & CI/CD

## NSIS Installer

The installer is built with [NSIS (Nullsoft Scriptable Install System)](https://nsis.sourceforge.io/) v3.0+.

### Build Scripts

**Unified Build Script (Windows - PowerShell):**

```powershell
# Full deploy pipeline (builds everything)
.\scripts\build.ps1 all

# Build Windows artifacts only (WPF, Service, Setup, ZIP)
.\scripts\build.ps1 windows

# Build with specific channel
.\scripts\build.ps1 all -Channel beta
```

**Unified Build Script (Linux - Bash):**

```bash
# Full auto-release workflow (builds + publishes everything)
./scripts/build.sh

# Build everything (no publish)
./scripts/build.sh all

# Build specific targets
./scripts/build.sh windows    # Windows artifacts
./scripts/build.sh linux      # Linux artifacts (GTK app, packages)
./scripts/build.sh update-server  # Update server
./scripts/build.sh website    # Website validation
```

### Build Commands

| Command         | Description                          |
| --------------- | ------------------------------------ |
| `all`           | Build everything (no publish)        |
| `auto-release`  | Build + publish everything [DEFAULT] |
| `windows`       | Build Windows artifacts              |
| `linux`         | Build Linux artifacts                |
| `update-server` | Build/validate update-server         |
| `website`       | Build website                        |
| `clean`         | Clean all build artifacts            |
| `publish`       | Publish release to update-server     |
| `serve`         | Start update-server locally          |
| `status`        | Show build status                    |

### Build Script Release Behaviour

For release builds, the build script commits and pushes the version bump before creating GitHub releases so release notes always include commit history.

```powershell
# Release build with specific version
.\scripts\build.ps1 publish -Version "2.1.500"

# Beta release
.\scripts\build.ps1 publish -Beta
```

### Deploy Pipeline

The build script automatically:

1. Builds the WPF application via `dotnet publish`
2. Builds the Windows Service (`Redball.Service`)
3. Creates an NSIS installer (`Redball-{version}-Setup.exe`)
4. Creates a portable ZIP archive
5. Builds Linux artifacts (Flatpak, DEB, tarball) if on Linux or WSL available
6. Validates the update-server
7. Signs artifacts with a code-signing certificate (creates self-signed cert if none exists)

### Installer Features

The NSIS installer provides:

- Per-user installation to `%LocalAppData%\Redball`
- Start Menu and Desktop shortcuts
- Optional "Start with Windows" option
- Service installation for input injection
- Auto-launch after install
- Silent install support (`/S`)
- Optional default behaviour features via registry defaults
- "Launch Redball" checkbox on the finish page

### Installer Theme

The installer uses modern dark-themed graphics:

- `nsis-header.bmp` — Dark gradient header with Redball branding
- `nsis-welcome.bmp` — Dark gradient background for welcome/finish pages
- Professional dark theme matching Redball's UI
- Windows 10/11 styled Modern UI 2

### Installer Files

| File                           | Description                             |
| ------------------------------ | --------------------------------------- |
| `installer/Redball.nsi`        | Main NSIS installer script              |
| `installer/Redball.ico`        | Application icon                        |
| `installer/nsis-header.bmp`    | NSIS header image                       |
| `installer/nsis-welcome.bmp`   | NSIS welcome/finish image               |
| `installer/Launch-Redball.vbs` | Hidden-window launcher for post-install |
| `installer/signpath.json`      | Code signing configuration              |

---

## CI/CD — GitHub Actions

### Release Workflow (`release.yml`)

**Trigger:** Push to `main` branch with version tag

**Steps:**

1. Check out repository
2. Extract version from `Directory.Build.props`
3. Create a Git tag for the version
4. Build the WPF application on `windows-latest`
5. Build the NSIS installer on `windows-latest`
6. Create a GitHub Release with the installer attached

### CI Workflow (`ci.yml`)

**Trigger:** Push or pull request to `main` or `develop` branches

**Steps:**

1. **Lint** — Run PSScriptAnalyzer on PowerShell scripts
2. **JSON Validation** — Validate all JSON files
3. **Security Scan** — Check for common security issues
4. **WPF Build** — Build the .NET 10 WPF application (via Wine on Linux runner)
5. **Unit Tests** — Run MSTest suite

### Linux CI Workflow (`linux-ci.yml`)

**Trigger:** Push or pull request affecting Linux-related files

**Steps:**

1. **Build Linux** — Build the GTK application on Ubuntu
2. **Build Packages** — Create Flatpak, DEB, and tarball packages
3. **Test** — Run Linux-specific tests

### Other Workflows

| Workflow         | Purpose                                 |
| ---------------- | --------------------------------------- |
| `security.yml`   | Security scanning and dependency checks |
| `e2e.yml`        | End-to-end testing                      |
| `virustotal.yml` | VirusTotal scanning for releases        |
| `cleanup.yml`    | Artifact cleanup                        |
| `docs.yml`       | Documentation validation                |

### Build Output

Build artifacts are placed in the `dist/` directory:

| File                          | Description                         |
| ----------------------------- | ----------------------------------- |
| `Redball.UI.WPF.exe`          | WPF executable                      |
| `Redball.Service.exe`         | Windows Service for input injection |
| `Redball-{version}-Setup.exe` | NSIS installer                      |
| `redball-portable.zip`        | Portable ZIP archive                |
| `redball-linux.tar.gz`        | Linux tarball                       |
| `redball.deb`                 | Debian package                      |
| `redball.flatpak`             | Flatpak bundle                      |

---

## Version Management

The version is centralized in `Directory.Build.props`:

```xml
<Version>2.1.492</Version>
```

This version is shared across all projects in the solution. The CI release workflow reads the version from this file to create the Git tag and GitHub Release.
