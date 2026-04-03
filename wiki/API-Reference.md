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

---

### ProcessWatcherService

Auto-activate when a target process is running.

**Methods:**

| Method | Returns | Description |
| --- | --- | --- |
| `CheckAndUpdate(KeepAwakeService service)` | void | Check for target process and update state |

---

### TemperatureMonitorService

CPU thermal protection monitoring.

**Methods:**

| Method | Returns | Description |
| --- | --- | --- |
| `GetStatusText()` | string | Get current temperature status text |

---

### AnalyticsService

Local analytics and feature tracking.

**Methods:**

| Method | Parameters | Returns | Description |
| --- | --- | --- | --- |
| `TrackFeature(string feature)` | `feature` | void | Record a feature usage event |
| `GetSummary()` | - | AnalyticsSummary | Get analytics summary |
| `ExportToCsv()` | - | string | Export analytics as CSV |
| `Export()` | - | string | Export analytics as JSON |

---

### HealthCheckService

Application self-monitoring and diagnostics.

---

### WebApiService

Optional local REST API for remote control.

**Configuration:** `WebApiEnabled` (default: false), `WebApiPort` (default: 48080)

---

### PluginService

Plugin loading and management via `IRedballPlugin` interface.

---

---

### ServiceInputProvider

Provides service-based keyboard input injection for RDP and elevated process compatibility.

**Properties:**

| Property | Type | Description |
| --- | --- | --- |
| `IsServiceInstalled` | bool | Whether the Redball Input Service is installed |
| `IsReady` | bool | Whether service is ready to send keystrokes |
| `LastErrorSummary` | string | Last initialization or runtime error |

**Methods:**

| Method | Parameters | Description |
| --- | --- | --- |
| `RefreshServiceInstalledState()` | - | Check if service is installed and running |
| `GetDetailedServiceState()` | - | Get detailed service status information |
| `InstallService()` | - | Install the Redball Input Service (requires admin) |
| `UninstallService()` | - | Uninstall the service |

---

### CalendarIntegrationService

Auto-activates during meetings based on local JSON calendar data.

**Methods:**

| Method | Returns | Description |
| --- | --- | --- |
| `LoadEvents()` | void | Reload events from `calendar.json` |
| `GetStatusText()` | string | Get current meeting or next event info |

---

### CloudAnalyticsService

Opt-in remote analytics collection and cohort analysis.

---

### DataExportService

GDPR-style data bundling.

**Methods:**

| Method | Parameters | Description |
| --- | --- | --- |
| `ExportAll(string path)` | `path` | Bundle all data into a ZIP archive |

---

### TemplateService

Manages named text templates for TypeThing.

---

### TextToSpeechService

Text-to-speech for TypeThing feature.

---

### SecurityService

Provides application integrity checks, Authenticode verification, and SBOM generation.

**Methods:**

| Method | Parameters | Description |
| --- | --- | --- |
| `VerifyAuthenticodeSignature(string path)` | `path` | Verify file digital signature |
| `GenerateSBOM()` | - | Generate SPDX 2.3 SBOM |

---

## Configuration Class

### RedballConfig

The full configuration model is defined in `Models/RedballConfig.cs`. See the [Configuration](Configuration) wiki page for a complete reference of all settings.

Key sections:

- **General & UI** — Theme, locale, notifications, logging
- **Smart Features** — Battery, network, idle, schedule, presentation, thermal, process watcher, VPN, session lock, app rules, power plan, WiFi profiles, restart reminders
- **TypeThing** — Clipboard typer settings including TTS
- **Updates** — Auto-update check, channel, signature verification
- **Advanced** — Telemetry, performance metrics, Web API

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
