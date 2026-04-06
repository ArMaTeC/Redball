# Redball

[![.NET 10](https://img.shields.io/badge/.NET_10-WPF-512BD4.svg)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Platform-Windows_8.1%2B-0078D6.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![CI](https://img.shields.io/github/actions/workflow/status/ArMaTeC/Redball/ci.yml?label=CI)](https://github.com/ArMaTeC/Redball/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/actions/workflow/status/ArMaTeC/Redball/release.yml?label=Release)](https://github.com/ArMaTeC/Redball/actions/workflows/release.yml)
[![VirusTotal](https://img.shields.io/github/actions/workflow/status/ArMaTeC/Redball/virustotal.yml?label=VirusTotal&logo=virustotal&color=green)](https://github.com/ArMaTeC/Redball/actions/workflows/virustotal.yml)
[![Signed](https://img.shields.io/badge/Code_Signed-SHA256-success.svg)](#code-signing)

> A professional clipboard typer that keeps your computer awake — type anything, anywhere.

Redball is a **clipboard automation tool** for Windows built as a native WPF desktop application (.NET 10) with 14 custom themes. Its flagship feature, **TypeThing**, simulates human-like typing of clipboard contents into any application — perfect for systems that block Ctrl+V. It also includes a smart **keep-awake** utility with monitoring features (battery, network, idle, schedule, presentation mode, thermal protection, process watcher, VPN detection), built-in analytics dashboard, auto-updating, code signing, comprehensive security framework, performance monitoring, and a branded MSI installer.

![Redball Icon](installer/redball.png)

> **[Full Documentation Wiki](https://github.com/ArMaTeC/Redball/wiki)** — Comprehensive guides for every feature, function, and configuration option.

## Features

### TypeThing — Clipboard Typer

The flagship feature of Redball. TypeThing reads text from your clipboard and types it character-by-character using the Windows `SendInput` API with `KEYEVENTF_UNICODE`, making it compatible with virtually any application and character set.

- **Global Hotkeys** — Start typing with Ctrl+Shift+V (configurable), emergency stop with Ctrl+Shift+X
- **Human-Like Typing** — Configurable random delays and pauses simulate natural typing patterns
- **Universal Compatibility** — Works in applications that block Ctrl+V paste (remote desktop, secure terminals, VMs, web forms)
- **Full Unicode Support** — Handles any character set via `KEYEVENTF_UNICODE`
- **Configurable Speed** — 10–500ms per character (~60–400 WPM range)
- **Smart Newline Handling** — Optionally types Enter for line breaks
- **Emergency Stop** — Instant stop hotkey at any time
- **Progress Tracking** — Live tray menu status shows typing progress
- **Text-to-Speech** — Optional TTS reads text as it types
- **HID/Driver-Level Support** — Optional Windows Service for elevated and RDP environments
- **Countdown Timer** — Configurable delay before typing starts (switch to target app)
- **Large Clipboard Warning** — Confirmation dialog for >10,000 characters

### Modern WPF Desktop Application (.NET 10)

- **Native WPF UI** — Built with .NET 10 WPF, self-contained single-file EXE (~3.3MB compressed)
- **14 Custom Themes** — System (auto-detect), Dark, Light, Midnight Blue, Forest Green, Ocean Blue, Sunset Orange, Royal Purple, Slate Gray, Rose Gold, Cyberpunk, Coffee, Arctic Frost, High Contrast
- **Design System** — Tokenized spacing, typography, colors, elevation, and motion with full Material Design color roles
- **Adaptive Layouts** — DPI-aware responsive layouts supporting 100%-300% DPI range across multi-monitor setups
- **Accessibility Baseline** — WCAG AA compliance framework with contrast auditing, keyboard navigation, and screen reader support
- **Custom Window Chrome** — Modern borderless window with custom title bar, minimize/maximize/close buttons, and rounded corners via `WindowChrome`
- **Command Palette** — Progressive disclosure UX with searchable command surface (Ctrl+K) for instant access to actions and settings
- **Settings Window** — Tabbed settings with live slider value labels for duration and typing speed, organized into General, Behavior, Smart Features, TypeThing, Security, and Updates sections
- **Mini Widget** — Floating mini widget window for quick status and controls with customizable presets (Focus, Meeting, BatterySafe)
- **Onboarding Tutorial** — Interactive first-run tutorial for new users
- **Theme QA Matrix** — 14-theme control readability testing across 12 control types with WCAG AA contrast validation
- **Theme Persistence** — Selected theme saved to config and restored on startup
- **Auto Theme Switching** — Follow Windows light/dark mode changes automatically
- **Code Signed** — EXE and installer signed with SHA-256 certificate via `signtool` in CI

### Keep-Awake — Smart System Monitor

A secondary feature that prevents Windows from sleeping using the `SetThreadExecutionState` API with intelligent monitoring and automation.

- **Timed Sessions** — Set duration (15, 30, 60, 120 minutes) or run indefinitely
- **Display Sleep Control** — Optionally keep display awake too
- **Configurable Heartbeat Key** — Sends invisible F13–F16 keypresses to prevent idle detection via native `SendInput`
- **Smart Monitoring & Automation** — Battery-aware, network-aware, idle detection, scheduled operation, presentation mode detection, thermal protection, process watcher, VPN detection, session lock detection
- **Toast Notifications** — Modern toast-style notifications with configurable mode filtering (All, Important, Errors, Silent)

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

### Core Features

- **Startup with Windows** — Launch automatically via Registry Run key or MSI installer
- **JSON Configuration** — Persistent settings via `Redball.json` in `%LocalAppData%\Redball\UserData`
- **Structured Logging** — Rotating log files with configurable size limits and verbose mode
- **Settings Backup/Restore** — Export and import all settings to a JSON backup file
- **Global Hotkey** — Ctrl+Alt+Pause to toggle pause/resume system-wide
- **Localization (i18n)** — English, Spanish, French, German, and Blade Runner theme
- **Auto-Updater** — Automatic background update checks with configurable interval, or manual check from GitHub Releases
- **Analytics Dashboard** — Built-in session tracking, feature adoption metrics, and CSV/JSON export
- **Mini Widget** — Floating mini widget window for quick status and controls with customizable presets
- **Local Web API** — Optional REST API for remote control and integration (configurable port)
- **Plugin System** — Extensible plugin interface for custom functionality
- **Data Export** — GDPR-style user data export (config, analytics, session, logs)
- **Health Check Service** — Self-monitoring and diagnostics
- **NSIS Installer** — Modern branded installer with feature selection, .NET runtime detection, auto-start options, and post-install launch
- **CI/CD** — GitHub Actions with unit tests, PSScriptAnalyzer lint, WPF build, security scan, and GitHub Release creation

## Quick Start

### Prerequisites

- **Windows 10** or later
- **.NET 10 Runtime** (included in the self-contained WPF EXE — no separate install needed)

### Option A — NSIS Installer (Recommended)

Download the latest **`Redball-Setup.exe`** from the [Releases](https://github.com/ArMaTeC/Redball/releases) page and run it. The installer is code-signed and includes:

- **WPF application** — Modern themed desktop UI (`Redball.UI.WPF.exe`)
- Per-user installation to `%LocalAppData%\Redball`
- Start Menu, Desktop, and optional Startup shortcuts (all with Redball icon)
- Branded installer UI with custom banner and dialog images
- Optional default behavior features (battery-aware, network-aware, idle detection, etc.)
- .NET 10 Runtime detection and installation option
- "Launch Redball" checkbox on the finish page
- Full uninstaller that cleans registry and shortcuts

### Option B — Run the Executable

If you have the self-contained EXE from the repository or a custom build:

```powershell
# Run the WPF application
.\Redball.UI.WPF.exe

# The application will start minimized to the system tray
# Right-click the tray icon to access all features
```

## Using TypeThing

TypeThing is the fastest way to use Redball:

1. **Copy text** to your clipboard (Ctrl+C)
2. **Press Ctrl+Shift+V** (or your configured start hotkey)
3. **Switch to your target application** during the 3-second countdown
4. **Watch as text is typed** character-by-character with human-like delays
5. **Press Ctrl+Shift+X** at any time to emergency-stop

### TypeThing Settings

Access TypeThing settings via:
- **Tray Menu → TypeThing → TypeThing Settings...**
- **Main Window → TypeThing tab**

| Setting | Default | Description |
| ------- | ------- | ----------- |
| `TypeThingEnabled` | `true` | Master switch for the TypeThing feature |
| `TypeThingMinDelayMs` | `30` | Minimum delay between keystrokes (ms) |
| `TypeThingMaxDelayMs` | `120` | Maximum delay between keystrokes (ms) |
| `TypeThingStartDelaySec` | `3` | Countdown seconds before typing begins |
| `TypeThingStartHotkey` | `Ctrl+Shift+V` | Global hotkey to start typing |
| `TypeThingStopHotkey` | `Ctrl+Shift+X` | Global hotkey to stop typing |
| `TypeThingTypeNewlines` | `true` | Press Enter when a newline is encountered |
| `TypeThingTtsEnabled` | `false` | Enable text-to-speech while typing |
| `TypeThingInputMode` | `SendInput` | Input method (`SendInput` or `Interception`) |

### Typing Speed Reference

| Min Delay | Max Delay | Approx WPM |
| --------- | --------- | ----------- |
| 10 ms | 50 ms | ~400 WPM |
| 30 ms | 120 ms | ~160 WPM |
| 50 ms | 200 ms | ~96 WPM |
| 100 ms | 300 ms | ~60 WPM |

## Keep-Awake Configuration

The keep-awake feature is secondary and disabled by default. Enable it from the tray menu or settings:

| Setting | Description | Default |
| ------- | ----------- | ------- |
| `HeartbeatSeconds` | Interval for keep-awake heartbeat | 59 |
| `PreventDisplaySleep` | Keep display awake while active | true |
| `UseHeartbeatKeypress` | Send invisible F15 keypress | true |
| `DefaultDuration` | Default timer duration (minutes) | 60 |

See the [Keep-Awake documentation](https://github.com/ArMaTeC/Redball/wiki/KeepAwake) for full smart monitoring features.

## Configuration

Settings are stored in `%LocalAppData%\Redball\UserData\Redball.json`. A default file is created on first run. You can also change all settings from the **Settings** sections in the main window.

```json
{
    "TypeThingEnabled": true,
    "TypeThingMinDelayMs": 30,
    "TypeThingMaxDelayMs": 120,
    "TypeThingStartDelaySec": 3,
    "TypeThingStartHotkey": "Ctrl+Shift+V",
    "TypeThingStopHotkey": "Ctrl+Shift+X",
    "TypeThingTypeNewlines": true,
    "TypeThingTtsEnabled": false,
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

## Architecture

Redball v3.0 is a **pure C# WPF application** — all functionality runs natively with no PowerShell dependency.

```text
src/Redball.UI.WPF/
├── Interop/NativeMethods.cs          # All Win32 P/Invoke declarations
├── Services/                         # 40+ singleton services
│   ├── TypeThingService.cs           # Core clipboard typing engine
│   ├── KeepAwakeService.cs           # Keep-awake engine (SetThreadExecutionState + heartbeat)
│   ├── BatteryMonitorService.cs      # WMI battery monitoring
│   ├── NetworkMonitorService.cs      # Network connectivity monitoring
│   ├── IdleDetectionService.cs       # GetLastInputInfo idle detection
│   ├── ScheduleService.cs            # Time/day scheduled activation
│   ├── PresentationModeService.cs    # PowerPoint/Teams/Windows detection
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

1. **Startup** — Singleton mutex → crash recovery → load config → init theme → init TypeThingService → init KeepAwakeService → restore session → create tray icon → register hotkeys
2. **TypeThing** — Global hotkey → read clipboard → countdown → SendInput with delays → progress tracking → cleanup
3. **Keep-Awake Heartbeat** — Fires every N seconds: re-assert `SetThreadExecutionState` + send F15 keypress via `SendInput`
4. **Duration Timer** — Fires every 1s: check timed expiry, idle (1s), battery/network/presentation (10s), schedule (30s)
5. **Settings Save** — `ConfigService.Save()` syncs all settings immediately
6. **Shutdown** — Save session state → dispose services → clear crash flag → release mutex → exit

## Troubleshooting

### TypeThing not typing

- Ensure the target application has focus before the countdown ends
- Check that TypeThing is enabled in Settings
- Verify your hotkeys are not conflicting with other applications
- Try increasing the start delay to give yourself more time to switch windows
- Check verbose logs for SendInput errors

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

- [x] TypeThing clipboard typer with global hotkeys
- [x] TypeThing human-like typing with random delays
- [x] TypeThing text-to-speech support
- [x] TypeThing HID/Driver-level support via Windows Service
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
- [x] Process watcher (auto-activate for specific processes)
- [x] Session lock detection (pause on screen lock)
- [x] VPN auto keep-awake
- [x] App-specific keep-awake/pause rules
- [x] Power plan auto-switch
- [x] WiFi-based configuration profiles
- [x] Scheduled restart reminders
- [x] Thermal protection (CPU temperature monitoring)
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
