# Monitoring & Smart Features

Redball includes several intelligent monitoring features that automatically adjust the keep-awake state based on system conditions.

## Battery-Aware Mode

**Setting:** `BatteryAware` (default: `false`)

When enabled, Redball monitors battery status and automatically pauses when the charge drops below a configurable threshold.

### Battery Behaviour

1. **Auto-pause:** When on battery power and charge drops below `BatteryThreshold` (default: 20%), Redball pauses keep-awake and shows a toast notification
2. **Auto-resume:** When the charger is reconnected or battery charges above the threshold, Redball resumes automatically
3. **No-battery systems:** Desktops without batteries are unaffected — `Get-BatteryStatus` returns `HasBattery = $false`

### Battery Functions

| Function | Description |
| -------- | ----------- |
| `Get-BatteryStatus` | Queries WMI (`Win32_Battery` + `BatteryStatus`) with 30-second cache |
| `Test-BatteryThreshold` | Returns `$false` if battery is below threshold while on battery power |
| `Update-BatteryAwareState` | Called every second by the duration timer; handles auto-pause/resume |

### Battery State Properties

| Property | Description |
| -------- | ----------- |
| `BatteryAware` | Whether battery monitoring is enabled |
| `BatteryThreshold` | Percentage threshold for auto-pause |
| `OnBattery` | Whether the system is currently on battery power |
| `AutoPausedBattery` | Whether Redball was auto-paused due to low battery |
| `ActiveBeforeBattery` | Whether Redball was active before the battery pause |

### Battery Performance

Battery status is cached for 30 seconds (`$script:lastBatteryCheck` / `$script:lastBatteryResult`) to avoid expensive WMI queries on every timer tick.

---

## Network-Aware Mode

**Setting:** `NetworkAware` (default: `false`)

When enabled, Redball monitors network connectivity and automatically pauses when the network disconnects.

### Network Behaviour

1. **Auto-pause:** When no hardware network adapter has `Status = 'Up'`, Redball pauses
2. **Auto-resume:** When a network adapter comes back up, Redball resumes
3. **Error handling:** On detection errors, assumes connected (safe default)

### Network Functions

| Function | Description |
| -------- | ----------- |
| `Get-NetworkStatus` | Uses `Get-NetAdapter` to find an active hardware interface |
| `Update-NetworkAwareState` | Called every second; handles auto-pause/resume logic |

### Network State Properties

| Property | Description |
| -------- | ----------- |
| `NetworkAware` | Whether network monitoring is enabled |
| `AutoPausedNetwork` | Whether Redball was auto-paused due to disconnect |
| `ActiveBeforeNetwork` | Whether Redball was active before the network pause |

---

## Idle Detection

**Setting:** `IdleDetection` (default: `false`)

When enabled, Redball monitors user input and automatically pauses after 30 minutes of inactivity.

### Idle Behaviour

1. **Auto-pause:** After 30 minutes of no mouse or keyboard input, Redball pauses
2. **Auto-resume:** When any user input is detected (idle < 1 minute), Redball resumes
3. **F15 awareness:** The F15 heartbeat only fires when the system has been idle for at least 1 minute, preventing it from interfering with active work

### Idle Functions

| Function | Description |
| -------- | ----------- |
| `Get-IdleTimeMinutes` | Returns idle time in minutes via `user32.dll!GetLastInputInfo` |
| `Update-IdleAwareState` | Called every second; handles auto-pause/resume based on idle threshold |

### Idle State Properties

| Property | Description |
| -------- | ----------- |
| `IdleDetection` | Whether idle monitoring is enabled |
| `IdleThresholdMinutes` | Idle time threshold (hardcoded: 30 minutes) |
| `AutoPausedIdle` | Whether Redball was auto-paused due to idle |
| `ActiveBeforeIdle` | Whether Redball was active before the idle pause |

### Idle Interop

Uses a C# helper class `IdleHelper` compiled at runtime that wraps `user32.dll!GetLastInputInfo` and calculates idle time from `Environment.TickCount`.

---

## Scheduled Operation

**Settings:** `ScheduleEnabled`, `ScheduleStartTime`, `ScheduleStopTime`, `ScheduleDays`

When enabled, Redball automatically starts and stops keep-awake on a daily schedule.

### Schedule Behaviour

1. **Auto-start:** At `ScheduleStartTime` on configured days, Redball activates
2. **Auto-stop:** At `ScheduleStopTime`, Redball pauses (unless manually overridden via `ManualOverride`)
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

### Schedule Functions

| Function | Description |
| -------- | ----------- |
| `Test-ScheduleActive` | Returns `$true` if current time is within the scheduled window |
| `Update-ScheduleState` | Called every second; handles auto-start/stop |

### Schedule State Properties

| Property | Description |
| -------- | ----------- |
| `AutoPausedSchedule` | Whether Redball was auto-paused by the schedule |
| `ManualOverride` | If the user manually toggled state, don't auto-stop |

---

## Presentation Mode Detection

**Setting:** `PresentationModeDetection` (default: `false`)

When enabled, Redball automatically activates when a presentation is detected.

### Presentation Detection Sources

1. **PowerPoint:** Checks if `POWERPNT.exe` is running
2. **Microsoft Teams:** Checks if the Teams window title contains "Sharing", "Presenting", or "Screen sharing"
3. **Windows Presentation Mode:** Reads `HKCU:\Software\Microsoft\MobilePC\AdaptableSettings\PresentationMode`

### Presentation Behaviour

1. **Auto-activate:** When a presentation is detected and Redball is paused, it activates
2. **No auto-stop:** When the presentation ends, Redball does not auto-pause (the user may want to keep it active)

### Presentation Functions

| Function | Description |
| -------- | ----------- |
| `Test-PresentationMode` | Returns `@{ IsPresenting = $true/$false; Source = '...' }` |
| `Update-PresentationModeState` | Called every second; handles auto-activation |

---

## Priority & Interaction

When multiple smart features are enabled simultaneously, they operate independently. The duration timer checks them in this order every second:

1. Battery-Aware
2. Network-Aware
3. Idle Detection
4. Schedule
5. Presentation Mode
6. Timer expiration

Each feature tracks its own `AutoPaused*` and `ActiveBefore*` state, so resuming from one condition doesn't conflict with another.
