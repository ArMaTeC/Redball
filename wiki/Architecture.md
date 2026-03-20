# Architecture

> **v3.0** — Pure C# WPF architecture. All functionality runs natively in the WPF application with no PowerShell dependency.

## Project Structure

```text
Redball/
├── .github/workflows/
│   ├── ci.yml                       # CI pipeline (build, test, lint, security)
│   └── release.yml                  # Release pipeline (tag, build MSI, publish)
├── src/Redball.UI.WPF/              # WPF application (.NET 8)
│   ├── Interop/
│   │   └── NativeMethods.cs         # All Win32 P/Invoke declarations
│   ├── Models/
│   │   └── RedballConfig.cs         # Strongly-typed configuration model
│   ├── Services/                    # 40+ singleton services
│   │   ├── KeepAwakeService.cs      # Core keep-awake engine
│   │   ├── BatteryMonitorService.cs # Battery monitoring + auto-pause
│   │   ├── NetworkMonitorService.cs # Network monitoring + auto-pause
│   │   ├── IdleDetectionService.cs  # Idle detection + auto-pause
│   │   ├── ScheduleService.cs       # Scheduled activation
│   │   ├── PresentationModeService.cs # Presentation detection
│   │   ├── PomodoroService.cs       # Focus/break cycle timer
│   │   ├── ProcessWatcherService.cs # Process-based auto-activation
│   │   ├── SessionLockService.cs    # Screen lock detection
│   │   ├── TemperatureMonitorService.cs # CPU thermal protection
│   │   ├── PowerPlanService.cs      # Windows power plan switching
│   │   ├── ScheduledRestartService.cs # Uptime restart reminders
│   │   ├── AnalyticsService.cs      # Local analytics + feature tracking
│   │   ├── SessionStateService.cs   # Session save/restore
│   │   ├── SessionStatsService.cs   # Session statistics
│   │   ├── StartupService.cs        # Windows startup registration
│   │   ├── SingletonService.cs      # Named mutex singleton
│   │   ├── CrashRecoveryService.cs  # Crash flag detection
│   │   ├── NotificationService.cs   # Tray/toast notifications
│   │   ├── LocalizationService.cs   # i18n (en, es, fr, de, bl)
│   │   ├── ConfigService.cs         # JSON config load/save/export/import
│   │   ├── HotkeyService.cs         # Global hotkey registration
│   │   ├── UpdateService.cs         # GitHub release auto-updater
│   │   ├── HealthCheckService.cs    # App self-monitoring
│   │   ├── PluginService.cs         # Plugin loading + management
│   │   ├── WebApiService.cs         # Local REST API
│   │   ├── ProfileService.cs        # WiFi-based config profiles
│   │   ├── ForegroundAppService.cs  # Foreground app tracking
│   │   ├── TextToSpeechService.cs   # TTS for TypeThing
│   │   ├── SecurityService.cs       # Security + integrity checks
│   │   ├── TelemetryService.cs      # Opt-in local telemetry
│   │   └── Logger.cs                # Structured logging with rotation
│   ├── ViewModels/
│   │   └── MainViewModel.cs         # MVVM state + commands
│   ├── Views/
│   │   ├── MainWindow.xaml/.cs      # Main window (partial classes)
│   │   ├── MainWindow.Navigation.cs # Section switching
│   │   ├── MainWindow.Settings.cs   # Embedded settings
│   │   ├── MainWindow.TrayIcon.cs   # Tray icon setup/recovery
│   │   ├── MainWindow.TypeThing.cs  # TypeThing integration
│   │   ├── MainWindow.Pomodoro.cs   # Pomodoro integration
│   │   ├── MainWindow.Updates.cs    # Update management
│   │   ├── AboutWindow.xaml/.cs     # Version info + update check
│   │   ├── MiniWidgetWindow.xaml/.cs # Floating mini widget
│   │   ├── OnboardingWindow.xaml/.cs # First-run tutorial
│   │   ├── ToastNotification.xaml/.cs # Toast notification UI
│   │   └── QuickSettingsPopup.xaml/.cs # Quick settings
│   ├── Themes/                      # Dark/Light base XAML + Controls
│   ├── Converters/                  # WPF value converters
│   ├── Assets/redball.ico           # Application icon
│   ├── ThemeManager.cs              # 14-theme switching engine
│   ├── App.xaml / App.xaml.cs       # Application entry point
│   └── Redball.UI.WPF.csproj       # Project file
├── installer/                       # WiX MSI installer
├── scripts/                         # Build helper scripts
├── tests/                           # Unit test suite
├── wiki/                            # Documentation
└── locales.json                     # External locale overrides
```

## Service Architecture

All services are instantiated as singletons and coordinated by `App.xaml.cs` and `KeepAwakeService`.

