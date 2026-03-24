# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added (Unreleased)

- **Release Automation**: Added end-to-end release automation in `scripts/build.ps1` for version bumping, artifact packaging, optional release commit/push, and MSI-focused build flows.
- **HID Test App Build Step**: Added `Step-BuildHidTestApp` publishing for `tests-e2e/Redball.E2E.Tests.csproj` to `dist/hid-test-app`, with `-SkipHidTestApp` opt-out support.
- **Update Controls in UI**: Added explicit "Check for Updates" entry points in tray/menu and quick settings, with update-check behavior exposed through a public async path.
- **Config Durability Layer**: Added `UserData` persistence path (`%LocalAppData%\Redball\UserData`) plus migration/recovery logic so settings survive MSI upgrades.
- **TypeThing HID Input Mode**: Added HID-driver-backed typing mode and hardware scan-code handling for better keyboard layout compatibility.
- **Localization Expansion**: Added broader language support in the WPF app (including the synchronized `bl` locale option).

### Changed (Unreleased)

- **Installer Artifacts & Packaging**: Standardized release artifact naming and installer asset selection logic to prefer installer-focused outputs where applicable.
- **MSI Launch Flow**: Refined MSI launch and post-install behavior, including VBScript launcher integration and launch control adjustments.
- **Build/Release Pipeline**: Updated build and release scripts to support force-rebuild scenarios, optional no-restart driver install paths, and clearer process/retry handling.
- **Config/Update Defaults**: Added normalization for legacy update repository settings and improved startup/default-config save behavior.
- **UI/UX Iterations**: Improved theme brush initialization/transparency behavior, expanded TypeThing/schedule settings surfaces, and removed redundant apply-step interactions through auto-apply.

### Fixed (Unreleased)

- **Settings Reset Regression**: Prevented startup UI event handlers from overwriting loaded settings by ensuring initialization gating is active during control wiring.
- **Config Loss During Upgrades**: Fixed MSI-upgrade-related settings loss by separating user data from installer-managed directories and adding migration from legacy locations.
- **Input Interception Safety**: Fixed keyboard hook filter initialization to avoid intercepting physical keyboard input and added defensive hook cleanup on failed initialization.
- **Tray/Window Lifecycle Stability**: Fixed duplicate subscriptions and resource leaks in tray icon/window lifecycle paths, including safer icon handle disposal and central cleanup.
- **RDP/Hotkey Reliability**: Improved hotkey behavior in remote session scenarios.
- **VirusTotal Artifact Filtering**: Fixed artifact name matching used in VirusTotal-related workflow filtering.

### CI (Unreleased)

- **Workflow Modernization**: Updated GitHub Actions dependencies (`actions/checkout`, `setup-dotnet`, `cache`, `stale`, and `upload-pages-artifact`) to current major versions.
- **Node Compatibility**: Added Node.js 24 opt-in coverage and fixed Node deprecation fallout across workflows.
- **Version Source Consistency**: Updated CI version extraction to read from the WPF project metadata instead of the legacy PowerShell path.
- **Pipeline Cleanup**: Removed legacy Pester-focused workflow steps and aligned CI with the WPF-first build/test pipeline.

## [3.0.0] - 2026-03-13

### Changed (3.0.0)

- **Pure WPF Architecture**: Migrated all functionality from PowerShell script to native C# WPF application. The WPF exe is now fully self-contained with no PowerShell dependency.
- **Removed IPC Layer**: Eliminated named pipe communication between WPF UI and PowerShell backend. All state management now happens directly in C#.
- **Direct Win32 API**: Keep-awake engine uses `SetThreadExecutionState` and `SendInput` (F15 heartbeat) via P/Invoke in `NativeMethods.cs`.

### Added (3.0.0)

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

### Removed (3.0.0)

- **IpcClientService.cs**: Named pipe client for PowerShell communication (no longer needed).
- **System.IO.Pipes dependency**: Removed from project file.
- **PowerShell backend dependency**: The WPF application no longer requires `Redball.ps1` to be running.

## [2.1.1] - 2026-03-11

### Security (2.1.1)

- **TLS 1.2+ Enforcement**: All HTTPS requests now enforce TLS 1.2/1.3 to prevent protocol downgrade attacks
- **Update Repo Validation**: `Get-RedballLatestRelease` validates repo owner/name against injection patterns and warns on non-default repos
- **Renamed -Encrypt to -Obfuscate**: `Export-RedballSettings`/`Import-RedballSettings` parameters renamed to avoid misleading users about Base64 security

### Fixed (2.1.1)

