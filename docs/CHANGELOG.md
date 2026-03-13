# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [3.0.0] - 2026-03-13

### Changed

- **Pure WPF Architecture**: Migrated all functionality from PowerShell script to native C# WPF application. The WPF exe is now fully self-contained with no PowerShell dependency.
- **Removed IPC Layer**: Eliminated named pipe communication between WPF UI and PowerShell backend. All state management now happens directly in C#.
- **Direct Win32 API**: Keep-awake engine uses `SetThreadExecutionState` and `SendInput` (F15 heartbeat) via P/Invoke in `NativeMethods.cs`.

### Added

- **KeepAwakeService**: Core keep-awake engine with `SetThreadExecutionState`, F15 heartbeat via `SendInput`, timed sessions, and auto-pause/resume tracking.
- **BatteryMonitorService**: WMI-based battery monitoring with configurable threshold and 60-second cache.
- **NetworkMonitorService**: Network connectivity monitoring via `System.Net.NetworkInformation`.
- **IdleDetectionService**: User idle detection via `GetLastInputInfo` P/Invoke.
- **ScheduleService**: Time/day-based scheduled activation and deactivation.
- **PresentationModeService**: Auto-detect PowerPoint, Teams screen sharing, and Windows presentation mode.
- **SessionStateService**: Save/restore session state (`Redball.state.json`) across application restarts.
- **StartupService**: Windows startup registration via Registry Run key.
- **SingletonService**: Named mutex (`Global\Redball_Singleton_Mutex`) to prevent multiple instances.
- **CrashRecoveryService**: Crash flag file detection with safe-defaults recovery.
- **NotificationService**: Centralized tray balloon notifications with configurable notification mode filtering.
- **LocalizationService**: Built-in locales (en, es, fr, de, bl) with external `locales.json` override support.
- **TelemetryService**: Opt-in local telemetry event logging.
- **NativeMethods.cs**: Centralized P/Invoke declarations for kernel32 and user32.
- **Config Export/Import**: `ConfigService.Export()` and `ConfigService.Import()` for settings backup and restore.
- **ReloadConfig**: `KeepAwakeService.ReloadConfig()` called after settings save so monitors pick up changes immediately.

### Removed

- **IpcClientService.cs**: Named pipe client for PowerShell communication (no longer needed).
- **System.IO.Pipes dependency**: Removed from project file.
- **PowerShell backend dependency**: The WPF application no longer requires `Redball.ps1` to be running.

## [2.1.1] - 2026-03-11

### Security

- **TLS 1.2+ Enforcement**: All HTTPS requests now enforce TLS 1.2/1.3 to prevent protocol downgrade attacks
- **Update Repo Validation**: `Get-RedballLatestRelease` validates repo owner/name against injection patterns and warns on non-default repos
- **Renamed -Encrypt to -Obfuscate**: `Export-RedballSettings`/`Import-RedballSettings` parameters renamed to avoid misleading users about Base64 security

### Fixed

- **TypeThing Retry Timer Closure Bug**: Timer retry variables now use `$script:` scope so they persist across tick events
- **Locale Detection**: Replaced broken `$env:LANG` with `(Get-Culture).TwoLetterISOLanguageName` for reliable system locale detection
- **Empty Catch Blocks**: All silent `catch {}` blocks now log to DEBUG/WARN level for diagnosability
- **Form Disposal**: `Show-RedballSettings` now disposes the form in a `finally` block to prevent GDI leaks on error
- **Temp File Cleanup**: `Install-RedballUpdate` removes the downloaded temp file after installation
- **Path Consistency**: Replaced all `$PSScriptRoot` references in function bodies with `$script:AppRoot`
- **Idle Detection Text**: Menu item and settings label now reflect actual threshold instead of hardcoded "30min"

### Removed (2.1.1)

- **Duplicate Settings Dialog**: Removed dead `Show-RedballSettingsDialog` function (superseded by `Show-RedballSettings`)

### Improved

- **Update Rate Limiting**: `Get-RedballLatestRelease` caches results for 5 minutes to prevent GitHub API rate limiting
- **Battery Query Throttling**: `Get-BatteryStatus` cache TTL increased from 30s to 60s to reduce CIM overhead
- **Presentation Mode Throttling**: `Test-PresentationMode` caches results for 10 seconds to avoid expensive process scans every tick
- **Log Rotation Throttling**: Log file size check now runs every 50 writes instead of every write
- **Add-Type Gating**: `Test-HighContrastMode` and `Enable-HighDPI` skip C# compilation when types already loaded
- **ES Constants Scoping**: `ES_CONTINUOUS`, `ES_SYSTEM_REQUIRED`, `ES_DISPLAY_REQUIRED` use `$script:` prefix
- **Named Hotkey Constants**: Replaced magic numbers `100`/`101` with `$script:HOTKEY_ID_TYPETHING_START`/`STOP`
- **Large Clipboard Threshold**: Configurable via `TypeThingLargeClipboardThreshold` instead of hardcoded `10000`
- **Runspace Hex Comment**: Documented `0x80000003` ES flags in keep-awake runspace
- **TypeThing Disabled Status**: Menu shows "Status: Disabled" when TypeThing is off
- **Locale Sync**: Added 'bl' (hacker) locale to both `locales.json` and settings dropdown
- **Renamed Telemetry**: `Send-RedballTelemetry` → `Write-RedballTelemetryEvent` to clarify local-only logging
- **Test Safety Comment**: Documented `Invoke-Expression` usage in test file AST loader
- **State/Config Duplication**: Documented the dual-store pattern for future refactoring

