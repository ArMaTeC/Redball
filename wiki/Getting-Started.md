# Getting Started

## Prerequisites

- **Windows 10** or later
- **.NET 10 Runtime** (included in the self-contained EXE — no separate install needed)

## Installation

### Option A — MSI Installer (Recommended)

Download the latest `Redball.msi` from the [Releases](https://github.com/ArMaTeC/Redball/releases) page and run it.

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

### Option C — Maintenance Arguments (Advanced)

Current command-line arguments are maintenance-focused:

| Argument | Description |
| -------- | ----------- |
| `--install-service` | Install the Redball Input Service for elevated typing |
| `--uninstall-service` | Uninstall the Redball Input Service |

## First Run

On first run, Redball will:

1. Create or load your configuration from `HKCU\Software\Redball\UserData` (with file copy in `%LocalAppData%\Redball\UserData\Redball.json`)
2. Check for singleton instance (only one Redball can run at a time)
3. Check for crash recovery from a previous abnormal termination
4. Display a bright red 3D ball icon in your system tray
5. Restore previous session state if available, otherwise start active keep-awake mode
6. Initialize TypeThing clipboard typer with default hotkeys (Ctrl+Shift+V / Ctrl+Shift+X)

The main window opens with a modern custom chrome design featuring:

- **Title bar** with app icon, title, and window controls (minimize, maximize, close)
- **Left navigation panel** with nine sections: Home, Analytics, SLO Dashboard, Diagnostics, Settings, Behavior, Smart Features, TypeThing, and Updates
- **Content area** showing the selected section's controls and information

Right-click the tray icon to access quick controls, or use the main window for full access to all features and settings.