- **TypeThing Retry Timer Closure Bug**: Timer retry variables now use `$script:` scope so they persist across tick events
- **Locale Detection**: Replaced broken `$env:LANG` with `(Get-Culture).TwoLetterISOLanguageName` for reliable system locale detection
- **Empty Catch Blocks**: All silent `catch {}` blocks now log to DEBUG/WARN level for diagnosability
- **Form Disposal**: `Show-RedballSettings` now disposes the form in a `finally` block to prevent GDI leaks on error
- **Temp File Cleanup**: `Install-RedballUpdate` removes the downloaded temp file after installation
- **Path Consistency**: Replaced all `$PSScriptRoot` references in function bodies with `$script:AppRoot`
- **Idle Detection Text**: Menu item and settings label now reflect actual threshold instead of hardcoded "30min"

### Removed (2.1.1)

- **Duplicate Settings Dialog**: Removed dead `Show-RedballSettingsDialog` function (superseded by `Show-RedballSettings`)

### Improved (2.1.1)

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

### Added (2.0.0)

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

### Added (1.0.0)

- Initial release as "Redball"
- System tray icon with basic functionality
- Keep-awake state using `SetThreadExecutionState` API
- F15 heartbeat keypress
- Duration timer (15, 30, 60, 120 minutes)
- Prevent display sleep toggle
- Basic context menu with pause/resume

## GitHub Tag History

### Added (GitHub)

- **v2.0.x Tags**: v2.0.0, v2.0.1, v2.0.2, v2.0.3, v2.0.4, v2.0.5, v2.0.6, v2.0.7, v2.0.8, v2.0.9, v2.0.10, v2.0.11, v2.0.12, v2.0.13, v2.0.14, v2.0.15, v2.0.16, v2.0.17, v2.0.18, v2.0.19, v2.0.20, v2.0.21, v2.0.22, v2.0.23, v2.0.24, v2.0.25, v2.0.26, v2.0.27, v2.0.28, v2.0.29, v2.0.30, v2.0.31, v2.0.32, v2.0.33, v2.0.34, v2.0.35, v2.0.36, v2.0.37, v2.0.38, v2.0.39, v2.0.40, v2.0.41, v2.0.42.
- **v2.1.x Tags**: v2.1.1, v2.1.2, v2.1.3, v2.1.4, v2.1.5, v2.1.11, v2.1.14, v2.1.15, v2.1.16, v2.1.17, v2.1.18, v2.1.19, v2.1.20, v2.1.21, v2.1.22, v2.1.25, v2.1.26, v2.1.27, v2.1.28, v2.1.30, v2.1.31, v2.1.32, v2.1.33, v2.1.34, v2.1.35, v2.1.37, v2.1.43, v2.1.44, v2.1.45, v2.1.47, v2.1.48, v2.1.49, v2.1.50, v2.1.52, v2.1.53, v2.1.54, v2.1.70, v2.1.73, v2.1.80, v2.1.82, v2.1.83, v2.1.84, v2.1.85, v2.1.86, v2.1.87, v2.1.88, v2.1.89, v2.1.90, v2.1.91, v2.1.92, v2.1.93, v2.1.94, v2.1.96, v2.1.97, v2.1.105, v2.1.106, v2.1.107, v2.1.109, v2.1.110, v2.1.115, v2.1.117, v2.1.120, v2.1.121, v2.1.122, v2.1.123, v2.1.125, v2.1.126, v2.1.127, v2.1.128, v2.1.129, v2.1.130, v2.1.131, v2.1.133, v2.1.134, v2.1.136, v2.1.137, v2.1.141, v2.1.142, v2.1.143, v2.1.144, v2.1.145, v2.1.146, v2.1.147, v2.1.148, v2.1.149, v2.1.150, v2.1.151, v2.1.152, v2.1.153, v2.1.154, v2.1.155, v2.1.156, v2.1.157, v2.1.158, v2.1.159, v2.1.160, v2.1.161, v2.1.162, v2.1.163, v2.1.165, v2.1.166, v2.1.172, v2.1.173, v2.1.174, v2.1.175, v2.1.176, v2.1.177, v2.1.178, v2.1.179, v2.1.180, v2.1.181, v2.1.183, v2.1.184, v2.1.185, v2.1.187, v2.1.188, v2.1.189, v2.1.190, v2.1.191, v2.1.192, v2.1.193, v2.1.194, v2.1.195, v2.1.196, v2.1.198, v2.1.199, v2.1.200, v2.1.202, v2.1.203, v2.1.205, v2.1.206, v2.1.207, v2.1.208, v2.1.211, v2.1.212, v2.1.213, v2.1.214, v2.1.218, v2.1.219, v2.1.220, v2.1.221, v2.1.222, v2.1.223, v2.1.224, v2.1.225, v2.1.226.

[Unreleased]: https://github.com/ArMaTeC/redball/compare/v3.0.0...HEAD
[3.0.0]: https://github.com/ArMaTeC/redball/compare/v2.1.1...v3.0.0
[2.0.0]: https://github.com/ArMaTeC/redball/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/ArMaTeC/redball/releases/tag/v1.0.0
