# Redball - Agent Guide

This document provides context and guidelines for AI agents working on the Redball codebase.

## Project Overview

**Redball** is a Windows system tray utility that prevents your computer from sleeping, with style. It's a native WPF desktop application built with .NET 10, featuring 14 custom themes, smart monitoring, and extensive automation capabilities.

### Core Features

- **Keep-Awake Engine**: Uses `SetThreadExecutionState` Windows API to prevent system sleep
- **Heartbeat System**: Sends invisible F13-F16 keypresses via `SendInput` to prevent idle detection
- **Smart Monitoring**: Battery, network, idle, schedule, presentation mode, thermal protection, process watching, VPN detection
- **TypeThing**: Clipboard typer with human-like keystroke simulation and optional TTS
- **14 Custom Themes**: From System/Dark/Light to Cyberpunk, Rose Gold, High Contrast
- **Mini Widget**: Floating quick-access window with customizable presets
- **Auto-Updater**: Automatic background update checks from GitHub Releases
- **Analytics Dashboard**: Built-in session tracking and feature metrics
- **Code Signed**: EXE and MSI signed with SHA-256 certificate

## Tech Stack

### Application

- **Language**: C# 13 / .NET 10
- **Framework**: WPF (Windows Presentation Foundation)
- **Architecture**: Self-contained single-file EXE (~3.3MB compressed)
- **Platform**: Windows 8.1+ (Windows 10/11 recommended)
- **Input Simulation**: P/Invoke `SendInput` (no WinForms dependency)
- **Driver**: Optional KMDF driver for HID-level input (`Redball.KMDF.sys`)

### Installer

- **Tool**: WiX Toolset v4
- **Output**: Code-signed MSI with branded UI
- **Install Location**: `%LocalAppData%\Redball` (per-user)

### CI/CD

- **Platform**: GitHub Actions
- **Build**: .NET 10 SDK
- **Signing**: `signtool.exe` with repository secrets
- **Tests**: xUnit, Pester (legacy), BenchmarkDotNet

## Project Structure

```text
Redball/
├── src/
│   ├── Redball.UI.WPF/           # Main WPF application
│   │   ├── Services/             # 40+ singleton services
│   │   ├── Views/                # XAML views (MainWindow, Settings, etc.)
│   │   ├── ViewModels/           # MVVM view models
│   │   ├── Themes/               # Theme XAML dictionaries
│   │   └── Interop/              # Win32 P/Invoke declarations
│   ├── Redball.Core/             # Core shared library
│   ├── Redball.Service/          # Windows Service components
│   ├── Redball.SessionHelper/    # Session helper executable
│   └── Redball.Driver/             # KMDF driver source
├── tests/                          # Unit tests (xUnit)
├── tests-e2e/                      # E2E tests
├── tests-integration/              # Integration tests
├── tests-ui-automation/            # UI automation tests
├── installer/                      # WiX installer files
├── scripts/                        # PowerShell build scripts
│   ├── build.ps1                 # Main build script
│   └── Bump-Version.ps1          # Version management
├── docs/                           # Documentation
└── wiki/                           # GitHub wiki content
```

### Key Services (in `src/Redball.UI.WPF/Services/`)

| Service                    | Purpose                                     |
| -------------------------- | ------------------------------------------- |
| `KeepAwakeService`         | Core engine using `SetThreadExecutionState` |
| `BatteryMonitorService`    | WMI-based battery level monitoring          |
| `NetworkMonitorService`    | Network connectivity detection              |
| `IdleDetectionService`     | `GetLastInputInfo` idle detection           |
| `ScheduleService`          | Time/day-based auto activation              |
| `ProcessWatcherService`    | Process-based auto-activation               |
| `ConfigService`            | JSON settings with validation               |
| `UpdateService`            | GitHub Release auto-updater                 |
| `HotkeyService`            | Global hotkey registration                  |
| `NotificationService`      | Tray/toast notifications                    |
| `InterceptionInputService` | Driver-level HID input simulation           |

## Coding Standards

### C# / WPF

- **MVVM Pattern**: Strict separation of Views, ViewModels, and Models
- **Singleton Services**: Service locator pattern with `ServiceLocator` class
- **Async/Await**: Proper async patterns for I/O and long-running operations
- **P/Invoke**: All Win32 declarations centralized in `Interop/NativeMethods.cs`
- **Null Safety**: Enable nullable reference types
- **Logging**: Use `Logger` service with structured logging

### Naming Conventions

- **Files**: PascalCase (e.g., `KeepAwakeService.cs`)
- **Classes**: PascalCase (e.g., `KeepAwakeService`)
- **Methods**: PascalCase (e.g., `SetActive()`)
- **Private fields**: `_camelCase` with underscore prefix
- **Constants**: `PascalCase` or `ALL_CAPS` for true constants
- **XAML**: PascalCase for element names

### Error Handling

- Use try/catch at service boundaries
- Log errors via `Logger.Instance.LogError()`
- Never swallow exceptions silently
- Dispose resources properly (implement `IDisposable` where needed)

## Development Workflow

### Build & Run

```powershell
# Build the WPF application
dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj

# Run in development mode
dotnet run --project src/Redball.UI.WPF/Redball.UI.WPF.csproj

# Full build pipeline (tests, WPF, MSI)
.\scripts\build.ps1

# Run tests
dotnet test tests/

# Benchmarks
dotnet run --project tests/Redball.Benchmarks --configuration Release
```

### Common Tasks

- **Adding a service**: Create in `Services/`, register in `ServiceLocator`, inject where needed
- **Modifying UI**: Edit XAML in `Views/`, bind to ViewModel properties
- **Adding a theme**: Create in `Themes/`, register in `ThemeManager`
- **Updating config**: Modify `Models/RedballConfig.cs`, update `ConfigService` validation

## Security-Focused Development

- **Input Validation**: Validate all user inputs before processing
- **Path Traversal**: Never use user input directly in file paths
- **Secrets**: Store sensitive data in Windows Credential Manager via `SecretManagerService`
- **Code Signing**: All releases must be signed (handled in CI)
- **Config Encryption**: Support optional AES-256 config encryption

## Troubleshooting

- **Check logs**: `%LocalAppData%\Redball\Logs\Redball_*.log`
- **Config location**: `%LocalAppData%\Redball\UserData\Redball.json`
- **Crash recovery**: Crash flag at `%LocalAppData%\Redball\UserData\crash.flag`
- **Single instance**: Named mutex prevents multiple instances
- **Log file locked**: Falls back to `%TEMP%\Redball_fallback.log`

## Key Files for Agents

| File                                              | Purpose                   |
| ------------------------------------------------- | ------------------------- |
| `src/Redball.UI.WPF/Services/KeepAwakeService.cs` | Core keep-awake engine    |
| `src/Redball.UI.WPF/Models/RedballConfig.cs`      | Configuration schema      |
| `src/Redball.UI.WPF/Services/ConfigService.cs`    | Settings persistence      |
| `src/Redball.UI.WPF/Views/MainWindow.xaml.cs`     | Main window logic         |
| `src/Redball.UI.WPF/Interop/NativeMethods.cs`     | Win32 P/Invoke            |
| `Directory.Build.props`                           | Global MSBuild properties |
