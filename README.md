# Redball

[![PowerShell](https://img.shields.io/badge/PowerShell-5.1+-blue.svg)](https://github.com/PowerShell/PowerShell)
[![Windows](https://img.shields.io/badge/Platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> A system tray utility to prevent Windows from sleeping, with style.

Redball is a PowerShell-based system tray application that keeps your Windows computer awake using the `SetThreadExecutionState` API. It features a beautiful 3D red ball icon that changes color based on state, comprehensive logging, graceful shutdown handling, and extensive configuration options.

![Redball Icon](installer/redball.png)

## Features ✨

- 🎨 **3D Red Ball Icon** - Dynamic icon that changes color based on state:
  - 🔴 **Bright Red** - Active and keeping system awake
  - 🟠 **Orange/Red** - Timed mode with countdown
  - ⚫ **Dark Red/Gray** - Paused/idle state
- ⏱️ **Timed Sessions** - Set duration (15, 30, 60, 120 minutes) or run indefinitely
- 🖥️ **Display Sleep Control** - Optionally keep display awake too
- 🎹 **F15 Heartbeat** - Sends invisible F15 keypresses to prevent idle detection
- 🔋 **Battery-Aware Mode** - Auto-pause when battery < 20%, resume on charge
- 🌐 **Network-Aware Mode** - Auto-pause when network disconnects
- 😴 **Idle Detection** - Auto-pause after 30 minutes of user inactivity
- 📅 **Scheduled Operation** - Auto-start/stop at scheduled times (9 AM - 6 PM weekdays)
- 📊 **Presentation Mode Detection** - Auto-activate for PowerPoint/Teams screenshare
- 💾 **Session Restore** - Saves state on exit, restores on startup
- 🚀 **Startup with Windows** - Launch automatically with Windows
- 🔔 **Toast Notifications** - Native Windows 10/11 notifications
- 📝 **Structured Logging** - Rotating log files with configurable size limits
- ⚙️ **JSON Configuration** - Persistent settings via `Redball.json`
- 🛡️ **Graceful Shutdown** - Handles Ctrl+C and terminal close without errors
- 🧪 **Pester Tests** - Comprehensive test suite included

## Quick Start 🚀

### Prerequisites

- Windows 8.1 or later
- PowerShell 5.1 or later
- Administrative privileges (for `SetThreadExecutionState` API)

### Installation

1. Download `Redball.ps1` and `Redball.json`
2. Right-click `Redball.ps1` → "Run with PowerShell"

Or from PowerShell:

```powershell
# Run with default settings
.\Redball.ps1

# Run with custom config path
.\Redball.ps1 -ConfigPath "C:\Tools\Redball.json"
```

## Configuration ⚙️

Create a `Redball.json` file in the same directory:

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
    "ScheduleEnabled": false,
    "ScheduleStartTime": "09:00",
    "ScheduleStopTime": "18:00",
    "ScheduleDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
    "PresentationModeDetection": false
}
```

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `HeartbeatSeconds` | Interval between F15 keypresses | 59 |
| `PreventDisplaySleep` | Keep display awake when active | true |
| `UseHeartbeatKeypress` | Send F15 keypresses | true |
| `DefaultDuration` | Default timer duration (minutes) | 60 |
| `LogPath` | Path to log file | Redball.log |
| `MaxLogSizeMB` | Log rotation threshold | 10 |
| `ShowBalloonOnStart` | Show tray notification on start | true |
| `BatteryAware` | Auto-pause when battery low | false |
| `BatteryThreshold` | Battery % to trigger pause | 20 |
| `ScheduleEnabled` | Enable scheduled operation | false |
| `ScheduleStartTime` | Auto-start time (HH:mm) | 09:00 |
| `ScheduleStopTime` | Auto-stop time (HH:mm) | 18:00 |
| `ScheduleDays` | Days to apply schedule | Weekdays |
| `PresentationModeDetection` | Auto-activate for presentations | false |

## Usage 📖

### Tray Icon Menu

Right-click the red ball icon in your system tray:

- **|| Pause Keep Awake** - Pause the keep-awake functionality
- **> Resume Keep Awake** - Resume (shown when paused)
- **Prevent Display Sleep** - Toggle display sleep prevention
- **Use F15 Heartbeat** - Toggle invisible keypresses
- **Stay Awake For** → - Choose duration (15/30/60/120 min)
- **Stay Awake Until Paused** - Run indefinitely
- **Exit** - Close Redball gracefully

### Keyboard Shortcuts

All menu items have keyboard shortcuts:

| Shortcut | Action |
| -------- | ------ |
| **Space** | Toggle pause/resume |
| **D** | Toggle display sleep prevention |
| **H** | Toggle F15 heartbeat |
| **I** | Stay awake indefinitely |
| **B** | Toggle battery-aware mode |
| **S** | Toggle startup with Windows |
| **N** | Toggle network-aware mode |
| **L** | Toggle idle detection |
| **X** | Exit application |

- **Double-click icon** - Toggle pause/resume

### Command Line Interface

Redball supports several CLI parameters for automation:

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
```

| Parameter | Description |
| --------- | ----------- |
| `-Install` | Add Redball to Windows startup |
| `-Uninstall` | Remove from Windows startup |
| `-Duration` | Run for N minutes (1-720) |
| `-ExitOnComplete` | Exit after timed duration completes |
| `-Minimized` | Start minimized to tray |
| `-Status` | Output JSON status and exit |
| `-ConfigPath` | Specify custom config file path |

## API Reference 📚

### Functions

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

Write to the structured log.

```powershell
Write-RedballLog -Level 'INFO' -Message 'Started successfully'
Write-RedballLog -Level 'ERROR' -Message 'Something went wrong'
```

### State Object

Access the current state via `$script:state`:

| Property | Type | Description |
| -------- | ---- | ----------- |
| `Active` | bool | Currently keeping system awake |
| `PreventDisplaySleep` | bool | Display sleep prevention enabled |
| `UseHeartbeatKeypress` | bool | F15 keypresses enabled |
| `HeartbeatSeconds` | int | Interval between heartbeats |
| `Until` | DateTime? | Timer expiration (null if indefinite) |
| `IsShuttingDown` | bool | Shutdown in progress |

## Testing 🧪

Run the Pester test suite:

```powershell
# Install Pester if needed
Install-Module Pester -Force -SkipPublisherCheck

# Run all tests
Invoke-Pester -Path "Redball.Tests.ps1"

# Run with detailed output
Invoke-Pester -Path "Redball.Tests.ps1" -Output Detailed

# Run specific test block
Invoke-Pester -Path "Redball.Tests.ps1" -TestName "*Icon*"
```

## Architecture 🏗️

```text
Redball/
├── Redball.ps1           # Main script
├── Redball.json          # Configuration file
├── Redball.Tests.ps1     # Pester test suite
├── CHANGELOG.md          # Version history
└── README.md             # This file
```

### Component Flow

1. **Initialization** - Load config, create tray icon, set up timers
2. **Heartbeat Timer** - Fires every 59 seconds to send F15 keypress
3. **Duration Timer** - Fires every second to check timer expiration
4. **UI Updates** - Refresh icon and tooltip based on state changes
5. **Shutdown** - Dispose resources, reset power state, exit cleanly

## Troubleshooting 🔧

### Issue: "Cannot bind argument to parameter 'Path' because it is an empty string"

**Solution**: This happens when running from ISE/VS Code without saving. Either:

- Save the file first, then run
- Specify the config path: `.
edball.ps1 -ConfigPath ".
edball.json"`

### Issue: Tray icon not appearing

**Solution**:

- Check Windows notification area settings
- Click "Show hidden icons" in the system tray
- Restart Windows Explorer if needed: `Stop-Process -Name explorer -Force`

### Issue: System still sleeps

**Solution**:

- Verify you're running as Administrator
- Check Windows power plan settings
- Ensure no group policy is overriding `SetThreadExecutionState`
- Try enabling "Prevent Display Sleep" as well

### Issue: Log file not created

**Solution**:

- Check write permissions in the script directory
- Verify `LogPath` in config is valid
- Check for disk space

## Contributing 🤝

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Run tests (`Invoke-Pester`)
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

### Development Setup

```powershell
# repository
git https://github.com/username/redball.git
cd redball

# Run in development mode
.\Redball.ps1 -ConfigPath ".\dev-config.json"

# Run tests
Invoke-Pester -Path ".\Redball.Tests.ps1" -Output Detailed
```

## Roadmap 🗺️

- [x] Keyboard shortcuts (tray menu access keys)
- [x] Multiple language support (i18n)
- [ ] GUI configuration editor
- [x] Installer (MSI/EXE)
- [x] Auto-start with Windows
- [x] Network keep-alive option
- [ ] Dark mode support
- [x] PowerShell Core (7.x) compatibility
- [x] Battery-aware mode
- [x] Idle detection
- [x] Scheduled operation
- [x] Presentation mode detection
- [x] Session restore
- [x] Toast notifications

## License 📄

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments 🙏

- Icon design using System.Drawing GDI+
- PowerShell community for best practices

## Support 💬

- 🐛 [Report bugs](../../issues)
- 💡 [Request features](../../issues)
- ❓ [Ask questions](../../discussions)
