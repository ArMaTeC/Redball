# Configuration

Settings are persisted primarily in registry `HKCU\Software\Redball\UserData` (value `ConfigPayload`) with a local file copy at `%LocalAppData%\Redball\UserData\Redball.json`. You can change settings from the main window's navigation sections or by editing/importing JSON.

## Configuration File Location

- **Primary store:** `HKCU\Software\Redball\UserData`
- **File copy:** `%LocalAppData%\Redball\UserData\Redball.json`

Config is migrated automatically from legacy locations (install dir, old LocalAppData root, roaming AppData) on first run.

## Full Configuration Reference

### General & UI Settings

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `HeartbeatSeconds` | int | Interval between keep-awake refreshes | `59` |
| `PreventDisplaySleep` | bool | Keep the display on in addition to preventing system sleep | `true` |
| `UseHeartbeatKeypress` | bool | Send invisible keypresses to prevent app-level idle detection | `true` |
| `HeartbeatInputMode` | string | Which function key to send (`F13`, `F14`, `F15`, `F16`) | `F15` |
| `DefaultDuration` | int | Default timer duration in minutes | `60` |
| `Theme` | string | UI theme (System, Dark, Light, MidnightBlue, ForestGreen, OceanBlue, SunsetOrange, RoyalPurple, SlateGrey, RoseGold, Cyberpunk, Coffee, ArcticFrost, HighContrast) | `Dark` |
| `Locale` | string | Display language (`en`, `es`, `fr`, `de`, `bl`) | `en` |
| `MinimizeOnStart` | bool | Start minimized to system tray | `false` |
| `MinimizeToTray` | bool | Minimize to tray instead of taskbar | `false` |
| `ConfirmOnExit` | bool | Show confirmation dialog when exiting | `true` |
| `ShowNotifications` | bool | Enable tray/toast notifications | `true` |
| `SoundNotifications` | bool | Play sound with notifications | `false` |
| `NotificationMode` | enum | Notification filter (`All`, `Important`, `Errors`, `Silent`) | `All` |
| `VerboseLogging` | bool | Record extra diagnostic log details | `false` |
| `MaxLogSizeMB` | int | Log rotation threshold in MB | `10` |
| `AutoExitOnComplete` | bool | Exit automatically when a timed session finishes | `false` |
| `FirstRun` | bool | Whether this is the first run (triggers onboarding) | `true` |

### Smart Features

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `BatteryAware` | bool | Auto-pause when battery is low | `false` |
| `BatteryThreshold` | int | Battery % below which to auto-pause | `20` |
| `NetworkAware` | bool | Auto-pause when network disconnects | `false` |
| `IdleDetection` | bool | Auto-pause after user inactivity | `false` |
| `IdleThreshold` | int | Minutes of inactivity before auto-pause | `30` |
| `PresentationModeDetection` | bool | Auto-activate for PowerPoint/Teams presentations | `false` |
| `PauseOnScreenLock` | bool | Auto-pause when the screen is locked | `false` |
| `VpnAutoKeepAwake` | bool | Auto-activate when VPN is connected | `false` |
| `ProcessWatcherEnabled` | bool | Auto-activate when target process is running | `false` |
| `ProcessWatcherTarget` | string | Process name to watch (e.g. `code.exe`) | `""` |
| `ThermalProtectionEnabled` | bool | Auto-pause when CPU temperature is too high | `false` |
| `ThermalThreshold` | int | CPU temperature threshold (°C) | `85` |
| `AppRulesEnabled` | bool | Enable app-specific keep-awake/pause rules | `false` |
| `KeepAwakeApps` | string | Apps that trigger keep-awake (one per line) | `""` |
| `PauseApps` | string | Apps that trigger a pause (one per line) | `""` |
| `PowerPlanAutoSwitch` | bool | Auto-switch Windows power plan | `false` |
| `WifiProfileSwitchEnabled` | bool | Switch profiles based on WiFi network | `false` |
| `WifiProfileMappings` | string | WiFi-to-profile mappings (`WiFiName=Profile` per line) | `""` |
| `RestartReminderEnabled` | bool | Remind to restart after N days | `false` |
| `RestartReminderDays` | int | Days before restart reminder | `7` |
| `AutoRestartEnabled` | bool | Auto-restart instead of just reminding | `false` |

### Schedule Settings

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `ScheduleEnabled` | bool | Enable daily scheduled activation | `false` |
| `ScheduleStartTime` | string | Time to auto-start (HH:mm) | `09:00` |
| `ScheduleStopTime` | string | Time to auto-stop (HH:mm) | `18:00` |
| `ScheduleDays` | string[] | Days of the week the schedule applies | Weekdays |

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
| `TypeThingInputMode` | string | Input method (`SendInput` or `Interception`) | `SendInput` |
| `TypeThingTtsEnabled` | bool | Enable text-to-speech while typing | `false` |

### Update Settings

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `AutoUpdateCheckEnabled` | bool | Check for updates automatically | `true` |
| `AutoUpdateCheckIntervalMinutes` | int | Minutes between automatic update checks | `120` |
| `UpdateRepoOwner` | string | GitHub owner for update checks | `ArMaTeC` |
| `UpdateRepoName` | string | GitHub repo for update checks | `Redball` |
| `UpdateChannel` | string | Release channel (`stable` or `beta`) | `stable` |
| `VerifyUpdateSignature` | bool | Require valid digital signature on updates | `false` |

### Advanced Settings

| Setting | Type | Description | Default |
| ------- | ---- | ----------- | ------- |
| `EnablePerformanceMetrics` | bool | Track CPU, memory, and handle metrics | `false` |
| `EnableTelemetry` | bool | Opt-in anonymous usage telemetry (logged locally) | `false` |
| `WebApiEnabled` | bool | Enable local REST API for remote control | `false` |
| `WebApiPort` | int | Port for the local Web API | `48080` |

## Settings Backup & Restore

Export and import settings from the main window:

1. Open the main window → **Diagnostics** section
2. Click **Export Diagnostics** to export a diagnostics report
3. Use `ConfigService.Instance.Export("backup.json")` / `Import("backup.json")` programmatically

Or use the tray menu → **Settings...** to access all settings with immediate auto-apply.

## Installer Registry Defaults

When installed via the MSI, the installer writes default values to the registry at:

```text
HKCU\Software\Redball\InstallerDefaults
```

These are read on first run (when no saved config exists) and include:

- `BatteryAware` (DWORD 1 = enabled)
- `NetworkAware` (DWORD 1 = enabled)
- `IdleDetection` (DWORD 1 = enabled)
- `Minimized` (DWORD 1 = start minimized)
- `ExitOnComplete` (DWORD 1 = auto-exit on timer finish)
