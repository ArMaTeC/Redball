# Getting Started

## Prerequisites

- **Windows 8.1** or later
- **PowerShell 5.1** or later (PowerShell 7+ also supported)

## Installation

### Option A — MSI Installer (Recommended)

Download the latest `Redball.msi` from the [Releases](https://github.com/karl-lawrence/Redball/releases) page and run it.

The installer provides:

- Per-user installation to `%LocalAppData%\Redball`
- Start Menu and Desktop shortcuts
- Optional "Start with Windows" shortcut
- Optional default behavior features (battery-aware, network-aware, idle detection, etc.)
- "Launch Redball" checkbox on the finish page

### Option B — Run the Script Directly

```powershell
# Run with default settings
.\Redball.ps1

# Run with custom config path
.\Redball.ps1 -ConfigPath "C:\Tools\Redball.json"
```

### Option C — Command Line Options

```powershell
# Install to Windows startup
.\Redball.ps1 -Install

# Remove from Windows startup
.\Redball.ps1 -Uninstall

# Run for 60 minutes then exit
.\Redball.ps1 -Duration 60 -ExitOnComplete

# Start minimized to tray
.\Redball.ps1 -Minimized

# Get current status as JSON
.\Redball.ps1 -Status | ConvertFrom-Json

# Check for updates
.\Redball.ps1 -CheckUpdate

# Install the latest update from GitHub
.\Redball.ps1 -Update

# Sign the script with a code-signing certificate
.\Redball.ps1 -SignScript [-CertThumbprint <thumbprint>] [-TimestampServer <url>]
```

## CLI Parameters

| Parameter | Description |
| --------- | ----------- |
| `-Install` | Add Redball to Windows startup |
| `-Uninstall` | Remove from Windows startup |
| `-Duration <N>` | Run for N minutes (1–720) |
| `-ExitOnComplete` | Exit after timed duration completes |
| `-Minimized` | Start minimized to system tray |
| `-Status` | Output JSON status and exit |
| `-CheckUpdate` | Check GitHub for a newer version and exit |
| `-Update` | Download and install the latest release |
| `-SignScript` | Sign the script with an Authenticode certificate |
| `-SignPath` | Path to the file to sign (defaults to `Redball.ps1`) |
| `-CertThumbprint` | Certificate thumbprint to use for signing |
| `-TimestampServer` | RFC 3161 timestamp server URL |
| `-ConfigPath` | Specify a custom config file path |

## First Run

On first run, Redball will:

1. Create a default `Redball.json` configuration file in the script directory
2. Check for singleton instance (only one Redball can run at a time)
3. Check for crash recovery from a previous abnormal termination
4. Display a bright red 3D ball icon in your system tray
5. Begin keeping your system awake immediately
6. Initialize TypeThing clipboard typer with default hotkeys (Ctrl+Shift+V / Ctrl+Shift+X)

Right-click the tray icon to access all features and settings.

## Execution Policy

If PowerShell blocks the script, use one of these methods:

```powershell
# Method 1: Set execution policy for current user
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Method 2: Run with bypass
PowerShell -ExecutionPolicy Bypass -File .\Redball.ps1

# Method 3: Use the -Install parameter (handles bypass automatically)
.\Redball.ps1 -Install
```
