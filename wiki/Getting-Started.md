# Getting Started

## Prerequisites

- **Windows 10** or later
- **.NET 8 Runtime** (included in the self-contained EXE — no separate install needed)

## Installation

### Option A — MSI Installer (Recommended)

Download the latest `Redball.msi` from the [Releases](https://github.com/karl-lawrence/Redball/releases) page and run it.

The installer provides:

- Per-user installation to `%LocalAppData%\Redball`
- Start Menu and Desktop shortcuts
- Optional "Start with Windows" shortcut
- Optional default behavior features (battery-aware, network-aware, idle detection, etc.)
- "Launch Redball" checkbox on the finish page

### Option B — Run the Executable

If you have the self-contained EXE from the repository or a custom build:

```powershell
# Run the WPF application
.\Redball.UI.WPF.exe

# The application will start minimized to the system tray
# Right-click the tray icon to access all features
```

### Option C — Command Line Arguments

```powershell
# Start minimized to tray
.\Redball.UI.WPF.exe -minimized

# Start with specific config path
.\Redball.UI.WPF.exe -config "C:\Tools\Redball.json"

# Show help
.\Redball.UI.WPF.exe -help
```

| Argument | Description |
| -------- | ----------- |
| `-minimized` | Start minimized to system tray |
| `-config <path>` | Specify a custom config file path |
| `-help` | Show help information |

## First Run

On first run, Redball will:

1. Create a default `Redball.json` configuration file in the application directory
2. Check for singleton instance (only one Redball can run at a time)
3. Check for crash recovery from a previous abnormal termination
4. Display a bright red 3D ball icon in your system tray
5. Begin keeping your system awake immediately
6. Initialize TypeThing clipboard typer with default hotkeys (Ctrl+Shift+V / Ctrl+Shift+X)

Right-click the tray icon to access all features and settings.
