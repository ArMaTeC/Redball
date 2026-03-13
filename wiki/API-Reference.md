# API Reference

Complete reference for Redball v3.0 C# services.

> **Note:** Redball v3.0 is a pure C# WPF application. The legacy PowerShell API has been archived in `legacy/Redball-v2.1.19-legacy.zip`.

## Service Architecture

All services are implemented as singletons and registered in `App.xaml.cs`.

### Accessing Services

```csharp
// Get service instance
var keepAwake = KeepAwakeService.Instance;
var config = ConfigService.Instance;
```

## Core Services

### KeepAwakeService

The main keep-awake engine. Manages `SetThreadExecutionState`, F15 heartbeat, and monitoring.

**Properties:**

| Property | Type | Description |
| --- | --- | --- |
| `Instance` | KeepAwakeService | Singleton instance |
| `IsActive` | bool | Current keep-awake state |
| `Until` | DateTime? | Timer expiration (null if indefinite) |
| `PreventDisplaySleep` | bool | Keep display awake |
| `UseHeartbeat` | bool | Send F15 keypresses |
| `AutoPausedBattery` | bool | Auto-paused due to low battery |
| `AutoPausedNetwork` | bool | Auto-paused due to disconnect |
| `AutoPausedIdle` | bool | Auto-paused due to idle |
| `AutoPausedSchedule` | bool | Auto-paused by schedule |

**Methods:**

| Method | Parameters | Description |
| --- | --- | --- |
| `SetActive(bool active)` | `active` | Enable/disable keep-awake |
| `Toggle()` | - | Toggle active state |
| `StartTimedAwake(TimeSpan duration)` | `duration` | Start timed session |
| `ReloadConfig()` | - | Reload settings from ConfigService |

**Events:**

| Event | Description |
| --- | --- |
| `ActiveStateChanged` | Fired when active state changes |
| `TimedAwakeExpired` | Fired when timed session expires |
| `HeartbeatTick` | Fired on each heartbeat interval |

---

### ConfigService

JSON configuration management with validation and export/import.

**Properties:**

| Property | Type | Description |
| --- | --- | --- |
| `Instance` | ConfigService | Singleton instance |
| `Config` | RedballConfig | Current configuration |

**Methods:**

| Method | Parameters | Description |
| --- | --- | --- |
| `Load(string? path)` | `path` | Load config from file |
| `Save()` | - | Save current config |
| `Export(string path)` | `path` | Export to backup file |
| `Import(string path)` | `path` | Import from backup file |
| `Validate()` | - | Validate config values |

---

### BatteryMonitorService

WMI-based battery monitoring with caching.

**Methods:**

| Method | Returns | Description |
| --- | --- | --- |
| `CheckAndUpdate(KeepAwakeService service)` | void | Check battery and update state |
| `GetBatteryStatus()` | BatteryStatus | Get current battery info |

---

### NetworkMonitorService

Network connectivity monitoring.

**Methods:**

| Method | Returns | Description |
| --- | --- | --- |
| `CheckAndUpdate(KeepAwakeService service)` | void | Check network and update state |
| `IsNetworkAvailable()` | bool | Check if any network is connected |

---

### IdleDetectionService

User idle detection via `GetLastInputInfo`.

**Methods:**

| Method | Returns | Description |
| --- | --- | --- |
| `CheckAndUpdate(KeepAwakeService service)` | void | Check idle time and update state |
| `GetIdleTime()` | TimeSpan | Get current idle duration |

---

### SessionStateService

Save/restore session state across restarts.

**Methods:**

| Method | Parameters | Description |
| --- | --- | --- |
| `Save()` | - | Save current session state |
| `Restore(KeepAwakeService service)` | `service` | Restore previous session |

---

### NotificationService

Tray balloon notifications with mode filtering.

**Methods:**

| Method | Parameters | Description |
| --- | --- | --- |
| `ShowNotification(string title, string message)` | `title`, `message` | Show notification |
| `ShowNotification(string title, string message, NotificationMode mode)` | `title`, `message`, `mode` | Show with mode filter |

---

### LocalizationService

Internationalization with built-in and external locales.

**Methods:**

| Method | Parameters | Returns | Description |
| --- | --- | --- | --- |
| `GetString(string key)` | `key` | string | Get localized string |
| `SetLocale(string locale)` | `locale` | void | Change locale |
| `GetAvailableLocales()` | - | string[] | List available locales |

---

### StartupService

Windows startup registration via Registry Run key.

**Methods:**

| Method | Parameters | Returns | Description |
| --- | --- | --- | --- |
| `IsStartupEnabled()` | - | bool | Check if startup is enabled |
| `SetStartup(bool enabled)` | `enabled` | void | Enable/disable startup |

---

### SingletonService

Named mutex for single instance enforcement.

**Methods:**

| Method | Returns | Description |
| --- | --- | --- |
| `TryAcquire()` | bool | Try to acquire mutex (false if another instance running) |
| `Dispose()` | - | Release mutex on exit |

---

### CrashRecoveryService

Crash detection and safe recovery.

**Methods:**

| Method | Returns | Description |
| --- | --- | --- |
| `CheckAndRecover()` | bool | Check for previous crash and recover |
| `SetCrashFlag()` | void | Set crash flag for this session |
| `ClearCrashFlag()` | void | Clear flag on clean exit |

## Configuration Classes

### RedballConfig

```csharp
public class RedballConfig
{
    public int HeartbeatSeconds { get; set; } = 59;
    public bool PreventDisplaySleep { get; set; } = true;
    public bool UseHeartbeatKeypress { get; set; } = true;
    public int DefaultDuration { get; set; } = 60;
    public string LogPath { get; set; } = "Redball.log";
    public int MaxLogSizeMB { get; set; } = 10;
    public bool ShowBalloonOnStart { get; set; } = true;
    public string Locale { get; set; } = "en";
    public bool MinimizeOnStart { get; set; } = false;
    public bool BatteryAware { get; set; } = false;
    public int BatteryThreshold { get; set; } = 20;
    public bool NetworkAware { get; set; } = false;
    public bool IdleDetection { get; set; } = false;
    public int IdleThresholdMinutes { get; set; } = 30;
    public bool AutoExitOnComplete { get; set; } = false;
    public bool ScheduleEnabled { get; set; } = false;
    public string ScheduleStartTime { get; set; } = "09:00";
    public string ScheduleStopTime { get; set; } = "18:00";
    public List<string> ScheduleDays { get; set; } = new();
    public bool PresentationModeDetection { get; set; } = false;
    public bool EnableTelemetry { get; set; } = false;
    // ... TypeThing settings
}
```

## Events

Services communicate via events:

```csharp
// Subscribe to events
KeepAwakeService.Instance.ActiveStateChanged += (s, e) =>
{
    // Handle state change
};
```

## Win32 Interop

Native methods are in `Interop/NativeMethods.cs`:

| Method | DLL | Purpose |
| --- | --- | --- |
| `SetThreadExecutionState` | kernel32 | Prevent sleep/display off |
| `SendInput` | user32 | F15 heartbeat, TypeThing input |
| `GetLastInputInfo` | user32 | Idle detection |
| `RegisterHotKey` | user32 | Global hotkeys |

---

## TypeThing

See [TypeThing](TypeThing.md) for full documentation of the clipboard typing feature.
