# Monitoring & Smart Features

Redball includes many intelligent monitoring features that automatically adjust the keep-awake state based on system conditions. All features are implemented as C# singleton services coordinated by `KeepAwakeService`.

## Battery-Aware Mode

**Setting:** `BatteryAware` (default: `false`) | **Service:** `BatteryMonitorService`

When enabled, Redball monitors battery status and automatically pauses when the charge drops below a configurable threshold.

### Battery Behaviour

1. **Auto-pause:** When on battery power and charge drops below `BatteryThreshold` (default: 20%), Redball pauses keep-awake and shows a toast notification
2. **Auto-resume:** When the charger is reconnected or battery charges above the threshold, Redball resumes automatically
3. **No-battery systems:** Desktops without batteries are unaffected

### Battery Performance

Battery status is cached for 60 seconds to avoid expensive WMI queries on every timer tick. Checked every 10 seconds by the duration timer.

---

## Network-Aware Mode

**Setting:** `NetworkAware` (default: `false`) | **Service:** `NetworkMonitorService`

When enabled, Redball monitors network connectivity and automatically pauses when the network disconnects.

### Network Behaviour

1. **Auto-pause:** When no network adapter is connected, Redball pauses
2. **Auto-resume:** When a network adapter comes back up, Redball resumes
3. **Error handling:** On detection errors, assumes connected (safe default)

Checked every 10 seconds by the duration timer.

---

## Idle Detection

**Setting:** `IdleDetection` (default: `false`) | **Service:** `IdleDetectionService`

When enabled, Redball monitors user input and automatically pauses after a configurable period of inactivity.

### Idle Behaviour

1. **Auto-pause:** After `IdleThreshold` minutes (default: 30) of no mouse or keyboard input, Redball pauses
2. **Auto-resume:** When any user input is detected, Redball resumes
3. **Heartbeat awareness:** The heartbeat keypress only fires when the system has been idle for at least 1 minute, preventing interference with active work

Uses `user32.dll!GetLastInputInfo` via P/Invoke. Checked every 1 second by the duration timer.

---

## Scheduled Operation

**Settings:** `ScheduleEnabled`, `ScheduleStartTime`, `ScheduleStopTime`, `ScheduleDays` | **Service:** `ScheduleService`

When enabled, Redball automatically starts and stops keep-awake on a daily schedule.

### Schedule Behaviour

1. **Auto-start:** At `ScheduleStartTime` on configured days, Redball activates
2. **Auto-stop:** At `ScheduleStopTime`, Redball pauses (unless manually overridden)
3. **Day filtering:** Only activates on days listed in `ScheduleDays`

### Schedule Example Configuration

```json
{
    "ScheduleEnabled": true,
    "ScheduleStartTime": "09:00",
    "ScheduleStopTime": "18:00",
    "ScheduleDays": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
}
```

Checked every 30 seconds by the duration timer.

---

## Presentation Mode Detection

**Setting:** `PresentationModeDetection` (default: `false`) | **Service:** `PresentationModeService`

When enabled, Redball automatically activates when a presentation is detected.

### Presentation Detection Sources

1. **PowerPoint:** Checks if `POWERPNT.exe` is running
2. **Microsoft Teams:** Checks if the Teams window title contains "Sharing", "Presenting", or "Screen sharing"
3. **Windows Presentation Mode:** Reads `HKCU\Software\Microsoft\MobilePC\AdaptableSettings\PresentationMode`

### Presentation Behaviour

1. **Auto-activate:** When a presentation is detected and Redball is paused, it activates
2. **No auto-stop:** When the presentation ends, Redball does not auto-pause (the user may want to keep it active)

Process scan results are cached for 10 seconds. Checked every 10 seconds by the duration timer.

---

## Thermal Protection

**Setting:** `ThermalProtectionEnabled` (default: `false`) | **Service:** `TemperatureMonitorService`

When enabled, Redball monitors CPU temperature and automatically pauses to reduce system load when temperature exceeds a threshold.

