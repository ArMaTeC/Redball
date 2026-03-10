# Building & CI/CD

## MSI Installer

The MSI is built with [WiX Toolset v4](https://wixtoolset.org/).

### Build Scripts

```powershell
# Full deploy pipeline (EXE via ps2exe + MSI via WiX, with code signing)
.\installer\Deploy-Redball.ps1

# Build MSI only
.\installer\Build-MSI.ps1 -Version "2.0.29"

# Build with specific WiX path
.\installer\Build-MSI.ps1 -WixBinPath "C:\Tools\wix"
```

### Deploy Pipeline

`Deploy-Redball.ps1` automatically:

1. Increments the build number (stored in `.buildversion`)
2. Compiles an EXE via `ps2exe`
3. Builds an MSI via WiX
4. Signs both artifacts with a code-signing certificate (creates a self-signed cert if none exists)

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
- VBS launcher for hidden-window execution

### Installer Files

| File | Description |
| ---- | ----------- |
| `installer/Build-MSI.ps1` | WiX MSI build script |
| `installer/Deploy-Redball.ps1` | Full deploy pipeline (EXE + MSI + signing) |
| `installer/Launch-Redball.vbs` | Hidden-window launcher for MSI post-install |
| `installer/Redball.wxs` | WiX v4 installer definition |
| `installer/Redball.ico` | Application icon |
| `installer/Redball-License.rtf` | License for installer UI |
| `installer/redball.png` | Readme icon image |

---

## CI/CD — GitHub Actions

### Release Workflow (`release.yml`)

**Trigger:** Push to `main` branch

**Steps:**

1. Check out repository
2. Extract version from `$script:VERSION` in `Redball.ps1`
3. Create a Git tag for the version
4. Build the MSI on `windows-latest`
5. Create a GitHub Release with the MSI attached

### CI Workflow (`ci.yml`)

**Trigger:** Push or pull request to any branch

**Steps:**

1. **Pester Tests** — Run the full test suite
2. **PSScriptAnalyzer** — Lint the PowerShell code
3. **JSON Validation** — Validate `Redball.json` and `locales.json`
4. **Security Scan** — Basic security checks

### Running Tests Locally

```powershell
# Install Pester if needed
Install-Module Pester -Force -SkipPublisherCheck

# Run all tests
Invoke-Pester -Path ".\Redball.Tests.ps1"

# Run with detailed output
Invoke-Pester -Path ".\Redball.Tests.ps1" -Output Detailed

# Run specific test block
Invoke-Pester -Path ".\Redball.Tests.ps1" -TestName "*Icon*"
```

### Build Output

Build artifacts are placed in the `dist/` directory:

| File | Description |
| ---- | ----------- |
| `Redball.exe` | Compiled EXE (via ps2exe) |
| `Redball-{version}.msi` | WiX MSI installer |

---

## Version Management

The version is stored in two places:

1. `$script:VERSION` in `Redball.ps1` (line ~80) — the canonical version
2. `.buildversion` — auto-incremented build number used by the deploy pipeline

The CI release workflow reads the version from the script to create the Git tag and GitHub Release.
