# Auto-Updater & Code Signing

## Auto-Updater

Redball includes a built-in update system that checks GitHub Releases for newer versions. Updates can be checked automatically in the background or manually from the UI.

### Update Channels

| Channel | Description |
| ------- | ----------- |
| `stable` | Production releases only (default) |
| `beta` | Pre-release and production releases |

### Checking for Updates

#### Automatic Background Checks

When `AutoUpdateCheckEnabled` is `true` (default), Redball checks for updates every `AutoUpdateCheckIntervalMinutes` minutes (default: 120). If an update is found, a toast notification is shown.

#### Via Main Window

1. Navigate to the **Updates** section in the main window
2. Click **Check for Updates Now**
3. If an update is available, release notes and download options are shown

#### Via About Dialog

1. Open the tray menu → **About...**
2. Click **Check for Updates**
3. If an update is available, a changelog dialog appears with release notes
4. Click **Download & Update** to download and install the update

### Update Process

1. `UpdateService.CheckForUpdateAsync()` queries the GitHub API
2. If a newer version exists, update details are shown in the update UI
3. User clicks download/update and progress is shown
4. If `VerifyUpdateSignature` is enabled, signature verification is performed before install
5. MSI installer is downloaded and launched

### Configuration

| Setting | Default | Description |
| ------- | ------- | ----------- |
| `AutoUpdateCheckEnabled` | `true` | Enable automatic background update checks |
| `AutoUpdateCheckIntervalMinutes` | `120` | Minutes between automatic checks |
| `UpdateRepoOwner` | `ArMaTeC` | GitHub account or organization |
| `UpdateRepoName` | `Redball` | GitHub repository name |
| `UpdateChannel` | `stable` | Release channel |
| `VerifyUpdateSignature` | `false` | Require valid digital signature |

### UpdateService API

| Method | Description |
| ------ | ----------- |
| `CheckForUpdateAsync()` | Queries GitHub API for the latest release |
| `DownloadUpdateAsync(updateInfo)` | Downloads the update MSI |

---

## Code Signing

Both the EXE and MSI are automatically code-signed during CI releases.

### Signing Details

- **Algorithm:** SHA-256 with RSA 2048-bit key
- **Timestamping:** DigiCert RFC 3161 timestamp server
- **Tool:** Windows SDK `signtool.exe`
- **Secrets:** `CODE_SIGNING_CERT` (base64 PFX) and `CODE_SIGNING_PASSWORD` stored as GitHub repository secrets

### Setting Up Code Signing

To use your own certificate, set the GitHub secrets:

```powershell
# Base64-encode your PFX certificate
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("your-cert.pfx"))
$base64 | gh secret set CODE_SIGNING_CERT --repo YourOrg/Redball
"your-password" | gh secret set CODE_SIGNING_PASSWORD --repo YourOrg/Redball
```

If no certificate secrets are configured, the CI creates a self-signed development certificate as a fallback.

### Deploy Pipeline Integration

The `Deploy-Redball.ps1` script automatically:

1. Finds or creates a code-signing certificate
2. Signs the compiled EXE
3. Signs the MSI installer
4. Uses `http://timestamp.digicert.com` for timestamping
