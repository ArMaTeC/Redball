# Auto-Updater & Code Signing

## Auto-Updater

Redball includes a built-in update system that checks GitHub Releases for newer versions.

### Update Channels

| Channel | Description |
| ------- | ----------- |
| `stable` | Production releases only (default) |
| `beta` | Pre-release and production releases |

### Checking for Updates

#### Via CLI

```powershell
# Check for updates (returns JSON)
.\Redball.ps1 -CheckUpdate

# Download and install the latest update
.\Redball.ps1 -Update
```

#### Via About Dialog

1. Open the tray menu → **About...**
2. Click **Check for Updates**
3. If an update is available, release notes are shown
4. Click **Download Update** to open the release page in your browser

### Update Process

When `-Update` is used:

1. `Test-RedballUpdateAvailable` queries the GitHub API
2. If a newer version exists, finds the `Redball.ps1` asset in the release
3. Downloads to `%TEMP%`
4. If `VerifyUpdateSignature` is enabled, verifies the Authenticode signature
5. Creates a backup of the current script (e.g., `Redball.ps1.bak.20240309123456`)
6. Replaces the current script with the downloaded version
7. Optionally restarts Redball with `-RestartAfterUpdate`

### Configuration

| Setting | Default | Description |
| ------- | ------- | ----------- |
| `UpdateRepoOwner` | `ArMaTeC` | GitHub account or organization |
| `UpdateRepoName` | `Redball` | GitHub repository name |
| `UpdateChannel` | `stable` | Release channel |
| `VerifyUpdateSignature` | `false` | Require valid digital signature |

### Functions (Update)

| Function | Description |
| -------- | ----------- |
| `Get-RedballLatestRelease` | Queries GitHub API for the latest release |
| `Test-RedballUpdateAvailable` | Compares versions and returns update status |
| `Install-RedballUpdate` | Downloads, verifies, backs up, and installs the update |

---

## Code Signing

Redball supports Authenticode code signing for script integrity verification.

### Signing the Script

```powershell
# Sign with auto-detected certificate
.\Redball.ps1 -SignScript

# Sign with a specific certificate
.\Redball.ps1 -SignScript -CertThumbprint "ABC123..."

# Sign with a custom timestamp server
.\Redball.ps1 -SignScript -TimestampServer "http://timestamp.digicert.com"

# Sign a different file
.\Redball.ps1 -SignScript -SignPath "C:\Scripts\MyScript.ps1"
```

### Certificate Selection

`Set-RedballCodeSignature` selects a certificate in this order:

1. If `-CertThumbprint` is provided, finds that specific cert
2. Otherwise, finds the newest valid code-signing cert in `Cert:\CurrentUser\My`
3. If no cert is found, offers to create a self-signed certificate

### Self-Signed Certificates

`New-RedballSelfSignedCodeSigningCertificate` creates a code-signing certificate with:

- Subject: `CN=Redball Self-Signed Code Signing`
- Store: `Cert:\CurrentUser\My`
- Validity: 3 years
- Key export: Enabled

### Verifying Signatures

```powershell
# Basic signature check
Test-RedballFileSignature -Path '.\Redball.ps1'

# Verify against a list of trusted thumbprints
Test-RedballFileSignature -Path '.\update.ps1' -AllowedThumbprints @('ABC123...')
```

### Functions

| Function | Description |
| -------- | ----------- |
| `Get-RedballCodeSigningCertificate` | Finds code-signing certs by thumbprint or newest |
| `New-RedballSelfSignedCodeSigningCertificate` | Creates a self-signed cert (3-year validity) |
| `Set-RedballCodeSignature` | Signs a script with Authenticode + timestamp |
| `Test-RedballFileSignature` | Verifies signature status and optional signer allowlist |

### Deploy Pipeline Integration

The `Deploy-Redball.ps1` script automatically:

1. Finds or creates a code-signing certificate
2. Signs the compiled EXE
3. Signs the MSI installer
4. Uses `http://timestamp.digicert.com` for timestamping