```text
App.xaml.cs (entry point)
  ├── SingletonService        — Mutex check (first thing)
  ├── CrashRecoveryService    — Crash flag check
  ├── ConfigService           — Load Redball.json from UserData
  ├── ThemeManager            — Apply saved theme (14 themes)
  ├── KeepAwakeService        — Initialize + SetActive + StartMonitoring
  │     ├── HeartbeatTimer    — SetThreadExecutionState + F13–F16 (every Ns)
  │     └── DurationTimer     — 1s tick driving all monitors:
  │           ├── IdleDetectionService        (every 1s — cheap P/Invoke)
  │           ├── BatteryMonitorService       (every 10s — WMI cached 60s)
  │           ├── NetworkMonitorService       (every 10s)
  │           ├── PresentationModeService     (every 10s — process scan cached 10s)
  │           ├── ScheduleService             (every 30s)
  │           ├── ProcessWatcherService       (process scanning)
  │           ├── SessionLockService          (session events)
  │           ├── TemperatureMonitorService   (CPU temp checks)
  │           └── PomodoroService             (focus/break cycles)
  ├── AnalyticsService        — Feature tracking + session analytics
  ├── SessionStateService     — Restore previous session
  ├── HealthCheckService      — App self-monitoring
  ├── WebApiService           — Optional local REST API
  └── MainWindow              — Tray icon, hotkeys, TypeThing, Pomodoro
        └── MainViewModel     — Binds to KeepAwakeService.ActiveStateChanged
```

## Startup Sequence

```text
1. Logger.Initialize()
2. Register global exception handlers
3. SingletonService.TryAcquire() — exit if another instance running
4. CrashRecoveryService.CheckAndRecover() — detect previous crash
5. CrashRecoveryService.SetCrashFlag() — mark this session
6. ConfigService.Load() — load Redball.json
7. ConfigService.Validate() — check config ranges
8. ThemeManager.Initialize() — apply saved theme
9. KeepAwakeService.Initialize() — create timers, configure monitors
10. SessionStateService.Restore() — restore previous state (or SetActive(true))
11. KeepAwakeService.StartMonitoring() — start duration timer
12. Create MainWindow (tray-only mode)
13. MainWindow.SetupTrayIcon() + SetupGlobalHotkeys()
```

## Heartbeat Timer (every N seconds)

```text
→ Re-assert SetThreadExecutionState (ES_CONTINUOUS | ES_SYSTEM_REQUIRED [| ES_DISPLAY_REQUIRED])
→ Send F15 keypress via SendInput (if UseHeartbeat enabled)
→ Fire HeartbeatTick event
```

## Duration Timer (every 1 second)

```text
→ Check timed expiry (if Until has passed → SetActive(false))
→ IdleDetectionService.CheckAndUpdate()          [every 1s]
→ BatteryMonitorService.CheckAndUpdate()          [every 10s]
→ NetworkMonitorService.CheckAndUpdate()           [every 10s]
→ PresentationModeService.CheckAndUpdate()         [every 10s]
→ ScheduleService.CheckAndUpdate()                 [every 30s]
```

## Shutdown Sequence

```text
1. SessionStateService.Save() — write Redball.state.json
2. KeepAwakeService.Dispose() — stop timers, SetThreadExecutionState(ES_CONTINUOUS)
3. CrashRecoveryService.ClearCrashFlag() — clean exit
4. SingletonService.Dispose() — release mutex
5. Application.Shutdown()
```

## Win32 Interop (NativeMethods.cs)

All P/Invoke declarations are centralized in `Interop/NativeMethods.cs`:

| API | DLL | Purpose |
| --- | --- | ------- |
| `SetThreadExecutionState` | kernel32.dll | Prevent system/display sleep |
| `SendInput` | user32.dll | F15 heartbeat + TypeThing character input |
| `GetLastInputInfo` | user32.dll | Idle time detection |
| `RegisterHotKey` / `UnregisterHotKey` | user32.dll | Global hotkeys (in HotkeyService) |

## State Management

Runtime state lives in `KeepAwakeService` (singleton):

- **Core:** `IsActive`, `Until`, `StartTime`, `PreventDisplaySleep`, `UseHeartbeat`
- **Auto-pause flags:** `AutoPausedBattery`, `AutoPausedNetwork`, `AutoPausedIdle`, `AutoPausedSchedule`
- **Events:** `ActiveStateChanged`, `TimedAwakeExpired`, `HeartbeatTick`

`MainViewModel` subscribes to `ActiveStateChanged` to update the UI. Config is stored in `ConfigService.Config` (strongly-typed `RedballConfig` class) and persisted to `Redball.json`.

When settings are saved, `KeepAwakeService.ReloadConfig()` is called to sync monitor enable/disable flags and thresholds.

## Singleton Pattern

`SingletonService` uses a named mutex (`Global\Redball_Singleton_Mutex`):

1. `TryAcquire()` on startup — returns false if another instance holds the mutex
2. If false → show message box → `Shutdown()`
3. On exit → `Dispose()` releases the mutex

## Crash Recovery

1. On startup, `CrashRecoveryService.CheckAndRecover()` checks for `Redball.crash.flag`
2. If found → previous session crashed → log warning, return true for safe defaults
3. `SetCrashFlag()` writes the flag for the current session
4. On clean exit, `ClearCrashFlag()` deletes the flag
5. If Redball crashes, the flag remains → detected on next startup

## Performance Optimizations

- **Throttled monitors:** Battery/network/presentation checks every 10s, schedule every 30s, idle every 1s
- **WMI caching:** Battery results cached for 60 seconds
- **Process scan caching:** Presentation mode results cached for 10 seconds
- **UI-thread timers:** `DispatcherTimer` ensures thread safety without cross-thread marshaling
- **Lazy singletons:** Services instantiated on first access via `Lazy<T>`