## [2.1.0] - 2026-03-11

### Added (2.1.0)

- **Config Validation**: `Test-RedballConfigSchema` validates and sanitizes all config values against expected types, ranges, and formats on startup
- **First-Run Onboarding**: Welcome toast notification on first launch with guidance for new users
- **Crash Reporting**: Detailed crash reports with stack traces written to `Redball.crash.log` for both PowerShell and WinForms exceptions
- **Feature Usage Analytics**: Opt-in per-session feature usage counters logged at shutdown (local only, never transmitted)
- **User Error Helper**: `Show-RedballError` centralizes user-friendly error display via toast notifications
- **Copyright Headers**: Added copyright and license headers to all source files
- **ROADMAP.md**: Formal product roadmap with milestones, user personas, competitive analysis, and value proposition
- **SECURITY.md**: Security policy with vulnerability reporting process, threat model, and security features documentation
- **PRIVACY.md**: Privacy policy documenting local-only data handling, network requests, and user rights
- **CODE_OF_CONDUCT.md**: Contributor Covenant code of conduct for community standards
- **THIRD-PARTY-NOTICES.md**: Complete third-party license attribution for all dependencies
- **PS7 CI Matrix**: CI pipeline now tests on both PowerShell 5.1 and PowerShell 7
- **Code Coverage Reporting**: CI pipeline reports code coverage percentage with threshold warnings

### Fixed (2.1.0)

- **TypeThing SendInput Bug**: Fixed PowerShell nested value type copy issue where `$input.ki.wVk = value` silently modified a copy instead of the original struct — this was the root cause of typing producing no output
- **INPUT Struct Alignment**: Fixed 64-bit struct layout (`FieldOffset` 4→8, size 28→40) for correct SendInput marshaling on 64-bit Windows
- **Hotkey Debug Logging**: Added Win32 error codes and parsed VK values to hotkey registration failure messages

### Security (2.1.0)

- **Input Sanitization**: All string config values are stripped of control characters on load
- **Range Validation**: All numeric config values are clamped to safe ranges
- **Enum Validation**: UpdateChannel and TypeThingTheme values validated against allowed sets
- **Schedule Format Validation**: ScheduleStartTime/ScheduleStopTime validated against HH:mm format

## [2.0.0] - 2024-03-09

### Added (2.0.0

- **Branding**: "Redball" with new red ball icon
- **3D Icon**: Custom-drawn 3D red sphere with specular highlight and shadow effects
- **Color States**: Three distinct icon states:
  - Active: Bright red ball (crimson/tomato gradient)
  - Timed: Orange/red ball (dark orange gradient)
  - Paused: Dark red/gray ball (muted colors)
- **Configuration File Support**: JSON-based configuration with `Redball.json`
- **Structured Logging**: `Write-RedballLog` function with log rotation at 10MB
- **Pester Tests**: Comprehensive test suite covering 40+ test cases
- **Graceful Shutdown**: Pipeline stop exception handling for Ctrl+C/terminal close
- **Error Handling**: `PipelineStoppedException` catches throughout all functions
- **Memory Management**: Proper disposal of GDI+ objects and previous icons
- **Parameter Validation**: `[ValidateRange(1, 720)]` for timer duration
- **Help Documentation**: Full PowerShell help with `.SYNOPSIS`, `.DESCRIPTION`, `.PARAMETER`, `.EXAMPLE`
- **Trap Handler**: Global trap for `PipelineStoppedException` with graceful exit

### Changed (2.0.0)

- **Icon System**: Replaced coffee cup design with 3D red ball
- **Function Names**: Renamed `Update-Ui` to `Update-RedballUI`
- **Log Files**: Changed from `Redball.log` to `Redball.log`
- **Config Files**: Changed from `Redball.json` to `Redball.json`
- **UI Text**: Updated all references from "Redball" to "Redball"
- **Tray Tooltip**: Now shows `[REDBALL] Redball`

### Fixed (2.0.0)

- **Pipeline Stop Error**: No more "pipeline stopped" exception on external termination
- **$PSScriptRoot Empty**: Added fallback to current directory when `$PSScriptRoot` is empty
- **Memory Leaks**: Proper disposal of `PreviousIcon` prevents GDI+ handle leaks
- **UInt32 Overflow**: Fixed constant definitions to prevent signed integer overflow
- **DateTime Nullability**: Used `[Nullable[datetime]]` for proper null handling

### Security (2.0.0)

- **Execution Policy**: Requires `-Version 5.1` and administrative privileges
- **Error Suppression**: Sensitive error details only logged, not displayed to user

## [1.0.0] - 2024-03-01

### Initial Features

- Initial release as "Redball"
- System tray icon with basic functionality
- Keep-awake state using `SetThreadExecutionState` API
- F15 heartbeat keypress
- Duration timer (15, 30, 60, 120 minutes)
- Prevent display sleep toggle
- Basic context menu with pause/resume

[Unreleased]: https://github.com/ArMaTeC/redball/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/ArMaTeC/redball/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/ArMaTeC/redball/releases/tag/v1.0.0
