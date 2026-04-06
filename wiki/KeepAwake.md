# Keep-Awake Guide

The keep-awake feature is a **secondary utility** in Redball that prevents Windows from going to sleep. While TypeThing is the primary focus, the keep-awake functionality provides intelligent system monitoring for users who need their computer to stay awake during specific activities.

## Overview

Keep-awake uses the Windows `SetThreadExecutionState` API to tell the OS that the system is in use. It can also send invisible F13–F16 keypresses (heartbeat) to prevent idle detection in applications.

**Default State:** Paused (disabled on first run)

## Quick Start

### Enable Keep-Awake

1. Right-click the Redball tray icon
2. Select **Resume** (or press Space in the menu)
3. The icon turns **bright red** when active

### Disable Keep-Awake

1. Right-click the tray icon
2. Select **Pause** (or press Space in the menu)
3. The icon turns **dark red/gray** when paused

## Modes

### Indefinite Mode

Stay awake until manually paused:

- Tray Menu → **Indefinite** (or press I)
- No timer — runs until you pause it

### Timed Mode

Stay awake for a specific duration:

- Set duration in Settings: **Default Duration** (1–720 minutes)
- Tray Menu shows countdown timer
- Auto-pauses when timer expires

## Core Settings

| Setting                | Default | Description                       |
| ---------------------- | ------- | --------------------------------- |
| `HeartbeatSeconds`     | 59      | Interval for keep-awake heartbeat |
| `PreventDisplaySleep`  | true    | Keep display awake while active   |
| `UseHeartbeatKeypress` | true    | Send invisible F15 keypress       |
| `HeartbeatInputMode`   | F15     | Key to send (F13, F14, F15, F16)  |
| `DefaultDuration`      | 60      | Default timer duration (minutes)  |

## Smart Monitoring Features

All smart features are **disabled by default**. Enable them in Settings as needed.

### Battery-Aware Mode

**Setting:** `BatteryAware` (default: `false`)

Automatically pauses when battery drops below threshold.

| Behavior    | Trigger                                          |
| ----------- | ------------------------------------------------ |
| Auto-pause  | Battery below `BatteryThreshold`% (default: 20%) |
| Auto-resume | Charger connected or battery above threshold     |

**Note:** No effect on desktops without batteries.

### Network-Aware Mode

**Setting:** `NetworkAware` (default: `false`)

Automatically pauses when network disconnects.

| Behavior    | Trigger                       |
| ----------- | ----------------------------- |
| Auto-pause  | No network adapter connected  |
| Auto-resume | Network adapter comes back up |

### Idle Detection

**Setting:** `IdleDetection` (default: `false`)

Automatically pauses after user inactivity.

| Behavior    | Trigger                                            |
| ----------- | -------------------------------------------------- |
| Auto-pause  | No input for `IdleThreshold` minutes (default: 30) |
| Auto-resume | Any user input detected                            |

Uses `GetLastInputInfo` API. Checked every 1 second.

### Scheduled Operation

**Settings:** `ScheduleEnabled`, `ScheduleStartTime`, `ScheduleStopTime`, `ScheduleDays`

Automatically start/stop on a daily schedule.

**Example Configuration:**

```json
{
    "ScheduleEnabled": true,
    "ScheduleStartTime": "09:00",
    "ScheduleStopTime": "18:00",
    "ScheduleDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
}
```

### Presentation Mode Detection

**Setting:** `PresentationModeDetection` (default: `false`)

Automatically activates when a presentation is detected.

**Detection Sources:**

- PowerPoint running (`POWERPNT.exe`)
- Microsoft Teams screen sharing (window title contains "Sharing", "Presenting")
- Windows Presentation Mode registry key

**Behavior:**

- Auto-activates when presentation detected
- Does NOT auto-stop when presentation ends (user must pause manually)

### Thermal Protection

**Setting:** `ThermalProtectionEnabled` (default: `false`)

Automatically pauses when CPU temperature is high.

| Behavior    | Trigger                                               |
| ----------- | ----------------------------------------------------- |
| Auto-pause  | CPU temp exceeds `ThermalThreshold`°C (default: 85°C) |
| Auto-resume | Temperature drops below threshold                     |

### Process Watcher

**Setting:** `ProcessWatcherEnabled` (default: `false`)

Automatically activates when a specific process is running.

**Configuration:**

- `ProcessWatcherTarget`: Process name (e.g., `code.exe`, `devenv.exe`)

