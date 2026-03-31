# Redball

[![.NET 10](https://img.shields.io/badge/.NET_10-WPF-512BD4.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Platform-Windows_8.1%2B-0078D6.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![CI](https://img.shields.io/github/actions/workflow/status/ArMaTeC/Redball/ci.yml?label=CI)](https://github.com/ArMaTeC/Redball/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/actions/workflow/status/ArMaTeC/Redball/release.yml?label=Release)](https://github.com/ArMaTeC/Redball/actions/workflows/release.yml)
[![VirusTotal](https://img.shields.io/github/actions/workflow/status/ArMaTeC/Redball/virustotal.yml?label=VirusTotal&logo=virustotal&color=green)](https://github.com/ArMaTeC/Redball/actions/workflows/virustotal.yml)
[![Signed](https://img.shields.io/badge/Code_Signed-SHA256-success.svg)](#code-signing)

> A system tray utility to prevent Windows from sleeping, with style.

Redball is a keep-awake utility for Windows built as a **native WPF desktop application** (.NET 10) with 14 custom themes. It keeps your computer awake using the `SetThreadExecutionState` API, with smart monitoring features (battery, network, idle, schedule, presentation mode, thermal protection, process watcher, VPN detection), a Pomodoro timer, clipboard typer, built-in analytics dashboard, auto-updating, code signing, comprehensive security framework, performance monitoring, and a branded MSI installer.

![Redball Icon](installer/redball.png)

> **[Full Documentation Wiki](https://github.com/ArMaTeC/Redball/wiki)** — Comprehensive guides for every feature, function, and configuration option.

## Features

### Modern WPF Desktop Application (.NET 10)

- **Native WPF UI** — Built with .NET 10 WPF, self-contained single-file EXE (~3.3MB compressed)
- **14 Custom Themes** — System (auto-detect), Dark, Light, Midnight Blue, Forest Green, Ocean Blue, Sunset Orange, Royal Purple, Slate Gray, Rose Gold, Cyberpunk, Coffee, Arctic Frost, High Contrast
- **Design System** — Tokenized spacing, typography, colors, elevation, and motion with full Material Design color roles
- **Adaptive Layouts** — DPI-aware responsive layouts supporting 100%-300% DPI range across multi-monitor setups
- **Accessibility Baseline** — WCAG AA compliance framework with contrast auditing, keyboard navigation, and screen reader support
- **Custom Window Chrome** — Modern borderless window with custom title bar, minimize/maximize/close buttons, and rounded corners via `WindowChrome`
- **Command Palette** — Progressive disclosure UX with searchable command surface (Ctrl+K) for instant access to actions and settings
- **Settings Window** — Tabbed settings with live slider value labels for duration and typing speed, organized into General, Behavior, Smart Features, TypeThing, Pomodoro, Security, and Updates sections
- **Mini Widget** — Floating mini widget window for quick status and controls with customizable presets (Focus, Meeting, BatterySafe)
- **Onboarding Tutorial** — Interactive first-run tutorial for new users
- **Theme QA Matrix** — 14-theme control readability testing across 12 control types with WCAG AA contrast validation
- **P/Invoke SendInput** — Native Windows API for typing simulation (no WinForms dependency)
- **Theme Persistence** — Selected theme saved to config and restored on startup
- **Auto Theme Switching** — Follow Windows light/dark mode changes automatically
- **Code Signed** — EXE and MSI signed with SHA-256 certificate via `signtool` in CI

### Smart Monitoring & Automation

- **Battery-Aware Mode** — Auto-pause when battery drops below a configurable threshold, resume on charge
- **Network-Aware Mode** — Auto-pause when network disconnects, resume on reconnect
- **Idle Detection** — Auto-pause after configurable minutes of user inactivity, resume on input
- **Scheduled Operation** — Auto-start/stop at configured times and days of the week
- **Presentation Mode Detection** — Auto-activate when PowerPoint is open or Teams is screen-sharing
- **Thermal Protection** — Auto-pause when CPU temperature exceeds a configurable threshold
- **Process Watcher** — Auto-activate keep-awake when a specific process is running
- **VPN Auto Keep-Awake** — Auto-activate when a VPN connection is detected
- **Session Lock Detection** — Auto-pause when the screen is locked
- **App-Specific Rules** — Define apps that should keep awake or trigger a pause
- **Power Plan Auto-Switch** — Automatically switch Windows power plan when Redball activates
- **WiFi-Based Profiles** — Switch configuration profiles based on the connected WiFi network
- **Calendar Integration** — Auto-activate during meetings from local JSON calendar
- **Scheduled Restart Reminder** — Notify (or auto-restart) after a configurable number of days uptime
- **Session Restore** — Saves state on exit, restores on next startup
- **Singleton Instance** — Named mutex prevents multiple instances
- **Crash Recovery** — Detects previous abnormal termination and resets to safe defaults

### Core Features

- **Timed Sessions** — Set duration (15, 30, 60, 120 minutes) or run indefinitely
- **Display Sleep Control** — Optionally keep display awake too
- **Configurable Heartbeat Key** — Sends invisible F13–F16 keypresses to prevent idle detection via native `SendInput`
- **Pomodoro Timer** — Built-in focus/break cycle timer with configurable intervals and auto-start
- **Startup with Windows** — Launch automatically via Registry Run key or MSI installer
- **Toast Notifications** — Modern toast-style notifications with configurable mode filtering (All, Important, Errors, Silent)
- **JSON Configuration** — Persistent settings via `Redball.json` in `%LocalAppData%\Redball\UserData`
- **Structured Logging** — Rotating log files with configurable size limits and verbose mode
- **Settings Backup/Restore** — Export and import all settings to a JSON backup file
- **Global Hotkey** — Ctrl+Alt+Pause to toggle pause/resume system-wide
- **Localization (i18n)** — English, Spanish, French, German, and Blade Runner theme
- **Auto-Updater** — Automatic background update checks with configurable interval, or manual check from GitHub Releases
- **Code Signing** — EXE and MSI signed with SHA-256 certificate via `signtool` in CI
- **TypeThing — Clipboard Typer** — Simulates human-like typing of clipboard contents via global hotkeys, with optional TTS and Driver-Level (HID) support
- **Analytics Dashboard** — Built-in session tracking, feature adoption metrics, and CSV/JSON export
- **Mini Widget** — Floating mini widget window for quick status and controls with customizable presets
- **Onboarding Tutorial** — Interactive first-run tutorial for new users
- **Local Web API** — Optional REST API for remote control and integration (configurable port)
- **Plugin System** — Extensible plugin interface for custom functionality
- **Data Export** — GDPR-style user data export (config, analytics, session, logs)
- **Health Check Service** — Self-monitoring and diagnostics
- **MSI Installer** — WiX v4 installer with branded UI, shortcuts, and post-install launch
- **CI/CD** — GitHub Actions with unit tests, PSScriptAnalyzer lint, WPF build, security scan, and GitHub Release creation

## Quick Start

### Prerequisites

- **Windows 10** or later
- **.NET 10 Runtime** (included in the self-contained WPF EXE — no separate install needed)

### Option A — MSI Installer (Recommended)

Download the latest **`Redball.msi`** from the [Releases](https://github.com/ArMaTeC/Redball/releases) page and run it. The MSI is code-signed and includes:

- **WPF application** — Modern themed desktop UI (`Redball.UI.WPF.exe`)
- Per-user installation to `%LocalAppData%\Redball`
- Start Menu, Desktop, and optional Startup shortcuts (all with Redball icon)
- Branded installer UI with custom banner and dialog images
- Optional default behavior features (battery-aware, network-aware, idle detection, etc.)
- "Launch Redball" checkbox on the finish page

### Option B — Run the Executable

If you have the self-contained EXE from the repository or a custom build:

```powershell
# Run the WPF application
.\Redball.UI.WPF.exe

# The application will start minimized to the system tray
# Right-click the tray icon to access all features
```

## Configuration

Settings are stored in `%LocalAppData%\Redball\UserData\Redball.json`. A default file is created on first run. You can also change all settings from the **Settings** sections in the main window.

```json
{
    "HeartbeatSeconds": 59,
    "PreventDisplaySleep": true,
    "UseHeartbeatKeypress": true,
    "HeartbeatInputMode": "F15",
    "DefaultDuration": 60,
    "Theme": "Dark",
    "Locale": "en",
    "MinimizeOnStart": false,
    "MinimizeToTray": false,
    "ConfirmOnExit": true,
    "ShowNotifications": true,
    "NotificationMode": "All",
    "VerboseLogging": false,
    "MaxLogSizeMB": 10,
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
    "PomodoroEnabled": false,
    "PomodoroFocusMinutes": 25,
    "PomodoroBreakMinutes": 5,
    "PomodoroLongBreakMinutes": 15,
    "PomodoroLongBreakInterval": 4,
    "ProcessWatcherEnabled": false,
    "ProcessWatcherTarget": "",
    "PauseOnScreenLock": false,
    "VpnAutoKeepAwake": false,
    "ThermalProtectionEnabled": false,
    "ThermalThreshold": 85,
    "AutoUpdateCheckEnabled": true,
    "AutoUpdateCheckIntervalMinutes": 120,
    "UpdateChannel": "stable",
    "EncryptConfig": false
}
```

### General & UI

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `HeartbeatSeconds` | Interval between keep-awake refreshes | `59` |
| `PreventDisplaySleep` | Keep the display on in addition to preventing system sleep | `true` |
| `UseHeartbeatKeypress` | Send invisible keypresses to prevent app-level idle detection | `true` |
| `HeartbeatInputMode` | Which function key to send (`F13`, `F14`, `F15`, `F16`) | `F15` |
| `DefaultDuration` | Default timer duration in minutes | `60` |
| `Theme` | UI theme name (see theme list above) | `Dark` |
| `Locale` | Display language (`en`, `es`, `fr`, `de`, `bl`) | `en` |
| `MinimizeOnStart` | Start minimized to system tray | `false` |
| `MinimizeToTray` | Minimize to tray instead of taskbar | `false` |
| `ConfirmOnExit` | Show confirmation dialog when exiting | `true` |
| `ShowNotifications` | Enable tray/toast notifications | `true` |
| `SoundNotifications` | Play sound with notifications | `false` |
| `NotificationMode` | Notification filter (`All`, `Important`, `Errors`, `Silent`) | `All` |
| `VerboseLogging` | Record extra diagnostic log details | `false` |
| `MaxLogSizeMB` | Log rotation threshold in MB | `10` |
| `AutoExitOnComplete` | Exit automatically when a timed session finishes | `false` |

### Smart Features

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `BatteryAware` | Auto-pause when battery is low | `false` |
| `BatteryThreshold` | Battery % below which to auto-pause | `20` |
| `NetworkAware` | Auto-pause when network disconnects | `false` |
| `IdleDetection` | Auto-pause after user inactivity | `false` |
| `IdleThreshold` | Minutes of inactivity before auto-pause | `30` |
| `ScheduleEnabled` | Enable daily scheduled activation | `false` |
| `ScheduleStartTime` | Time to auto-start (HH:mm) | `09:00` |
| `ScheduleStopTime` | Time to auto-stop (HH:mm) | `18:00` |
| `ScheduleDays` | Days of the week the schedule applies | Weekdays |
| `PresentationModeDetection` | Auto-activate for PowerPoint/Teams presentations | `false` |
| `PauseOnScreenLock` | Auto-pause when the screen is locked | `false` |
| `VpnAutoKeepAwake` | Auto-activate when VPN is connected | `false` |
| `ProcessWatcherEnabled` | Auto-activate when target process is running | `false` |
| `ProcessWatcherTarget` | Process name to watch (e.g. `code.exe`) | `""` |
| `ThermalProtectionEnabled` | Auto-pause when CPU temperature is too high | `false` |
| `ThermalThreshold` | CPU temperature threshold (°C) | `85` |
| `AppRulesEnabled` | Enable app-specific keep-awake/pause rules | `false` |
| `KeepAwakeApps` | Apps that trigger keep-awake (one per line) | `""` |
| `PauseApps` | Apps that trigger a pause (one per line) | `""` |
| `PowerPlanAutoSwitch` | Auto-switch Windows power plan | `false` |
| `WifiProfileSwitchEnabled` | Switch profiles based on WiFi network | `false` |
| `WifiProfileMappings` | WiFi-to-profile mappings (`WiFiName=Profile` per line) | `""` |
| `RestartReminderEnabled` | Remind to restart after N days | `false` |
| `RestartReminderDays` | Days before restart reminder | `7` |

### Pomodoro

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `PomodoroEnabled` | Enable the Pomodoro timer | `false` |
| `PomodoroFocusMinutes` | Focus session duration | `25` |
| `PomodoroBreakMinutes` | Short break duration | `5` |
| `PomodoroLongBreakMinutes` | Long break duration | `15` |
| `PomodoroLongBreakInterval` | Focus sessions before a long break | `4` |
| `PomodoroAutoStart` | Auto-start next session | `true` |
| `PomodoroKeepAwakeDuringBreak` | Stay awake during breaks | `false` |

### TypeThing (Clipboard Typer)

| Setting | Description | Default |
| ------- | ----------- | ------- |
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
| `TypeThingInputMode` | Input method (`SendInput` or `Interception`) | `SendInput` |
| `TypeThingTtsEnabled` | Enable text-to-speech while typing | `false` |

### Updates

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `AutoUpdateCheckEnabled` | Check for updates automatically | `true` |
| `AutoUpdateCheckIntervalMinutes` | Minutes between automatic update checks | `120` |
| `UpdateChannel` | Release channel (`stable`, `beta`, `canary`) | `stable` |
| `VerifyUpdateSignature` | Require valid digital signature on updates | `true` |

### Advanced

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `EnableTelemetry` | Opt-in anonymous usage telemetry (logged locally) | `false` |
| `EnablePerformanceMetrics` | Track CPU, memory, and handle metrics | `false` |
| `WebApiEnabled` | Enable local REST API for remote control | `false` |
| `WebApiPort` | Port for the local Web API | `48080` |
| `EncryptConfig` | Encrypt Redball.json with DPAPI (default: `true`) | `true` |
| `StrictUpdateTrustMode` | Enforce strict update package validation | `false` |
| `MiniWidgetPreset` | Floating widget preset (`Focus`, `Meeting`, `BatterySafe`) | `Custom` |

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
| **Idle Detection** | Toggle idle-based auto-pause |
| **TypeThing →** | Clipboard typer submenu (Type Clipboard, Stop, Status, Settings) |
| **Settings...** | Open the tabbed settings dialog |
| **About...** | Version info and update checker |
| **Exit** | Close Redball gracefully |

### Keyboard Shortcuts

| Main UI | Description |
| ------- | ----------- |
| **Title Bar** | Custom chrome with app icon, title, subtitle, and window controls (minimize, maximize, close) |
| **Navigation Panel** | Left-side navigation with Home, Analytics, Metrics, Diagnostics, Settings, Behavior, Smart Features, TypeThing, Pomodoro, and Updates sections |
| **Content Area** | Dynamic content that changes based on selected navigation item |
| **Tray Icon** | Right-click for quick controls; left-click to toggle pause/resume |

## Command Line Arguments

The WPF application supports the following command-line arguments:

```powershell
# Start minimized to tray
.\Redball.UI.WPF.exe -minimized

# Start with specific config path
.\Redball.UI.WPF.exe -config "C:\Tools\Redball.json"
```

| Argument | Description |
| -------- | ----------- |
| `-minimized` | Start minimized to system tray |
| `-config <path>` | Specify a custom config file path |
| `--install-driver` | Install Interception driver and prompt reboot |
| `--install-driver-no-restart` | Install and attempt to restart HIDs |
| `--uninstall-driver` | Uninstall Interception driver |
| `-help` | Show help information |

## Settings GUI

The main window provides a left-side navigation panel with dedicated sections:

- **Home** — Overview dashboard with quick access cards
- **Analytics** — Session counts, usage patterns, feature events with CSV/JSON export
- **Metrics** — Feature adoption rates, retention, TypeThing success rates
- **Diagnostics** — Runtime state, logging, config validation, recent log viewer
- **Settings** — Theme, notifications, logging, minimize behavior
- **Behavior** — Display sleep prevention, heartbeat key, default duration, auto-exit
- **Smart Features** — Battery, network, idle, schedule, presentation, process watcher, VPN, thermal, session lock, app rules
- **TypeThing** — Enable/disable, typing speed, hotkeys, random pauses, newlines, TTS
- **Pomodoro** — Focus/break timer with configurable intervals
- **Updates** — Update channel, auto-check interval, signature verification

Changes are saved to `Redball.json` when you click **Apply Settings**.

TypeThing also has its own dedicated settings dialog (accessible from the TypeThing tray submenu) with grouped controls for speed, behaviour, hotkeys, and appearance — including a live WPM estimate and theme preview.

## C# Services API

Redball v3.0 is implemented as a pure C# WPF application. The core functionality is organized into services:

### Core Services

| Service | Purpose |
| --------- | --------- |
| `KeepAwakeService` | Core keep-awake engine with `SetThreadExecutionState` and heartbeat |
| `BatteryMonitorService` | WMI-based battery monitoring with auto-pause/resume |
| `NetworkMonitorService` | Network connectivity monitoring |
| `IdleDetectionService` | User idle detection via `GetLastInputInfo` |
| `ScheduleService` | Time/day-based scheduled activation |
| `PresentationModeService` | PowerPoint/Teams/Windows presentation detection |
| `CalendarIntegrationService` | JSON calendar meeting auto-activation |
| `PomodoroService` | Focus/break cycle timer |
| `ProcessWatcherService` | Auto-activate when target process is running |
| `SessionLockService` | Pause on screen lock |
| `TemperatureMonitorService` | CPU thermal protection |
| `PowerPlanService` | Automatic Windows power plan switching |
| `ProfileService` | WiFi-based configuration profiles |
| `ScheduledRestartService` | Uptime-based restart reminders |
| `SessionStateService` | Save/restore session state |
| `SessionStatsService` | Session statistics tracking |
| `AnalyticsService` | Local analytics and feature tracking |
| `CloudAnalyticsService` | Opt-in remote analytics collection |
| `DataExportService` | GDPR-style user data bundling |
| `HealthCheckService` | Application self-monitoring |
| `PluginService` | Plugin loading and management |
| `WebApiService` | Local REST API for remote control |
| `TextToSpeechService` | TTS for TypeThing |
| `ForegroundAppService` | Track active foreground application |
| `SecurityService` | Security, code signing, and SBOM |
| `InterceptionInputService` | Driver-level (HID) input simulation |
| `TemplateService` | Named text templates for TypeThing |
| `ServiceLocator` | Central DI container management |
| `Logger` | Structured logging with rotation |

### Usage Example

```csharp
// Access the KeepAwakeService singleton
var keepAwake = KeepAwakeService.Instance;

// Toggle active state
keepAwake.SetActive(!keepAwake.IsActive);

// Start a timed session
keepAwake.StartTimedAwake(TimeSpan.FromMinutes(30));

// Export settings
ConfigService.Instance.Export("backup.json");
```

## Building

### WPF Application

The WPF desktop application is built with .NET 10 as a self-contained single-file executable:

```powershell
# Publish the WPF app
dotnet publish src/Redball.UI.WPF/Redball.UI.WPF.csproj --configuration Release -o dist/wpf-publish

# Or use the comprehensive build script
.\scripts\build.ps1
```

The published EXE (~3.3MB compressed) includes the .NET runtime, uses compression and native library embedding, and has embedded debug symbols (no separate PDB file).

### MSI Installer

The MSI is built with [WiX Toolset v4](https://wixtoolset.org/):

```powershell
# Full deploy pipeline (MSI via WiX, with code signing)
.\installer\Deploy-Redball.ps1

# Build MSI only
.\installer\Build-MSI.ps1 -Version "3.0.0"
```

The installer includes branded UI images (custom banner and dialog backgrounds) and the Redball icon on all shortcuts.

### Build Script

The `build.ps1` script (located in `scripts/`) provides a comprehensive build pipeline:

```powershell
# Full build (version from version.txt)
.\scripts\build.ps1

# Specific tasks
.\scripts\build.ps1 -SkipTests    # Skip Pester tests
.\scripts\build.ps1 -SkipLint      # Skip PSScriptAnalyzer
.\scripts\build.ps1 -SkipWPF      # Skip WPF build
```

For release builds (when MSI is enabled), `build.ps1` now commits and pushes the version bump before calling `release.ps1` so GitHub release notes always have commit history.

```powershell
# Release build with default release commit message
.\scripts\build.ps1

# Override release commit message
.\scripts\build.ps1 -ReleaseMessage "chore(release): v3.1.0 + HID fixes"

# Opt out of auto release commit/push behavior
.\scripts\build.ps1 -SkipReleaseCommit
.\scripts\build.ps1 -SkipReleasePush
```

### Version Management

Version is defined in two places (kept in sync by `scripts/Bump-Version.ps1`):

1. `src/Redball.UI.WPF/Redball.UI.WPF.csproj` — `<Version>`, `<FileVersion>`, `<AssemblyVersion>`
2. `scripts/version.txt` — fallback version

### Code Signing

Both the EXE and MSI are automatically code-signed during CI releases:

- **Algorithm**: SHA-256 with RSA 2048-bit key
- **Timestamping**: DigiCert RFC 3161 timestamp server
- **Tool**: Windows SDK `signtool.exe`
- **Secrets**: `CODE_SIGNING_CERT` (base64 PFX) and `CODE_SIGNING_PASSWORD` stored as GitHub repository secrets

To use your own certificate, set the GitHub secrets:

```powershell
# Base64-encode your PFX certificate
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes("your-cert.pfx"))
$base64 | gh secret set CODE_SIGNING_CERT --repo YourOrg/Redball
"your-password" | gh secret set CODE_SIGNING_PASSWORD --repo YourOrg/Redball
```

If no certificate secrets are configured, the CI creates a self-signed development certificate as a fallback.

### CI/CD

GitHub Actions workflows (all using Node.js 24-compatible actions):

- **`ci.yml`** — On push/PR: runs WPF build, Pester tests (legacy), JSON validation, and security scan
- **`release.yml`** — On push to `main`: auto-tags version, publishes WPF app, signs EXE, builds branded MSI with WiX, signs MSI, creates GitHub Release with MSI attached

## Architecture

Redball v3.0 is a **pure C# WPF application** — all functionality runs natively with no PowerShell dependency.

```text
src/Redball.UI.WPF/
├── Interop/NativeMethods.cs          # All Win32 P/Invoke declarations
├── Services/                         # 40+ singleton services
│   ├── KeepAwakeService.cs           # Core engine (SetThreadExecutionState + heartbeat)
│   ├── BatteryMonitorService.cs      # WMI battery monitoring
│   ├── NetworkMonitorService.cs      # Network connectivity monitoring
│   ├── IdleDetectionService.cs       # GetLastInputInfo idle detection
│   ├── ScheduleService.cs            # Time/day scheduled activation
│   ├── PresentationModeService.cs    # PowerPoint/Teams/Windows detection
│   ├── PomodoroService.cs            # Focus/break cycle timer
│   ├── ProcessWatcherService.cs      # Process-based auto-activation
│   ├── SessionLockService.cs         # Screen lock detection
│   ├── TemperatureMonitorService.cs  # CPU thermal protection
│   ├── PowerPlanService.cs           # Windows power plan switching
│   ├── ScheduledRestartService.cs    # Uptime restart reminders
│   ├── AnalyticsService.cs           # Local analytics and feature tracking
│   ├── SessionStateService.cs        # Session save/restore
│   ├── SessionStatsService.cs        # Session statistics
│   ├── ConfigService.cs              # JSON config with export/import/validation
│   ├── HealthCheckService.cs         # App self-monitoring
│   ├── PluginService.cs              # Plugin system
│   ├── WebApiService.cs              # Local REST API
│   ├── UpdateService.cs              # GitHub release auto-updater
│   ├── HotkeyService.cs              # Global hotkey registration
│   ├── NotificationService.cs        # Tray/toast notifications
│   ├── LocalizationService.cs        # i18n (en, es, fr, de, bl)
│   ├── TextToSpeechService.cs        # TTS for TypeThing
│   ├── SecurityService.cs              # Security, tamper detection, threat model, CI gates
│   ├── SecretManagerService.cs       # Windows Credential Manager secret storage
│   ├── TamperPolicyService.cs          # Tamper detection with Warn/Quarantine/Block policies
│   ├── ThreatModelService.cs           # STRIDE threat inventory per release
│   ├── SecurityCIGatesService.cs       # Dependency audit, secret scanning, SBOM generation
│   ├── StartupTimingService.cs         # Startup SLO instrumentation (<1.5s cold, <0.8s warm)
│   ├── ResourceBudgetService.cs        # Per-service CPU/RAM budgets
│   ├── MemoryPressureService.cs        # Memory pressure handling with graceful degradation
│   ├── PerformanceTestService.cs       # Continuous performance testing framework
│   ├── RolloutService.cs               # Canary/beta/stable/enterprise staged rollouts
│   ├── UpdateObservabilityService.cs   # 14-stage update lifecycle telemetry
│   ├── TaskFunnelService.cs            # End-to-end task funnel analytics
│   ├── ProductStrategyService.cs       # User personas and north-star metrics
│   ├── ValueMapService.cs              # Quarterly feature-to-KPI linkage
│   ├── FeatureTieringService.cs        # Core/Pro/Experimental tier management
│   ├── CommandPaletteService.cs        # Ctrl+K searchable command surface
│   ├── WindowsShellIntegrationService.cs # Jump lists, URI protocol, toast activator
│   ├── EnterprisePolicyService.cs      # Group Policy integration
│   ├── OutboxDispatcherService.cs        # Durable offline sync with SQLite
│   ├── CrashTelemetryService.cs        # Privacy-safe crash reporting
│   ├── AccessibilityService.cs         # WCAG AA compliance framework
│   ├── DesignSystemService.cs          # Tokenized design system
│   ├── ThemeQAMatrixService.cs         # 14-theme readability testing
│   ├── LatencyMaskingService.cs        # Async operation loading UX
│   ├── InterruptionPolicyService.cs    # Non-blocking interruption management
│   └── Logger.cs                       # Structured logging with rotation
├── Models/RedballConfig.cs           # Strongly-typed configuration
├── ViewModels/MainViewModel.cs       # MVVM state + commands
├── Views/                            # MainWindow (partial classes), About, Settings, etc.
├── Themes/                           # Dark/Light base + ThemeManager variants
├── ThemeManager.cs                   # 14-theme switching engine
├── App.xaml.cs                       # Entry point + service orchestration
└── Redball.UI.WPF.csproj             # .NET 10 WPF project
```

### Component Flow

1. **Startup** — Singleton mutex → crash recovery → load config → init theme → init KeepAwakeService → restore session → create tray icon → register hotkeys
2. **Heartbeat Timer** — Fires every N seconds: re-assert `SetThreadExecutionState` + send F15 keypress via `SendInput`
3. **Duration Timer** — Fires every 1s: check timed expiry, idle (1s), battery/network/presentation (10s), schedule (30s)
4. **Settings Save** — `KeepAwakeService.ReloadConfig()` syncs all monitor settings immediately
5. **Shutdown** — Save session state → dispose KeepAwakeService → clear crash flag → release mutex → exit

## Troubleshooting

### Tray icon not appearing

- Check Windows notification area settings → "Select which icons appear on the taskbar"
- Click "Show hidden icons" (the `^` arrow) in the system tray
- Restart Windows Explorer if needed: `Stop-Process -Name explorer -Force`

### System still sleeps

- Check Windows power plan settings (some plans override API calls)
- Ensure no group policy is overriding `SetThreadExecutionState`
- Try enabling **Prevent Display Sleep** in the tray menu
- Try enabling **Verbose Logging** in Settings to diagnose the issue

### Multiple instances conflict

Redball uses a named mutex to enforce a single instance. If a stale instance is detected, it will attempt to stop it automatically. If that fails:

```powershell
# Find and stop Redball processes manually
Get-Process Redball.UI.WPF | Stop-Process -Force
```

### Log file locked

If the log file is locked by a previous instance, Redball automatically retries with exponential backoff and falls back to `%TEMP%\Redball_fallback.log`.

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Build and test (`dotnet build src/Redball.UI.WPF`)
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

### Development Setup

```powershell
git clone https://github.com/ArMaTeC/Redball.git
cd Redball

# Build the WPF application
dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj

# Run in development mode
dotnet run --project src/Redball.UI.WPF/Redball.UI.WPF.csproj

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
- [x] Settings backup/restore
- [x] Global hotkey (Ctrl+Alt+Pause)
- [x] CI/CD (GitHub Actions)
- [x] Performance metrics
- [x] TypeThing clipboard typer with global hotkeys
- [x] About dialog with update checker
- [x] Themed TypeThing settings dialog (light/dark/hacker)
- [x] Modern WPF desktop application (.NET 10)
- [x] 14 custom UI themes (Midnight Blue, Cyberpunk, Rose Gold, High Contrast, etc.)
- [x] P/Invoke SendInput (replaced WinForms SendKeys)
- [x] Self-contained compressed EXE (~3.3MB)
- [x] Branded MSI installer UI (custom banner/dialog images)
- [x] Automated code signing (EXE + MSI) in CI
- [x] Comprehensive build system (`build.ps1`)
- [x] Version management script (`Bump-Version.ps1`)
- [x] Node.js 24-compatible GitHub Actions (v5)
- [x] Pomodoro focus/break timer
- [x] Process watcher (auto-activate for specific processes)
- [x] Session lock detection (pause on screen lock)
- [x] VPN auto keep-awake
- [x] App-specific keep-awake/pause rules
- [x] Power plan auto-switch
- [x] WiFi-based configuration profiles
- [x] Scheduled restart reminders
- [x] Thermal protection (CPU temperature monitoring)
- [x] TypeThing text-to-speech
- [x] Local Web API for remote control
- [x] Analytics dashboard with CSV/JSON export
- [x] Product metrics (feature adoption, retention)
- [x] Mini widget floating window
- [x] Onboarding tutorial for new users
- [x] Toast notification system
- [x] Plugin system
- [x] Health check service
- [x] Session statistics tracking
- [x] High Contrast accessibility theme
- [x] Configurable heartbeat key (F13–F16)
- [x] Automatic background update checks

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

**Manufacturer:** ArMaTeC

## Acknowledgments

- Icon design using System.Drawing GDI+ path gradients
- [.NET 10 / WPF](https://dotnet.microsoft.com/) for the modern desktop application
- [WiX Toolset v4](https://wixtoolset.org/) for the MSI installer
- Windows SDK `signtool.exe` for code signing

## Support

- [Report bugs](https://github.com/ArMaTeC/Redball/issues)
- [Request features](https://github.com/ArMaTeC/Redball/issues)
- [Discussions](https://github.com/ArMaTeC/Redball/discussions)
