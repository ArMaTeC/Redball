# Configuration

Settings are stored in `Redball.json` in the same directory as the script. A default file is created on first run if one doesn't exist. You can also change all settings from the **Settings** dialog in the tray menu.

## Configuration File Location

- **Script mode:** Same directory as `Redball.ps1`
- **MSI installation:** `%LocalAppData%\Redball\Redball.json`
- **Custom:** Use `-ConfigPath "C:\path\to\Redball.json"`

## Full Configuration Reference

### General Settings

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `HeartbeatSeconds` | int | Interval between keep-awake refreshes and F15 keypresses | `59` |
| `PreventDisplaySleep` | bool | Keep the display on in addition to preventing system sleep | `true` |
| `UseHeartbeatKeypress` | bool | Send invisible F15 keypresses to prevent app-level idle detection | `true` |
| `DefaultDuration` | int | Default timer duration in minutes | `60` |
| `LogPath` | string | Path to log file | `Redball.log` |
| `MaxLogSizeMB` | int | Log rotation threshold in MB | `10` |
| `ShowBalloonOnStart` | bool | Show tray notification when Redball starts | `true` |
| `Locale` | string | Display language (`en`, `es`, `fr`, `de`) | Auto-detected |
| `MinimizeOnStart` | bool | Start minimized to system tray | `false` |
| `AutoExitOnComplete` | bool | Exit automatically when a timed session finishes | `false` |

### Power & Monitoring Settings

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `BatteryAware` | bool | Auto-pause when battery is low | `false` |
| `BatteryThreshold` | int | Battery % below which to auto-pause | `20` |
| `NetworkAware` | bool | Auto-pause when network disconnects | `false` |
| `IdleDetection` | bool | Auto-pause after 30 min of user inactivity | `false` |
| `PresentationModeDetection` | bool | Auto-activate for PowerPoint/Teams presentations | `false` |

### Schedule Settings

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `ScheduleEnabled` | bool | Enable daily scheduled activation | `false` |
| `ScheduleStartTime` | string | Time to auto-start (HH:mm) | `09:00` |
| `ScheduleStopTime` | string | Time to auto-stop (HH:mm) | `18:00` |
| `ScheduleDays` | string[] | Days of the week the schedule applies | Weekdays |

### Advanced Settings

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `ProcessIsolation` | bool | Run keep-awake API in a separate runspace | `false` |
| `EnablePerformanceMetrics` | bool | Track CPU, memory, and handle metrics | `false` |
| `EnableTelemetry` | bool | Opt-in anonymous usage telemetry (logged locally) | `false` |
| `UpdateRepoOwner` | string | GitHub owner for update checks | `ArMaTeC` |
| `UpdateRepoName` | string | GitHub repo for update checks | `Redball` |
| `UpdateChannel` | string | Release channel (`stable` or `beta`) | `stable` |
| `VerifyUpdateSignature` | bool | Require valid digital signature on updates | `false` |

### TypeThing Settings

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `TypeThingEnabled` | bool | Enable the clipboard typing feature | `true` |
| `TypeThingMinDelayMs` | int | Minimum delay between keystrokes (ms) | `30` |
| `TypeThingMaxDelayMs` | int | Maximum delay between keystrokes (ms) | `120` |
| `TypeThingStartDelaySec` | int | Countdown seconds before typing begins | `3` |
| `TypeThingStartHotkey` | string | Global hotkey to start typing | `Ctrl+Shift+V` |
| `TypeThingStopHotkey` | string | Global hotkey to stop typing | `Ctrl+Shift+X` |
| `TypeThingTheme` | string | Settings dialog theme (`light`, `dark`, `hacker`) | `dark` |
| `TypeThingAddRandomPauses` | bool | Add occasional longer pauses for realism | `true` |
| `TypeThingRandomPauseChance` | int | Chance (%) of a random pause per character | `5` |
| `TypeThingRandomPauseMaxMs` | int | Maximum random pause duration (ms) | `500` |
| `TypeThingTypeNewlines` | bool | Press Enter when a newline is encountered | `true` |
| `TypeThingNotifications` | bool | Show tray notifications for typing events | `true` |

## Example Configuration

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
    "UpdateRepoOwner": "ArMaTeC",
    "UpdateRepoName": "Redball",
    "UpdateChannel": "stable",
    "VerifyUpdateSignature": false,
    "TypeThingEnabled": true,
    "TypeThingMinDelayMs": 30,
    "TypeThingMaxDelayMs": 120,
    "TypeThingStartDelaySec": 3,
    "TypeThingStartHotkey": "Ctrl+Shift+V",
    "TypeThingStopHotkey": "Ctrl+Shift+X",
    "TypeThingTheme": "dark",
    "TypeThingAddRandomPauses": true,
    "TypeThingRandomPauseChance": 5,
    "TypeThingRandomPauseMaxMs": 500,
    "TypeThingTypeNewlines": true,
    "TypeThingNotifications": true
}
```

## Settings Backup & Restore

Export and import all settings using PowerShell:

```powershell
# Export settings to a backup file
Export-RedballSettings -Path '.\Redball.backup.json'

# Import settings from a backup file
Import-RedballSettings -Path '.\Redball.backup.json'
```

The backup includes both `$script:config` and relevant `$script:state` values, along with metadata (export timestamp, version).

## Installer Registry Defaults

When installed via the MSI, the installer can write default values to the registry at:

```text
HKCU:\Software\Redball\InstallerDefaults
```

These are read by `Import-RedballInstallerDefaults` on first run (when no saved session state exists) and include:

- `BatteryAware` (DWORD 1 = enabled)
- `NetworkAware` (DWORD 1 = enabled)
- `IdleDetection` (DWORD 1 = enabled)
- `Minimized` (DWORD 1 = start minimized)
- `ExitOnComplete` (DWORD 1 = auto-exit on timer finish)