**Behavior:**

- Auto-activates when target process detected
- Auto-pauses when target process exits

### VPN Auto Keep-Awake

**Setting:** `VpnAutoKeepAwake` (default: `false`)

Automatically activates when a VPN connection is detected.

### Session Lock Detection

**Setting:** `PauseOnScreenLock` (default: `false`)

Automatically pauses when the Windows session is locked.

## Advanced Features

### App-Specific Rules

**Settings:** `AppRulesEnabled`, `KeepAwakeApps`, `PauseApps`

Define applications that trigger keep-awake or pause.

- **KeepAwakeApps:** One process name per line — activates when any are in foreground
- **PauseApps:** One process name per line — pauses when any are in foreground

### Power Plan Auto-Switch

**Setting:** `PowerPlanAutoSwitch` (default: `false`)

Automatically switches Windows power plan when keep-awake activates.

- Switches to High Performance when active
- Restores previous plan when paused

### WiFi-Based Profiles

**Settings:** `WifiProfileSwitchEnabled`, `WifiProfileMappings`

Switch configuration profiles based on connected WiFi network.

**Format:** `WiFiName=ProfileName` (one per line)

### Calendar Integration

**Setting:** `CalendarIntegrationEnabled`

Automatically activates during meetings.

- Reads from `%LocalAppData%\Redball\calendar.json`
- Activates at meeting start
- Pauses at meeting end

### Scheduled Restart Reminder

**Settings:** `RestartReminderEnabled`, `RestartReminderDays`, `AutoRestartEnabled`

Reminds user to restart after continuous uptime.

- Default: 7 days
- Optional auto-restart

## Tray Icon States

| Color         | State                              |
| ------------- | ---------------------------------- |
| Bright Red    | Active — keeping system awake      |
| Orange/Red    | Timed mode — countdown in progress |
| Dark Red/Gray | Paused / idle state                |

## Check Intervals

Smart features are checked at different intervals:

| Feature           | Check Interval |
| ----------------- | -------------- |
| Idle Detection    | 1 second       |
| Battery-Aware     | 10 seconds     |
| Network-Aware     | 10 seconds     |
| Presentation Mode | 10 seconds     |
| Schedule          | 30 seconds     |
| Timer Expiration  | 1 second       |

## Configuration Example

Full keep-awake configuration in `Redball.json`:

```json
{
    "HeartbeatSeconds": 59,
    "PreventDisplaySleep": true,
    "UseHeartbeatKeypress": true,
    "HeartbeatInputMode": "F15",
    "DefaultDuration": 60,
    "BatteryAware": false,
    "BatteryThreshold": 20,
    "NetworkAware": false,
    "IdleDetection": false,
    "IdleThreshold": 30,
    "ScheduleEnabled": false,
    "ScheduleStartTime": "09:00",
    "ScheduleStopTime": "18:00",
    "ScheduleDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
    "PresentationModeDetection": false,
    "ProcessWatcherEnabled": false,
    "ProcessWatcherTarget": "",
    "PauseOnScreenLock": false,
    "VpnAutoKeepAwake": false,
    "ThermalProtectionEnabled": false,
    "ThermalThreshold": 85,
    "AppRulesEnabled": false,
    "KeepAwakeApps": "",
    "PauseApps": "",
    "PowerPlanAutoSwitch": false,
    "WifiProfileSwitchEnabled": false,
    "WifiProfileMappings": "",
    "CalendarIntegrationEnabled": false,
    "RestartReminderEnabled": false,
    "RestartReminderDays": 7,
    "AutoRestartEnabled": false
}
```

## Troubleshooting

### System still sleeps

- Check Windows power plan settings (some plans override API calls)
- Ensure no group policy is overriding `SetThreadExecutionState`
- Try enabling **Prevent Display Sleep** in the tray menu
- Try enabling **Verbose Logging** in Settings to diagnose the issue

### Smart features not working

- Ensure the feature is enabled in Settings
- Check that the relevant service is running (see Diagnostics)
- Verify configuration values are valid
- Check logs for errors

### Heartbeat key not working

- Try different heartbeat keys (F13, F14, F15, F16)
- Some applications may block all synthetic input
- Check if target application has anti-cheat or security software

## See Also

- **[TypeThing](TypeThing)** — Primary clipboard typing feature
- **[Settings GUI](Settings-GUI)** — Configuration interface
- **[Monitoring & Smart Features](Monitoring-and-Smart-Features)** — Technical details
