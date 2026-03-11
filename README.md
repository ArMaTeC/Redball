# Redball

[![PowerShell](https://img.shields.io/badge/PowerShell-5.1%2B-blue.svg)](https://github.com/PowerShell/PowerShell)
[![PS7 Compatible](https://img.shields.io/badge/PowerShell_7-Compatible-blueviolet.svg)](https://github.com/PowerShell/PowerShell)
[![Windows](https://img.shields.io/badge/Platform-Windows_8.1%2B-0078D6.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![CI](https://img.shields.io/github/actions/workflow/status/ArMaTeC/Redball/release.yml?label=Build)](https://github.com/ArMaTeC/Redball/actions/workflows/release.yml)

> A system tray utility to prevent Windows from sleeping, with style.

Redball is a PowerShell-based system tray application that keeps your Windows computer awake using the `SetThreadExecutionState` API. It features a custom 3D red ball icon that changes color based on state, smart monitoring features, a built-in settings GUI, auto-updating, code signing support, and an MSI installer — all in a single script.

![Redball Icon](installer/redball.png)

> **[Full Documentation Wiki](https://github.com/ArMaTeC/Redball/wiki)** — Comprehensive guides for every feature, function, and configuration option.

## Features

- **3D Red Ball Icon** — Dynamic GDI+ icon that changes color based on state:
  - **Bright Red** — Active and keeping system awake
  - **Orange/Red** — Timed mode with countdown
  - **Dark Red/Gray** — Paused / idle state
- **Timed Sessions** — Set duration (15, 30, 60, 120 minutes) or run indefinitely
- **Display Sleep Control** — Optionally keep display awake too
- **F15 Heartbeat** — Sends invisible F15 keypresses to prevent idle detection (only when system is actually idle)
- **Battery-Aware Mode** — Auto-pause when battery drops below a configurable threshold, resume on charge
- **Network-Aware Mode** — Auto-pause when network disconnects, resume on reconnect
- **Idle Detection** — Auto-pause after 30 minutes of user inactivity, resume on input
- **Scheduled Operation** — Auto-start/stop at configured times and days of the week
- **Presentation Mode Detection** — Auto-activate when PowerPoint is open or Teams is screen-sharing
- **Session Restore** — Saves state on exit, restores on next startup
- **Startup with Windows** — Launch automatically via startup shortcut or MSI installer option
- **Toast Notifications** — Native Windows 10/11 toast notifications with balloon tip fallback
- **Settings GUI** — Tabbed WinForms settings dialog (General, Power & Monitoring, Schedule, Advanced)
- **Structured Logging** — Rotating log files with configurable size limits and retry/fallback logic
- **JSON Configuration** — Persistent settings via `Redball.json`
- **Singleton Instance** — Named mutex prevents multiple instances; auto-stops stale processes
- **Crash Recovery** — Detects previous abnormal termination and resets to safe defaults
- **Auto-Updater** — Check for and install updates from GitHub Releases
- **Code Signing** — Sign scripts with `Set-AuthenticodeSignature` or `signtool.exe`
- **Settings Backup/Restore** — Export and import all settings to a JSON backup file
- **Process Isolation** — Optional runspace-based keep-awake for extra reliability
- **Global Hotkey** — Ctrl+Alt+Pause to toggle pause/resume system-wide
- **Localization (i18n)** — English, Spanish, French, and German with auto-detection
- **High DPI / High Contrast / Dark Mode** — System-aware rendering and icon fallback
- **Graceful Shutdown** — Handles Ctrl+C and terminal close without errors
- **TypeThing — Clipboard Typer** — Simulates human-like typing of clipboard contents via global hotkeys
  - Configurable typing speed (min/max delay), random pauses for realism
  - Countdown before typing starts, emergency stop hotkey
  - Unicode support via `SendInput` with `KEYEVENTF_UNICODE`
  - Themed settings dialog (light, dark, hacker)
  - Handles newlines, tabs, and large clipboard with confirmation prompt
- **About Dialog** — Version info with built-in update checker and download button
- **Pester Tests** — Comprehensive test suite included
- **MSI Installer** — WiX v4 installer with Start Menu/Desktop shortcuts, startup options, and feature selection
- **CI/CD** — GitHub Actions for automated testing, linting, security scanning, and release builds

## Quick Start

### Prerequisites

- **Windows 8.1** or later
- **PowerShell 5.1** or later (PowerShell 7+ also supported)

### Option A — MSI Installer (Recommended)

Download the latest `Redball.msi` from the [Releases](https://github.com/ArMaTeC/Redball/releases) page and run it. The installer provides:

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

## Configuration

Settings are stored in `Redball.json` in the same directory as the script. A default file is created on first run if one doesn't exist. You can also change all settings from the **Settings** dialog in the tray menu.

```json
{
    "HeartbeatSeconds": 59,
    "PreventDisplaySleep": true,
    "UseHeartbeatKeypress": true,
    "DefaultDuration": 60,
    "LogPath": "Redball.log",
    "MaxLogSizeMB": 10,
    "ShowBalloonOnStart": true,
    "Locale": "en",
    "MinimizeOnStart": false,
    "BatteryAware": false,
    "BatteryThreshold": 20,
    "NetworkAware": false,
    "IdleDetection": false,
    "AutoExitOnComplete": false,
    "ScheduleEnabled": false,
    "ScheduleStartTime": "09:00",
    "ScheduleStopTime": "18:00",
    "ScheduleDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
    "PresentationModeDetection": false,
    "ProcessIsolation": false,
    "EnablePerformanceMetrics": false,
    "EnableTelemetry": false,
    "UpdateRepoOwner": "karl-lawrence",
    "UpdateRepoName": "Redball",
    "UpdateChannel": "stable",
    "VerifyUpdateSignature": false
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `HeartbeatSeconds` | Interval between keep-awake refreshes and F15 keypresses | `59` |
| `PreventDisplaySleep` | Keep the display on in addition to preventing system sleep | `true` |
| `UseHeartbeatKeypress` | Send invisible F15 keypresses to prevent app-level idle detection | `true` |
| `DefaultDuration` | Default timer duration in minutes | `60` |
| `LogPath` | Path to log file | `Redball.log` |
| `MaxLogSizeMB` | Log rotation threshold in MB | `10` |
| `ShowBalloonOnStart` | Show tray notification when Redball starts | `true` |
| `Locale` | Display language (`en`, `es`, `fr`, `de`) | Auto-detected |
| `MinimizeOnStart` | Start minimized to system tray | `false` |
| `BatteryAware` | Auto-pause when battery is low | `false` |
| `BatteryThreshold` | Battery % below which to auto-pause | `20` |
| `NetworkAware` | Auto-pause when network disconnects | `false` |
| `IdleDetection` | Auto-pause after 30 min of user inactivity | `false` |
| `AutoExitOnComplete` | Exit automatically when a timed session finishes | `false` |
| `ScheduleEnabled` | Enable daily scheduled activation | `false` |
| `ScheduleStartTime` | Time to auto-start (HH:mm) | `09:00` |
| `ScheduleStopTime` | Time to auto-stop (HH:mm) | `18:00` |
| `ScheduleDays` | Days of the week the schedule applies | Weekdays |
| `PresentationModeDetection` | Auto-activate for PowerPoint/Teams presentations | `false` |
| `ProcessIsolation` | Run keep-awake API in a separate runspace | `false` |
| `EnablePerformanceMetrics` | Track CPU, memory, and handle metrics | `false` |
| `EnableTelemetry` | Opt-in anonymous usage telemetry (logged locally) | `false` |
| `UpdateRepoOwner` | GitHub owner for update checks | `karl-lawrence` |
| `UpdateRepoName` | GitHub repo for update checks | `Redball` |
| `UpdateChannel` | Release channel (`stable` or `beta`) | `stable` |
| `VerifyUpdateSignature` | Require valid digital signature on updates | `false` |
| `TypeThingEnabled` | Enable the clipboard typing feature | `true` |
| `TypeThingMinDelayMs` | Minimum delay between keystrokes (ms) | `30` |
| `TypeThingMaxDelayMs` | Maximum delay between keystrokes (ms) | `120` |
| `TypeThingStartDelaySec` | Countdown seconds before typing begins | `3` |
| `TypeThingStartHotkey` | Global hotkey to start typing | `Ctrl+Shift+V` |
| `TypeThingStopHotkey` | Global hotkey to stop typing | `Ctrl+Shift+X` |
| `TypeThingTheme` | Settings dialog theme (`light`, `dark`, `hacker`) | `dark` |
| `TypeThingAddRandomPauses` | Add occasional longer pauses for realism | `true` |
| `TypeThingRandomPauseChance` | Chance (%) of a random pause per character | `5` |
| `TypeThingRandomPauseMaxMs` | Maximum random pause duration (ms) | `500` |
| `TypeThingTypeNewlines` | Press Enter when a newline is encountered | `true` |
| `TypeThingNotifications` | Show tray notifications for typing events | `true` |

## Usage

### Tray Icon Menu

Right-click the red ball icon in your system tray:

| Menu Item | Description |
| --------- | ----------- |
| **Status** | Read-only status line (active state, display, F15, timer) |
| **Pause / Resume Keep-Awake** | Toggle the keep-awake state |
| **Prevent Display Sleep** | Toggle display sleep prevention |
| **Use F15 Heartbeat** | Toggle invisible F15 keypresses |
| **Stay Awake For →** | Choose duration (15 / 30 / 60 / 120 min) |
| **Stay Awake Until Paused** | Run indefinitely |
| **Battery-Aware Mode** | Toggle auto-pause on low battery |
| **Start with Windows** | Toggle startup shortcut |
| **Network-Aware Mode** | Toggle auto-pause on disconnect |
| **Idle Detection (30min)** | Toggle idle-based auto-pause |
| **TypeThing →** | Clipboard typer submenu (Type Clipboard, Stop, Status, Settings) |
| **Settings...** | Open the tabbed settings dialog |
| **About...** | Version info and update checker |
| **Exit** | Close Redball gracefully |

### Keyboard Shortcuts

| Shortcut | Action |
| -------- | ------ |
| **Space** | Toggle pause / resume |
| **D** | Toggle display sleep prevention |
| **H** | Toggle F15 heartbeat |
| **I** | Stay awake indefinitely |
| **B** | Toggle battery-aware mode |
| **S** | Toggle startup with Windows |
| **N** | Toggle network-aware mode |
| **L** | Toggle idle detection |
| **G** | Open settings dialog |
| **A** | Open About dialog |
| **X** | Exit application |
| **Ctrl+Shift+V** | TypeThing: Start typing clipboard (global) |
| **Ctrl+Shift+X** | TypeThing: Emergency stop typing (global) |

- **Double-click icon** — Toggle pause / resume
- **Ctrl+Alt+Pause** — Global hotkey to toggle (system-wide)

### Command Line Interface

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

## Settings GUI

Open the settings dialog from the tray menu (**Settings...** or press **G**). The dialog has five tabs:

- **General** — Duration, heartbeat interval, locale, minimize/exit behavior
- **Power & Monitoring** — Display sleep, F15 heartbeat, battery-aware, network-aware, idle detection, presentation mode
- **Schedule** — Enable/disable scheduled operation, start/stop times, active days
- **Advanced** — Log size, process isolation, performance metrics, telemetry, update channel, signature verification
- **TypeThing** — Enable/disable, typing speed, start delay, hotkeys, random pauses, newlines, notifications, theme

Changes are saved to `Redball.json` when you click **OK**.

TypeThing also has its own dedicated settings dialog (accessible from the TypeThing tray submenu) with grouped controls for speed, behaviour, hotkeys, and appearance — including a live WPM estimate and theme preview.

## API Reference

### Key Functions

#### `Set-KeepAwakeState`

Controls the Windows power state using `SetThreadExecutionState`.

```powershell
Set-KeepAwakeState -Enable:$true   # Prevent sleep
Set-KeepAwakeState -Enable:$false  # Allow sleep (reset)
```

#### `Set-ActiveState`

Sets the active state with optional timer.

```powershell
Set-ActiveState -Active:$true                                    # Active indefinitely
Set-ActiveState -Active:$true -Until (Get-Date).AddMinutes(30)  # Active for 30 min
Set-ActiveState -Active:$false                                   # Deactivate
```

#### `Start-TimedAwake`

Start a timed session.

```powershell
Start-TimedAwake -Minutes 60  # Active for 1 hour (1-720 valid range)
```

#### `Write-RedballLog`

Write to the structured log with retry logic and fallback.

```powershell
Write-RedballLog -Level 'INFO' -Message 'Started successfully'
Write-RedballLog -Level 'ERROR' -Message 'Something went wrong'
```

#### `Export-RedballSettings` / `Import-RedballSettings`

Backup and restore all settings.

```powershell
Export-RedballSettings -Path '.\Redball.backup.json'
Import-RedballSettings -Path '.\Redball.backup.json'
```

#### `Switch-ActiveState`

Toggles between active and paused.

```powershell
Switch-ActiveState  # If active → pause; if paused → resume
```

#### `Exit-Application`

Gracefully shuts down Redball — hides the tray icon, resets power state, saves session/config, disposes resources, and releases the singleton mutex.

#### `Import-RedballConfig` / `Save-RedballConfig`

Load or persist the JSON configuration.

```powershell
Import-RedballConfig -Path '.\Redball.json'
Save-RedballConfig -Path '.\Redball.json'
```

#### `Save-RedballState` / `Restore-RedballState`

Session state persistence for restart continuity.

```powershell
Save-RedballState       # Writes Redball.state.json
Restore-RedballState    # Restores from Redball.state.json
```

### Monitoring Functions

| Function | Description |
| -------- | ----------- |
| `Get-BatteryStatus` | Returns battery info (charge %, on-battery, has-battery) with 30s cache |
| `Test-BatteryThreshold` | Checks if battery is below the configured threshold |
| `Update-BatteryAwareState` | Auto-pause/resume based on battery level |
| `Get-NetworkStatus` | Returns network adapter connection status |
| `Update-NetworkAwareState` | Auto-pause/resume on network disconnect/reconnect |
| `Get-IdleTimeMinutes` | Returns user idle time in minutes via `GetLastInputInfo` |
| `Update-IdleAwareState` | Auto-pause after 30 min idle, resume on input |
| `Test-ScheduleActive` | Checks if current time is within the configured schedule |
| `Update-ScheduleState` | Auto-start/stop based on time-of-day schedule |
| `Test-PresentationMode` | Detects PowerPoint, Teams screen-sharing, or Windows presentation mode |
| `Update-PresentationModeState` | Auto-activate when a presentation is detected |

### Instance & Recovery Functions

| Function | Description |
| -------- | ----------- |
| `Test-RedballInstanceRunning` | Checks for an existing Redball instance via named mutex |
| `Initialize-RedballSingleton` | Creates the singleton mutex for this instance |
| `Get-RedballProcess` | Finds other PowerShell processes running Redball |
| `Stop-RedballProcess` | Gracefully stops a Redball process (with optional force kill) |
| `Clear-RedballLogLock` | Clears file locks on the log file from stale processes |
| `Test-CrashRecovery` | Detects previous abnormal termination and resets to safe defaults |
| `Clear-CrashFlag` | Removes the crash detection flag file |
| `Test-ExecutionPolicy` | Validates the PowerShell execution policy allows Redball to run |

### Update & Signing Functions

| Function | Description |
| -------- | ----------- |
| `Get-RedballLatestRelease` | Queries GitHub API for the latest release |
| `Test-RedballUpdateAvailable` | Compares current version against latest release |
| `Install-RedballUpdate` | Downloads and installs the latest release with backup |
| `Get-RedballCodeSigningCertificate` | Finds a code-signing cert by thumbprint or newest |
| `New-RedballSelfSignedCodeSigningCertificate` | Creates a self-signed code-signing cert |
| `Set-RedballCodeSignature` | Signs a script with Authenticode |
| `Test-RedballFileSignature` | Verifies Authenticode signature with optional signer allowlist |

### UI & System Functions

| Function | Description |
| -------- | ----------- |
| `Get-CustomTrayIcon` | Generates the 3D ball icon (active/timed/paused) via GDI+ |
| `Get-StatusText` | Returns the current status string for tooltip/menu |
| `Update-RedballUI` | Refreshes icon, tooltip, and menu items (only on state change) |
| `Show-RedballSettings` | Opens the full tabbed settings dialog |
| `Show-RedballSettingsDialog` | Alternative simplified settings dialog |
| `Show-AboutDialog` | Shows version info with update checker |
| `Send-RedballToast` | Sends a Windows toast notification (with balloon fallback) |
| `Send-HeartbeatKey` | Sends an invisible F15 keypress when system is idle |
| `Register-GlobalHotkey` / `Unregister-GlobalHotkey` | Manages the Ctrl+Alt+Pause global hotkey |
| `Install-RedballStartup` / `Uninstall-RedballStartup` | Manages Windows startup shortcut |
| `Test-RedballStartup` | Checks if the startup shortcut exists |
| `Import-RedballInstallerDefaults` | Reads MSI installer defaults from registry |
| `Import-RedballLocales` / `Get-LocalizedString` | Loads and resolves i18n locale strings |
| `Test-HighContrastMode` / `Update-HighContrastUI` | Detects and adapts to high contrast mode |
| `Enable-HighDPI` | Enables per-monitor DPI awareness |
| `Test-DarkMode` / `Update-DarkModeUI` | Detects Windows dark mode |
| `Update-PerformanceMetrics` | Tracks CPU, memory, and heartbeat metrics |
| `Send-RedballTelemetry` | Logs opt-in anonymous usage telemetry |
| `Get-RedballStatus` | Returns full status as JSON (for `-Status` CLI) |
| `Start-KeepAwakeRunspace` / `Stop-KeepAwakeRunspace` | Process isolation via background runspace |

### TypeThing (Clipboard Typer) Functions

| Function | Description |
| -------- | ----------- |
| `Start-TypeThingTyping` | Reads clipboard and begins simulated typing with countdown |
| `Stop-TypeThingTyping` | Emergency-stops typing and resets state |
| `Complete-TypeThingTyping` | Called when all characters have been typed successfully |
| `Start-TypeThingTimer` | Creates and starts the per-character WinForms timer |
| `Send-TypeThingChar` | Sends a single character via `SendInput` / `KEYEVENTF_UNICODE` |
| `Get-ClipboardText` | Gets text from clipboard with retry logic |
| `ConvertTo-HotkeyParams` | Parses a hotkey string (e.g. `Ctrl+Shift+V`) into modifier flags and VK code |
| `Register-TypeThingHotkeys` / `Unregister-TypeThingHotkeys` | Manages TypeThing global hotkeys |
| `Show-TypeThingSettings` | Opens the themed TypeThing settings dialog |
| `Get-TypeThingTheme` / `Set-TypeThingFormTheme` | Theme engine for the TypeThing settings dialog |

### State Object

Access the current state via `$script:state`:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Active` | bool | Currently keeping system awake |
| `PreventDisplaySleep` | bool | Display sleep prevention enabled |
| `UseHeartbeatKeypress` | bool | F15 keypresses enabled |
| `HeartbeatSeconds` | int | Interval between heartbeats |
| `Until` | DateTime? | Timer expiration (null if indefinite) |
| `BatteryAware` | bool | Battery monitoring enabled |
| `NetworkAware` | bool | Network monitoring enabled |
| `IdleDetection` | bool | Idle detection enabled |
| `IsShuttingDown` | bool | Shutdown in progress |
| `SessionId` | string | Unique GUID for the current session |
| `AutoPausedNetwork` | bool | Currently auto-paused due to network disconnect |
| `AutoPausedIdle` | bool | Currently auto-paused due to user idle |
| `AutoPausedPresentation` | bool | Currently auto-activated for presentation |
| `AutoPausedSchedule` | bool | Currently auto-paused by schedule |
| `ManualOverride` | bool | User manually overrode scheduled operation |
| `StartTime` | DateTime | When the current session started |
| `TypeThingIsTyping` | bool | TypeThing is currently typing |
| `TypeThingShouldStop` | bool | Emergency stop requested |
| `TypeThingIndex` | int | Current character position in typing |
| `TypeThingTotalChars` | int | Total characters to type |

## Testing

Run the Pester test suite:

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

## Building

### MSI Installer

The MSI is built with [WiX Toolset v4](https://wixtoolset.org/). Use the included build scripts:

```powershell
# Full deploy pipeline (EXE via ps2exe + MSI via WiX, with code signing)
.\installer\Deploy-Redball.ps1

# Build MSI only
.\installer\Build-MSI.ps1 -Version "2.0.9"

# Build with specific WiX path
.\installer\Build-MSI.ps1 -WixBinPath "C:\Tools\wix"
```

The deploy script automatically:

1. Increments the build number (stored in `.buildversion`)
2. Compiles an EXE via `ps2exe`
3. Builds an MSI via WiX
4. Signs both artifacts with a code-signing certificate (creates a self-signed cert if none exists)

### CI/CD

GitHub Actions workflows are included:

- **`release.yml`** — On push to `main`: auto-tags the version from the script, builds the MSI on `windows-latest`, and creates a GitHub Release with the MSI attached.
- **`ci.yml`** — On push/PR: runs Pester tests, PSScriptAnalyzer lint, JSON validation, and a basic security scan.

## Architecture

```text
Redball/
├── .github/
│   └── workflows/
│       ├── ci.yml                # CI pipeline (test, lint, security)
│       └── release.yml           # Release pipeline (tag, build MSI, publish)
├── installer/
│   ├── Build-MSI.ps1             # WiX MSI build script
│   ├── Deploy-Redball.ps1        # Full deploy pipeline (EXE + MSI + signing)
│   ├── Launch-Redball.vbs        # Hidden-window launcher for MSI post-install
│   ├── Redball.wxs               # WiX v4 installer definition
│   ├── Redball.ico               # Application icon
│   ├── Redball-License.rtf       # License for installer UI
│   └── redball.png               # Readme icon image
├── dist/                         # Build output (MSI, EXE)
├── docs/                         # Documentation (CHANGELOG, LICENSE, etc.)
├── Redball.ps1                   # Main application script
├── Redball.json                  # Configuration file
├── Redball.Tests.ps1             # Pester test suite
├── locales.json                  # External locale overrides (en, es, fr, de)
├── .buildversion                 # Auto-incremented build number
├── LICENSE                       # MIT License
└── README.md                     # This file
```

### Component Flow

1. **Startup** — Singleton check → crash recovery → load config → restore session state → apply installer defaults → apply CLI parameters → initialize locales
2. **Heartbeat Timer** — Fires every N seconds to refresh `SetThreadExecutionState` and send F15 keypress (if system is idle)
3. **Duration Timer** — Fires every second to check: timer expiration, battery, network, idle, schedule, and presentation mode
4. **UI Updates** — Refresh icon color and tooltip only when state changes (performance-optimized)
5. **TypeThing Init** — Create hotkey message window → register global hotkeys → listen for Ctrl+Shift+V / Ctrl+Shift+X
6. **Shutdown** — Stop TypeThing → unregister hotkeys → hide icon → reset power state → save session & config → stop timers → dispose resources → release mutex → exit

## Troubleshooting

### Tray icon not appearing

- Check Windows notification area settings → "Select which icons appear on the taskbar"
- Click "Show hidden icons" (the `^` arrow) in the system tray
- Restart Windows Explorer if needed: `Stop-Process -Name explorer -Force`

### System still sleeps

- Check Windows power plan settings (some plans override API calls)
- Ensure no group policy is overriding `SetThreadExecutionState`
- Try enabling **Prevent Display Sleep** in the tray menu
- Try enabling **Process Isolation** in Settings → Advanced

### Multiple instances conflict

Redball uses a named mutex to enforce a single instance. If a stale instance is detected, it will attempt to stop it automatically. If that fails:

```powershell
# Find and stop Redball processes manually
Get-Process powershell, pwsh | Where-Object { $_.CommandLine -like '*Redball*' } | Stop-Process -Force
```

### Log file locked

If the log file is locked by a previous instance, Redball automatically retries with exponential backoff and falls back to `%TEMP%\Redball_fallback.log`.

### `$PSScriptRoot` empty

This happens when running from ISE or VS Code without saving. Either:

- Save the file first, then run
- Specify the config path: `.\Redball.ps1 -ConfigPath ".\Redball.json"`

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Run tests (`Invoke-Pester -Path .\Redball.Tests.ps1 -Output Detailed`)
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

### Development Setup

```powershell
git clone https://github.com/ArMaTeC/Redball.git
cd Redball

# Run in development mode
.\Redball.ps1 -ConfigPath ".\Redball.json"

# Run tests
Invoke-Pester -Path ".\Redball.Tests.ps1" -Output Detailed

# Build installer locally
.\installer\Deploy-Redball.ps1 -BuildMsi
```

## Roadmap

- [x] Keyboard shortcuts (tray menu access keys)
- [x] Multiple language support (i18n — en, es, fr, de)
- [x] GUI configuration editor (tabbed settings dialog)
- [x] MSI installer (WiX v4 with feature selection)
- [x] Auto-start with Windows
- [x] Network-aware mode
- [x] Dark mode detection
- [x] High contrast / High DPI awareness
- [x] PowerShell Core (7.x) compatibility
- [x] Battery-aware mode
- [x] Idle detection
- [x] Scheduled operation
- [x] Presentation mode detection
- [x] Session restore
- [x] Toast notifications
- [x] Auto-updater (GitHub Releases)
- [x] Code signing support
- [x] Singleton instance management
- [x] Crash recovery
- [x] Process isolation
- [x] Settings backup/restore
- [x] Global hotkey (Ctrl+Alt+Pause)
- [x] CI/CD (GitHub Actions)
- [x] Performance metrics
- [x] TypeThing clipboard typer with global hotkeys
- [x] About dialog with update checker
- [x] Themed TypeThing settings dialog (light/dark/hacker)

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

**Manufacturer:** GCI Network Solutions

## Acknowledgments

- Icon design using System.Drawing GDI+ path gradients
- [WiX Toolset](https://wixtoolset.org/) for the MSI installer
- [ps2exe](https://github.com/MScholtes/PS2EXE) for EXE compilation
- PowerShell community for best practices

## Support

- [Report bugs](https://github.com/ArMaTeC/Redball/issues)
- [Request features](https://github.com/ArMaTeC/Redball/issues)
- [Discussions](https://github.com/ArMaTeC/Redball/discussions)