### Thermal Behaviour

1. **Auto-pause:** When CPU temperature exceeds `ThermalThreshold` (default: 85°C), Redball pauses
2. **Auto-resume:** When temperature drops below the threshold, Redball resumes
3. **Status display:** Current temperature is shown in the Diagnostics section of the main window

---

## Process Watcher

**Setting:** `ProcessWatcherEnabled` (default: `false`) | **Service:** `ProcessWatcherService`

When enabled, Redball automatically activates keep-awake when a specified process is running.

### Process Watcher Behaviour

1. **Auto-activate:** When `ProcessWatcherTarget` (e.g. `code.exe`) is detected as running, Redball activates
2. **Auto-pause:** When the target process exits, Redball pauses

---

## Session Lock Detection

**Setting:** `PauseOnScreenLock` (default: `false`) | **Service:** `SessionLockService`

When enabled, Redball automatically pauses when the Windows session is locked and resumes when unlocked.

---

## VPN Auto Keep-Awake

**Setting:** `VpnAutoKeepAwake` (default: `false`)

When enabled, Redball automatically activates keep-awake when a VPN connection is detected, useful for maintaining active connections during remote work.

---

## App-Specific Rules

**Settings:** `AppRulesEnabled`, `KeepAwakeApps`, `PauseApps` | **Service:** `ForegroundAppService`

Define lists of applications that should either trigger keep-awake or pause Redball when they are running in the foreground.

- **KeepAwakeApps:** One process name per line — Redball activates when any of these are in the foreground
- **PauseApps:** One process name per line — Redball pauses when any of these are in the foreground

---

## Power Plan Auto-Switch

**Setting:** `PowerPlanAutoSwitch` (default: `false`) | **Service:** `PowerPlanService`

When enabled, Redball automatically switches the Windows power plan to High Performance when keep-awake is active, and restores the previous plan when paused.

---

## WiFi-Based Profiles

**Settings:** `WifiProfileSwitchEnabled`, `WifiProfileMappings` | **Service:** `ProfileService`

Switch configuration profiles automatically based on the connected WiFi network. Define mappings in `WifiProfileMappings` using the format `WiFiName=ProfileName` (one per line).

---

## Scheduled Restart Reminder

**Settings:** `RestartReminderEnabled`, `RestartReminderDays`, `AutoRestartEnabled` | **Service:** `ScheduledRestartService`

Reminds the user to restart the application (or auto-restarts) after a configurable number of days of continuous uptime. Default: 7 days.

---

## Calendar Integration

**Setting:** `CalendarIntegrationEnabled` | **Service:** `CalendarIntegrationService`

Automatically activates keep-awake during meetings and deactivates during breaks.

1. **Local File:** Reads from `%LocalAppData%\Redball\calendar.json`
2. **Auto-activation:** When a meeting starts, Redball activates keep-awake
3. **Auto-deactivation:** When the meeting ends, Redball pauses
4. **Compatibility:** Users can export their calendar to JSON via a companion script

Checked every 60 seconds.

---

## User Data Export (GDPR)

**Service:** `DataExportService`

Provides a complete bundle of all user data stored by the application. Accessible via **Diagnostics** section or **Settings → Export Diagnostics**.

Includes:

- Full JSON configuration
- Local analytics and tracking data
- Session state and status history
- Current and rotated log files
- System metadata (version, OS, machine name)

The export is bundled into a single ZIP archive.

---

## Priority & Interaction

When multiple smart features are enabled simultaneously, they operate independently. The duration timer checks them at these intervals:

| Feature | Check Interval |
| ------- | -------------- |
| Idle Detection | 1 second |
| Battery-Aware | 10 seconds |
| Network-Aware | 10 seconds |
| Presentation Mode | 10 seconds |
| Schedule | 30 seconds |
| Timer expiration | 1 second |

Each feature tracks its own `AutoPaused*` state, so resuming from one condition doesn't conflict with another.
