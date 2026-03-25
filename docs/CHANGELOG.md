# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added (Unreleased)

- **Self-Contained MSI Installer v2.0**: Completely redesigned MSI installer that is now fully self-contained with no external script dependencies.
  - Native C# custom action DLL (`Redball.Installer.CustomActions.dll`) for all installer operations.
  - Self-contained backup/restore of user data during upgrades (no PowerShell required).
  - Native process management for stopping running Redball instances before install/upgrade.
  - Built-in logging system that tracks all installer actions to temp log file.
  - Clean uninstall that properly removes user data when requested.
  - Removed all PowerShell custom actions and external script dependencies.
  - _Comment_: The MSI is now the "one true source" for all install/upgrade/uninstall operations.
- **Release Automation**: Added end-to-end release automation in `scripts/build.ps1` for version bumping, artifact packaging, optional release commit/push, and MSI-focused build flows.
  - _Comment_: This reduces manual release overhead and improves repeatability for shipping builds.
- **Update Controls in UI**: Added explicit "Check for Updates" entry points in tray/menu and quick settings, with update-check behavior exposed through a public async path.
  - _Comment_: Improves discoverability and gives users a direct way to validate update availability.
- **Config Durability Layer**: Added `UserData` persistence path (`%LocalAppData%\Redball\UserData`) plus migration/recovery logic so settings survive MSI upgrades.
  - _Comment_: This addresses upgrade-related data loss and keeps user preferences stable between versions.
- **TypeThing HID Input Mode**: Added HID-driver-backed typing mode and hardware scan-code handling for better keyboard layout compatibility.
- **HID Robustness & Safety**: Implemented a comprehensive HID safety stack including:
  - **Smart Install/Uninstall**: One-click driver management with explicit install state detection and elevated toggle button.
  - **HID Safe Mode**: Emergency override that forces SendInput and prevents HID initialization if failures occur.
  - **Auto-Fallback**: Automatic activation of Safe Mode after 3 consecutive HID initialization failures.
  - **Deterministic Cleanup**: Guaranteed HID resource release on app shutdown and window closure to prevent keyboard lockups.
  - **Emergency Release**: Dedicated hotkey (`Ctrl+Shift+Esc`) and tray menu item to immediately release HID control.
  - **Live Health Diagnostics**: Circular health indicator with animations (pulsing green for ready, flashing red for error) and real-time status details.
  - **Hot-Plug Recovery**: Automatic HID stack refresh when USB keyboard devices are added or removed.
  - **Idle Auto-Release**: Automatic release of HID resources after 30 minutes of inactivity.
  - **Per-Character Retry**: Three-attempt retry logic with backoff for failed HID character sends.
  - **Automatic Re-Init**: Active typing sessions attempt one silent HID re-initialization if a character fails before falling back to SendInput.
  - **Health Checks**: Automatic HID capability validation every time the application window is focused.
  - **Typing Progress**: Live UI overlay with progress bar and cancel button for active typing operations.
  - **HID Test Box**: Interactive testing area in Settings to verify driver functionality before use.
  - **Repair HID Stack**: One-click full fix sequence that validates integrity and refreshes the driver/device stack.
  - **Audio Feedback**: Optional subtle click sounds for successful HID sends providing non-visual confirmation.
  - **Driver Integrity Validation**: SHA256-based verification of driver files on disk to detect corruption.
  - **Layout Compatibility**: Diagnostic detection of non-standard keyboard layouts for improved input mapping.
  - **Windows Compatibility**: Integrated OS version checks to ensure HID driver compatibility.
- **Enhanced Build Pipeline**: Updated `build.ps1` with detailed HID feature summaries and improved dependency handling for LottieSharp and Xaml.Behaviors.
- **Localization Expansion**: Added broader language support in the WPF app (including the synchronized `bl` locale option).
- **Mini Widget Presets**: Added Focus/Meeting/Battery Safe preset support with one-click apply actions in main settings and quick settings popup.
  - _Comment_: Adds fast, goal-based widget setup for common workflows.
- **Mini Widget Preset Badge**: Added an always-visible preset indicator badge directly inside the mini widget header.
- **Secure Secret Management (ISecretProvider)**: Implemented OS-protected secure secret storage infrastructure:
  - `ISecretProvider` interface for pluggable secret storage backends.
  - `WindowsCredentialSecretProvider` using Windows Credential Manager APIs (CredWrite/CredRead/CredEnumerate).
  - `SecretManagerService` singleton for high-level secret operations with health monitoring.
  - `SecretManagementWindow` UI for managing API keys and credentials securely.
  - `SecretEditorDialog` for adding/editing secrets with masked input.
  - CloudAnalyticsService migrated to use secure secret storage instead of plain-text config.
  - New "Security" navigation section in main dashboard with secret status and provider health.
  - Secrets are never stored in configuration files - only in OS-protected credential store.
  - _Comment_: Completes sec-2 from improve_me.txt - replaces static API key handling with OS-protected secure storage.
- **Default DPAPI Encryption (sec-1)**: Configuration files now encrypted by default using Windows DPAPI:
  - `EncryptConfig` property now defaults to `true` for new installations.
  - Auto-migration: existing unencrypted configs are automatically encrypted on next save.
  - UI checkbox in Settings to control encryption (enabled by default).
  - Config files use "RBENC:" header to identify encrypted files.
  - Only the current Windows user can decrypt and read the configuration.
  - Registry storage also supports encrypted payloads.
  - _Comment_: Completes sec-1 from improve_me.txt - default-encrypts sensitive local state.
- **Update Package Trust Chain (sec-3)**: Implemented comprehensive update package trust validation:
  - `SecurityService.ValidateUpdatePackage()` - validates Authenticode signature, certificate chain, pinned publisher thumbprints, and manifest hashes.
  - Trusted publisher thumbprint pinning with runtime extensibility for enterprise deployments.
  - `TrustValidationResult` class with detailed success/warning/failure reporting.
  - Integration into `UpdateService.DownloadAndInstallAsync()` for installer validation.
  - `StrictUpdateTrustMode` config setting to enforce stricter validation policies.
  - Manifest hash verification against signed update manifests.
  - Blocks installation if Authenticode signature is invalid or manifest hash mismatches.
  - _Comment_: Completes sec-3 from improve_me.txt - enforces update package trust chain with Authenticode + manifest signature + pinned publisher.
- **Tamper Policy Levels (sec-4)**: Implemented configurable tamper detection and response system:
  - `TamperPolicyService` singleton for managing tamper detection policies and responses.
  - Three policy levels: `Warn` (log only), `Quarantine` (disable feature), `Block` (prevent operation).
  - `TamperEvent` and `TamperEventType` for tracking security incidents.
  - Per-event-type policy configuration (ConfigTamperPolicy, UpdateSignaturePolicy, CertificatePinPolicy, IntegrityPolicy).
  - User-safe recovery flow with `ResolveTamperEvent()` and `AttemptAutoRecovery()` methods.
  - Integration with ConfigService for config file tampering detection.
  - Integration with trust validation for certificate pinning failures.
  - UI in Security panel showing tamper event count and policy status.
  - "View Events" button to see tamper history and resolution status.
  - _Comment_: Completes sec-4 from improve_me.txt - tamper policy levels with user-safe recovery flow.
- **Threat Model Document (sec-5)**: Implemented per-release threat model documentation system:
  - `ThreatModelService` singleton with built-in threat inventory for Redball.
  - 12 identified threats mapped to STRIDE categories (Spoofing, Tampering, Repudiation, Information Disclosure, DoS, Elevation of Privilege).
  - Risk levels: Critical, High, Medium, Low, Informational.
  - Each threat includes: ID, title, description, category, risk, mitigation, test reference, verification status.
  - `ThreatModelSummary` for statistics (total, mitigated, unmitigated, by risk level).
  - `GenerateMarkdownDocument()` produces comprehensive markdown threat model document.
  - Export to JSON for programmatic access.
  - UI in Security panel showing threat count, mitigation status, and risk summary.
  - "Export" button to save threat model as Markdown or JSON.
  - Auto-maps implemented security features to threats (sec-1 through sec-4).
  - _Comment_: Completes sec-5 from improve_me.txt - threat model document per release with mitigations/tests mapping.
- **Security CI Gates (sec-6)**: Implemented automated security validation gates for CI/CD pipeline:
  - `SecurityCIGatesService` singleton for running comprehensive security validation.
  - `SecurityGateResult` with pass/fail status, errors, warnings, and timing.
  - Six gate types: Dependency Audit, Secret Scanning, Signing Verification, SBOM Generation, Threat Model Validation, Configuration Validation.
  - **Dependency Audit**: Scans NuGet packages against known vulnerability database.
  - **Secret Scanning**: Pattern-based detection of hardcoded secrets (API keys, tokens, passwords, private keys).
  - **Signing Verification**: Validates Authenticode signatures on all release binaries.
  - **SBOM Generation**: Generates SPDX-compliant Software Bill of Materials.
  - **Threat Model Validation**: Ensures all critical/high-risk threats are mitigated.
  - **Configuration Validation**: Checks security settings are properly enabled.
  - False positive detection for secret scanning (placeholders, examples, variable names).
  - Comprehensive JSON report generation with all gate results.
  - UI in Security panel to run gates on-demand with visual status indicators.
  - "Run Gates" button with real-time progress and detailed results.
  - _Comment_: Completes sec-6 from improve_me.txt - security CI gates with dependency audit, secret scanning, signing verification, and SBOM generation.
- **Service-Based Input Injection (Driver Replacement)**: Replaced kernel driver with Windows Service architecture for input injection, eliminating driver signing requirements.
  - New `Redball.Service` Windows Service for session-aware input injection via named pipes.
  - `Redball.SessionHelper` companion process for RDP session input injection using `CreateProcessAsUser`.
  - IPC client library (`InputServiceClient`) for WPF UI to service communication.
  - Service installer/uninstaller scripts (`Install-Service.ps1`, `Uninstall-Service.ps1`).
  - Build script updated to publish service and helper as single-file executables.
  - No EV certificate or driver signing required - runs as Windows Service with standard user permissions.
  - _Comment_: Maintains RDP console input capability without kernel complexity or attestation signing.
- **Offline Outbox + Reconciliation (improve_me.txt sec-A)**: Implemented durable local-first sync infrastructure for reliable offline operation:
  - `SyncEvent` model with idempotency keys, aggregate versioning, and retry tracking.
  - `IOutboxStore` interface with SQLite-backed durable queue (`SqliteOutboxStore`).
  - `OutboxDispatcherService` background processor with exponential backoff + jitter for retries.
  - Circuit breaker pattern for API health degradation.
  - Sync health dashboard UI (`SyncHealthPage`) showing queue depth, oldest pending age, dead letter count.
  - `IdempotencyKeyGenerator` for exactly-once delivery semantics.
  - `HttpSyncApi` reference implementation with idempotency header support.
  - Automatic purge of completed events after retention period.
  - _Comment_: Completes improve_me.txt item A - enables reliable sync under intermittent connectivity.
- **Production Crash Telemetry (improve_me.txt sec-C)**: Implemented privacy-safe crash reporting for fleet-level reliability insights:
  - `CrashEnvelope` model with app version, OS version, exception type, stack fingerprint (hashed, no PII).
  - `CrashFingerprint` generator that normalizes stack traces for consistent grouping.
  - `CrashTelemetryService` with local storage, queued upload, and PII scrubbing (machine names hashed).
  - Automatic upload on next startup with retry logic.
  - Diagnostics bundle exporter for support (crash history + metadata ZIP).
  - User consent UI (`CrashTelemetryPage`) with opt-in requirement.
  - 30-day retention with automatic purge of old reports.
  - _Comment_: Completes improve_me.txt item C - enables production-grade reliability monitoring without compromising privacy.
- **Command Palette (improve_me.txt sec-D)**: Implemented progressive disclosure UX with searchable command surface:
  - `CommandPaletteWindow` activated via `Ctrl+K` for instant access to actions and settings.
  - `SettingDefinition` model with `VisibilityTier` (Basic/Advanced/Experimental) for progressive disclosure.
  - `CommandPaletteIndex` with 15+ searchable commands including navigation, actions, diagnostics.
  - Real-time search with scoring (exact match > prefix > contains > keywords).
  - Category filtering and keyboard navigation (Up/Down/Enter/Esc).
  - Settings metadata model supporting command IDs for direct setting jumps.
  - _Comment_: Completes improve_me.txt item D - reduces complexity while preserving power-user depth.
- **Performance SLO + Adaptive Scheduler (improve_me.txt sec-E)**: Implemented startup instrumentation and adaptive monitoring:
  - `StartupTimingService` with span recording (config load, service init, theme apply, tray ready).
  - SLO targets: Cold start < 1.5s (95% pass rate), Warm start < 0.8s (99% pass rate).
  - `SloStatistics` with pass rates, averages, and P95 latencies.
  - Regression detection comparing current startup to 7-day baseline.
  - `AdaptiveIntervalPolicy` for dynamic monitor frequency based on battery/CPU/user-idle state.
  - `SharedScheduler` for coalescing monitor ticks and reducing timer churn.
  - SLO dashboard UI (`SloDashboardPage`) with health indicators and export capability.
  - _Comment_: Completes improve_me.txt item E - guarantees predictable startup and long-session efficiency.

### Changed (Unreleased)

- **Installer Artifacts & Packaging**: Standardized release artifact naming and installer asset selection logic to prefer installer-focused outputs where applicable.
  - _Comment_: Predictable naming helps automation and reduces manual artifact selection mistakes.
- **Documentation/Wiki Sync**: Updated core documentation and wiki pages to match current v3 WPF runtime behavior and build pipeline.
  - _Comment_: This removes stale references and keeps operator/user guidance aligned with shipped behavior.
- **MSI Launch Flow**: Refined MSI launch and post-install behavior, including VBScript launcher integration and launch control adjustments.
  - _Comment_: Improves post-install UX and reduces launch edge cases after setup completes.
- **Build/Release Pipeline**: Updated build and release scripts to support force-rebuild scenarios, optional no-restart driver install paths, and clearer process/retry handling.
  - _Comment_: These changes make CI and local release execution more resilient to transient failures.
- **Config/Update Defaults**: Added normalization for legacy update repository settings and improved startup/default-config save behavior.
  - _Comment_: Backward compatibility is improved for users carrying older config values.
- **UI/UX Iterations**: Improved theme brush initialization/transparency behavior, expanded TypeThing/schedule settings surfaces, and removed redundant apply-step interactions through auto-apply.
- **Mini Widget Configuration Flow**: Extended mini widget settings to persist a named preset (`Custom`, `Focus`, `Meeting`, `BatterySafe`) alongside individual widget options.
  - _Comment_: This streamlines settings interaction and reduces friction in common configuration flows.
- **Mini Widget Preset Revert Logic**: Manual edits to preset-managed mini widget controls now automatically switch the preset back to `Custom`.
  - _Comment_: Prevents stale preset labels when users intentionally diverge from preset defaults.

### Documentation (Unreleased)

- **Config Persistence Docs**: Updated docs/wiki references from file-only config to registry-first persistence (`HKCU\Software\Redball\UserData`) with `%LocalAppData%\Redball\UserData\Redball.json` file copy.
  - _Comment_: Matches the current durability implementation and avoids misleading reset/export guidance.
- **Strategic Assessment Artifact**: Added `improve_me.txt` with an architecture/product scorecard, 10/10 requirement checklist, and implementation roadmap/snippets for priority improvements.
  - _Comment_: Captures actionable product and technical direction in a single planning reference for upcoming iterations.
- **Settings UX Docs**: Updated settings guidance to reflect auto-apply behavior instead of legacy explicit apply-button flow.
  - _Comment_: Prevents user confusion when following settings instructions in the current UI.
- **Startup & CLI Docs**: Replaced outdated startup argument guidance with current maintenance arguments (`--install-driver`, `--install-driver-no-restart`).
  - _Comment_: Keeps launch/runtime docs accurate for support and troubleshooting.
- **Build/Release Docs**: Added current `scripts/build.ps1` behavior details.
  - _Comment_: Improves release reproducibility and artifact discoverability.
- **Security/Privacy/DPA Docs**: Updated supported versions, data storage locations, and update network behavior to current build.
  - _Comment_: Aligns policy/operations docs with implemented runtime behavior.

### Fixed (Unreleased)

- **Missing Animation Assets**: Restored Lottie animation JSON files (`ram_usage.json`, `engine_toggle.json`, `typething_launch.json`, `pomodoro_timer.json`) to `Assets/Animations` and ensured they are included in the build output. Fixes `XamlParseException` / `DirectoryNotFoundException` on app startup.
- **Settings Reset Regression**: Prevented startup UI event handlers from overwriting loaded settings by ensuring initialization gating is active during control wiring.
  - _Comment_: Fixes a high-impact regression where defaults could silently overwrite saved user settings.
- **Config Loss During Upgrades**: Fixed MSI-upgrade-related settings loss by separating user data from installer-managed directories and adding migration from legacy locations.
  - _Comment_: User configuration now persists across major/minor installer upgrades as expected.
- **Input Interception Safety**: Fixed keyboard hook filter initialization to avoid intercepting physical keyboard input and added defensive hook cleanup on failed initialization.
  - _Comment_: Prevents keyboard lockup risk and improves safe recovery on hook initialization failures.
- **Tray/Window Lifecycle Stability**: Fixed duplicate subscriptions and resource leaks in tray icon/window lifecycle paths, including safer icon handle disposal and central cleanup.
  - _Comment_: Lowers long-session instability risk and avoids resource leakage in tray-first usage.
- **RDP/Hotkey Reliability**: Improved hotkey behavior in remote session scenarios.
  - _Comment_: Helps keep shortcuts consistent for users running Redball over remote desktop.
- **VirusTotal Artifact Filtering**: Fixed artifact name matching used in VirusTotal-related workflow filtering.
  - _Comment_: Ensures security scanning workflows target the intended release artifacts.
- **Mini Widget Preset Validation**: Added config validation and self-healing normalization for invalid mini widget preset values.
  - _Comment_: Prevents invalid preset values from breaking widget behavior after imports or manual edits.

### CI (Unreleased)

- **Workflow Modernization**: Updated GitHub Actions dependencies (`actions/checkout`, `setup-dotnet`, `cache`, `stale`, and `upload-pages-artifact`) to current major versions.
  - _Comment_: Keeps CI dependencies current and lowers maintenance/security risk from outdated actions.
- **Node Compatibility**: Added Node.js 24 opt-in coverage and fixed Node deprecation fallout across workflows.
  - _Comment_: Prevents future breakage from platform deprecations in GitHub-hosted runners.
- **Version Source Consistency**: Updated CI version extraction to read from the WPF project metadata instead of the legacy PowerShell path.
  - _Comment_: Establishes a single version source of truth for modern WPF release pipelines.
- **Pipeline Cleanup**: Removed legacy Pester-focused workflow steps and aligned CI with the WPF-first build/test pipeline.
  - _Comment_: Reduces noise in CI and keeps automation aligned with the current product architecture.

## [3.0.0] - 2026-03-13

### Changed (3.0.0)

- **Pure WPF Architecture**: Migrated all functionality from PowerShell script to native C# WPF application. The WPF exe is now fully self-contained with no PowerShell dependency.
  - _Comment_: This marks the platform transition from script-driven runtime to a compiled desktop application model.
- **Removed IPC Layer**: Eliminated named pipe communication between WPF UI and PowerShell backend. All state management now happens directly in C#.
  - _Comment_: Removing cross-process messaging simplifies state flow and reduces synchronization failure points.
- **Direct Win32 API**: Keep-awake engine uses `SetThreadExecutionState` and `SendInput` (F15 heartbeat) via P/Invoke in `NativeMethods.cs`.
  - _Comment_: Native calls provide tighter control and lower overhead for core keep-awake behavior.

### Added (3.0.0)

- **KeepAwakeService**: Core keep-awake engine with `SetThreadExecutionState`, F15 heartbeat via `SendInput`, timed sessions, and auto-pause/resume tracking.
  - _Comment_: Centralizing this logic enables consistent keep-awake behavior across UI and automation paths.
- **BatteryMonitorService**: WMI-based battery monitoring with configurable threshold and 60-second cache.
  - _Comment_: Cached reads reduce polling overhead while still supporting low-battery safeguards.
- **NetworkMonitorService**: Network connectivity monitoring via `System.Net.NetworkInformation`.
  - _Comment_: Connectivity state is now first-class for network-aware behavior and future policy rules.
- **IdleDetectionService**: User idle detection via `GetLastInputInfo` P/Invoke.
  - _Comment_: Enables idle-aware automation without relying on coarse timer-only assumptions.
- **ScheduleService**: Time/day-based scheduled activation and deactivation.
  - _Comment_: Adds predictable automation windows for users with fixed working routines.
- **PresentationModeService**: Auto-detect PowerPoint, Teams screen sharing, and Windows presentation mode.
  - _Comment_: Reduces interruption risk during meetings and presentation scenarios.
- **SessionStateService**: Save/restore session state (`Redball.state.json`) across application restarts.
  - _Comment_: Improves continuity so sessions survive app restarts and crash-recovery flows.
- **StartupService**: Windows startup registration via Registry Run key.
  - _Comment_: Provides native startup integration without requiring user-managed shortcuts.
- **SingletonService**: Named mutex (`Global\Redball_Singleton_Mutex`) to prevent multiple instances.
  - _Comment_: Prevents duplicate app instances from conflicting over hotkeys, tray state, and config writes.
- **CrashRecoveryService**: Crash flag file detection with safe-defaults recovery.
  - _Comment_: Improves resilience by detecting unclean exits and restoring a safe launch state.
- **NotificationService**: Centralized tray balloon notifications with configurable notification mode filtering.
  - _Comment_: Unifies user messaging and enables per-mode noise control.
- **LocalizationService**: Built-in locales (en, es, fr, de, bl) with external `locales.json` override support.
  - _Comment_: Supports both bundled translations and external locale overrides for customization.
- **TelemetryService**: Opt-in local telemetry event logging.
  - _Comment_: Adds diagnostics visibility while keeping telemetry user-controlled.
- **NativeMethods.cs**: Centralized P/Invoke declarations for kernel32 and user32.
  - _Comment_: Consolidating interop declarations improves maintainability and reduces duplication.
- **Config Export/Import**: `ConfigService.Export()` and `ConfigService.Import()` for settings backup and restore.
  - _Comment_: Makes configuration portability and recovery easier for support and power users.
- **ReloadConfig**: `KeepAwakeService.ReloadConfig()` called after settings save so monitors pick up changes immediately.
  - _Comment_: Avoids stale runtime behavior after settings updates.

### Removed (3.0.0)

- **IpcClientService.cs**: Named pipe client for PowerShell communication (no longer needed).
  - _Comment_: This became obsolete after consolidating behavior into the native WPF process.
- **System.IO.Pipes dependency**: Removed from project file.
  - _Comment_: Dependency removal reflects the IPC layer deprecation and reduces package surface area.
- **PowerShell backend dependency**: The WPF application no longer requires `Redball.ps1` to be running.
  - _Comment_: Users now run a single app process instead of coordinating separate frontend/backend runtimes.

## [2.1.1] - 2026-03-11

### Security (2.1.1)

- **TLS 1.2+ Enforcement**: All HTTPS requests now enforce TLS 1.2/1.3 to prevent protocol downgrade attacks
  - _Comment_: This hardens update/network paths against legacy TLS downgrade risks.
- **Update Repo Validation**: `Get-RedballLatestRelease` validates repo owner/name against injection patterns and warns on non-default repos
  - _Comment_: Validation narrows the trusted update source and reduces misconfiguration risk.
- **Renamed -Encrypt to -Obfuscate**: `Export-RedballSettings`/`Import-RedballSettings` parameters renamed to avoid misleading users about Base64 security
  - _Comment_: The naming now accurately reflects behavior and avoids implying cryptographic protection.

### Fixed (2.1.1)

- **TypeThing Retry Timer Closure Bug**: Timer retry variables now use `$script:` scope so they persist across tick events
  - _Comment_: Scope correction prevents retry state from resetting unexpectedly between timer callbacks.
- **Locale Detection**: Replaced broken `$env:LANG` with `(Get-Culture).TwoLetterISOLanguageName` for reliable system locale detection
  - _Comment_: Locale detection now uses a Windows-consistent API for dependable language selection.
- **Empty Catch Blocks**: All silent `catch {}` blocks now log to DEBUG/WARN level for diagnosability
  - _Comment_: Error visibility is improved without changing user-facing behavior.
- **Form Disposal**: `Show-RedballSettings` now disposes the form in a `finally` block to prevent GDI leaks on error
  - _Comment_: Ensures UI resources are released even on failure paths.
- **Temp File Cleanup**: `Install-RedballUpdate` removes the downloaded temp file after installation
  - _Comment_: Prevents leftover installer files from accumulating in temporary storage.
- **Path Consistency**: Replaced all `$PSScriptRoot` references in function bodies with `$script:AppRoot`
  - _Comment_: Standardized path resolution reduces context-dependent file lookup issues.
- **Idle Detection Text**: Menu item and settings label now reflect actual threshold instead of hardcoded "30min"
  - _Comment_: UI text now correctly reflects configured runtime behavior.

### Removed (2.1.1)

- **Duplicate Settings Dialog**: Removed dead `Show-RedballSettingsDialog` function (superseded by `Show-RedballSettings`)
  - _Comment_: Eliminates unused UI path and reduces maintenance burden.

### Improved (2.1.1)

- **Update Rate Limiting**: `Get-RedballLatestRelease` caches results for 5 minutes to prevent GitHub API rate limiting
  - _Comment_: Cuts unnecessary outbound requests and avoids API throttling.
- **Battery Query Throttling**: `Get-BatteryStatus` cache TTL increased from 30s to 60s to reduce CIM overhead
  - _Comment_: Reduces repeated system query load during long-running sessions.
- **Presentation Mode Throttling**: `Test-PresentationMode` caches results for 10 seconds to avoid expensive process scans every tick
  - _Comment_: Keeps presentation detection responsive while lowering scan frequency.
- **Log Rotation Throttling**: Log file size check now runs every 50 writes instead of every write
  - _Comment_: Decreases file I/O overhead in high-frequency logging paths.
- **Add-Type Gating**: `Test-HighContrastMode` and `Enable-HighDPI` skip C# compilation when types already loaded
  - _Comment_: Avoids repeated compilation work in repeated initialization paths.
- **ES Constants Scoping**: `ES_CONTINUOUS`, `ES_SYSTEM_REQUIRED`, `ES_DISPLAY_REQUIRED` use `$script:` prefix
  - _Comment_: Explicit scoping prevents accidental shadowing and improves script predictability.
- **Named Hotkey Constants**: Replaced magic numbers `100`/`101` with `$script:HOTKEY_ID_TYPETHING_START`/`STOP`
  - _Comment_: Named constants improve readability and reduce hotkey ID misuse.
- **Large Clipboard Threshold**: Configurable via `TypeThingLargeClipboardThreshold` instead of hardcoded `10000`
  - _Comment_: Users can tune behavior for different clipboard sizes and workflows.
- **Runspace Hex Comment**: Documented `0x80000003` ES flags in keep-awake runspace
  - _Comment_: Adds technical clarity for maintainers touching execution-state logic.
- **TypeThing Disabled Status**: Menu shows "Status: Disabled" when TypeThing is off
  - _Comment_: Improves state visibility directly from the tray/menu surface.
- **Locale Sync**: Added 'bl' (hacker) locale to both `locales.json` and settings dropdown
  - _Comment_: Keeps locale source data and UI selection list aligned.
- **Renamed Telemetry**: `Send-RedballTelemetry` → `Write-RedballTelemetryEvent` to clarify local-only logging
  - _Comment_: Function intent is clearer and better aligned with PowerShell verb conventions.
- **Test Safety Comment**: Documented `Invoke-Expression` usage in test file AST loader
  - _Comment_: Clarifies why dynamic invocation appears in tests and reduces false-positive security concerns.
- **State/Config Duplication**: Documented the dual-store pattern for future refactoring
  - _Comment_: Improves onboarding context for future cleanup work in persistence architecture.

## [2.1.0] - 2026-03-11

### Added (2.1.0)

- **Config Validation**: `Test-RedballConfigSchema` validates and sanitizes all config values against expected types, ranges, and formats on startup
  - _Comment_: Establishes strong input hygiene early in app startup.
- **First-Run Onboarding**: Welcome toast notification on first launch with guidance for new users
  - _Comment_: Improves discoverability and reduces first-use confusion.
- **Crash Reporting**: Detailed crash reports with stack traces written to `Redball.crash.log` for both PowerShell and WinForms exceptions
  - _Comment_: Better crash diagnostics shortens issue triage time.
- **Feature Usage Analytics**: Opt-in per-session feature usage counters logged at shutdown (local only, never transmitted)
  - _Comment_: Helps guide product decisions while preserving privacy boundaries.
- **User Error Helper**: `Show-RedballError` centralizes user-friendly error display via toast notifications
  - _Comment_: Standardized error UX improves consistency and clarity.
- **Copyright Headers**: Added copyright and license headers to all source files
  - _Comment_: Improves legal clarity across distributed source artifacts.
- **ROADMAP.md**: Formal product roadmap with milestones, user personas, competitive analysis, and value proposition
  - _Comment_: Gives contributors and users clearer strategic context.
- **SECURITY.md**: Security policy with vulnerability reporting process, threat model, and security features documentation
  - _Comment_: Establishes a clear path for responsible disclosure and security practices.
- **PRIVACY.md**: Privacy policy documenting local-only data handling, network requests, and user rights
  - _Comment_: Increases transparency on data usage and storage behavior.
- **CODE_OF_CONDUCT.md**: Contributor Covenant code of conduct for community standards
  - _Comment_: Sets collaboration expectations for community health.
- **THIRD-PARTY-NOTICES.md**: Complete third-party license attribution for all dependencies
  - _Comment_: Improves compliance and attribution hygiene.
- **PS7 CI Matrix**: CI pipeline now tests on both PowerShell 5.1 and PowerShell 7
  - _Comment_: Broadens compatibility verification across common PowerShell environments.
- **Code Coverage Reporting**: CI pipeline reports code coverage percentage with threshold warnings
  - _Comment_: Improves visibility into test quality over time.

### Fixed (2.1.0)

- **TypeThing SendInput Bug**: Fixed PowerShell nested value type copy issue where `$input.ki.wVk = value` silently modified a copy instead of the original struct — this was the root cause of typing producing no output
  - _Comment_: This resolves a core functional bug where simulated typing appeared to run but produced no characters.
- **INPUT Struct Alignment**: Fixed 64-bit struct layout (`FieldOffset` 4→8, size 28→40) for correct SendInput marshaling on 64-bit Windows
  - _Comment_: Correct layout is critical for reliable Win32 interop on x64 systems.
- **Hotkey Debug Logging**: Added Win32 error codes and parsed VK values to hotkey registration failure messages
  - _Comment_: Enhances diagnosability for environment-specific hotkey failures.

### Security (2.1.0)

- **Input Sanitization**: All string config values are stripped of control characters on load
  - _Comment_: Reduces risk from malformed or maliciously crafted config content.
- **Range Validation**: All numeric config values are clamped to safe ranges
  - _Comment_: Prevents out-of-bound values from destabilizing runtime behavior.
- **Enum Validation**: UpdateChannel and TypeThingTheme values validated against allowed sets
  - _Comment_: Blocks invalid enum states from propagating into business logic.
- **Schedule Format Validation**: ScheduleStartTime/ScheduleStopTime validated against HH:mm format
  - _Comment_: Avoids schedule parsing errors and ambiguous time input.

## [2.0.0] - 2024-03-09

### Added (2.0.0)

- **Branding**: "Redball" with new red ball icon
  - _Comment_: Establishes the product identity used across UI and release assets.
- **3D Icon**: Custom-drawn 3D red sphere with specular highlight and shadow effects
  - _Comment_: Improves visual distinctiveness in tray and desktop contexts.
- **Color States**: Three distinct icon states:
  - _Comment_: State-specific iconography improves at-a-glance status recognition.
  - Active: Bright red ball (crimson/tomato gradient)
  - Timed: Orange/red ball (dark orange gradient)
  - Paused: Dark red/gray ball (muted colors)
- **Configuration File Support**: JSON-based configuration with `Redball.json`
  - _Comment_: Introduces persistent settings management for repeatable behavior.
- **Structured Logging**: `Write-RedballLog` function with log rotation at 10MB
  - _Comment_: Enables maintainable diagnostics without unbounded log growth.
- **Pester Tests**: Comprehensive test suite covering 40+ test cases
  - _Comment_: Provides foundational regression coverage for core script behavior.
- **Graceful Shutdown**: Pipeline stop exception handling for Ctrl+C/terminal close
  - _Comment_: Improves reliability during forced or user-initiated termination.
- **Error Handling**: `PipelineStoppedException` catches throughout all functions
  - _Comment_: Prevents noisy termination errors from propagating to end users.
- **Memory Management**: Proper disposal of GDI+ objects and previous icons
  - _Comment_: Reduces risk of handle leaks during long-running tray sessions.
- **Parameter Validation**: `[ValidateRange(1, 720)]` for timer duration
  - _Comment_: Prevents invalid runtime parameters before execution begins.
- **Help Documentation**: Full PowerShell help with `.SYNOPSIS`, `.DESCRIPTION`, `.PARAMETER`, `.EXAMPLE`
  - _Comment_: Improves self-service onboarding and script discoverability.
- **Trap Handler**: Global trap for `PipelineStoppedException` with graceful exit
  - _Comment_: Adds a final safety net for pipeline interruption scenarios.

### Changed (2.0.0)

- **Icon System**: Replaced coffee cup design with 3D red ball
  - _Comment_: Aligns visuals with the Redball branding refresh.
- **Function Names**: Renamed `Update-Ui` to `Update-RedballUI`
  - _Comment_: Makes naming more explicit and product-specific.
- **Log Files**: Changed from `Redball.log` to `Redball.log`
  - _Comment_: Normalizes naming consistency across docs and script output paths.
- **Config Files**: Changed from `Redball.json` to `Redball.json`
  - _Comment_: Keeps configuration naming aligned with product naming.
- **UI Text**: Updated all references from "Redball" to "Redball"
  - _Comment_: Removes mixed branding terminology in user-facing text.
- **Tray Tooltip**: Now shows `[REDBALL] Redball`
  - _Comment_: Improves tray tooltip clarity and app identification.

### Fixed (2.0.0)

- **Pipeline Stop Error**: No more "pipeline stopped" exception on external termination
  - _Comment_: Eliminates a common noisy error during normal shutdown paths.
- **$PSScriptRoot Empty**: Added fallback to current directory when `$PSScriptRoot` is empty
  - _Comment_: Improves execution reliability in non-standard launch contexts.
- **Memory Leaks**: Proper disposal of `PreviousIcon` prevents GDI+ handle leaks
  - _Comment_: Prevents gradual resource exhaustion in long-lived sessions.
- **UInt32 Overflow**: Fixed constant definitions to prevent signed integer overflow
  - _Comment_: Avoids subtle constant-related runtime defects.
- **DateTime Nullability**: Used `[Nullable[datetime]]` for proper null handling
  - _Comment_: Improves correctness for optional time-based state values.

### Security (2.0.0)

- **Execution Policy**: Requires `-Version 5.1` and administrative privileges
  - _Comment_: Enforces baseline runtime prerequisites for predictable behavior.
- **Error Suppression**: Sensitive error details only logged, not displayed to user
  - _Comment_: Reduces inadvertent disclosure of sensitive runtime details.

## [1.0.0] - 2024-03-01

### Added (1.0.0)

- Initial release as "Redball"
  - _Comment_: Establishes the first publicly tracked baseline.
- System tray icon with basic functionality
  - _Comment_: Introduces persistent background control via tray UX.
- Keep-awake state using `SetThreadExecutionState` API
  - _Comment_: Provides the core anti-sleep capability.
- F15 heartbeat keypress
  - _Comment_: Adds periodic synthetic input for environments requiring activity signals.
- Duration timer (15, 30, 60, 120 minutes)
  - _Comment_: Enables bounded sessions instead of only manual toggling.
- Prevent display sleep toggle
  - _Comment_: Gives users explicit control over monitor sleep behavior.
- Basic context menu with pause/resume
  - _Comment_: Delivers the primary interaction model for the initial release.

## GitHub Tag History

### Added (GitHub)

- **v2.0.x Tags**: v2.0.0, v2.0.1, v2.0.2, v2.0.3, v2.0.4, v2.0.5, v2.0.6, v2.0.7, v2.0.8, v2.0.9, v2.0.10, v2.0.11, v2.0.12, v2.0.13, v2.0.14, v2.0.15, v2.0.16, v2.0.17, v2.0.18, v2.0.19, v2.0.20, v2.0.21, v2.0.22, v2.0.23, v2.0.24, v2.0.25, v2.0.26, v2.0.27, v2.0.28, v2.0.29, v2.0.30, v2.0.31, v2.0.32, v2.0.33, v2.0.34, v2.0.35, v2.0.36, v2.0.37, v2.0.38, v2.0.39, v2.0.40, v2.0.41, v2.0.42.
- **v2.1.x Tags**: v2.1.1, v2.1.2, v2.1.3, v2.1.4, v2.1.5, v2.1.11, v2.1.14, v2.1.15, v2.1.16, v2.1.17, v2.1.18, v2.1.19, v2.1.20, v2.1.21, v2.1.22, v2.1.25, v2.1.26, v2.1.27, v2.1.28, v2.1.30, v2.1.31, v2.1.32, v2.1.33, v2.1.34, v2.1.35, v2.1.37, v2.1.43, v2.1.44, v2.1.45, v2.1.47, v2.1.48, v2.1.49, v2.1.50, v2.1.52, v2.1.53, v2.1.54, v2.1.70, v2.1.73, v2.1.80, v2.1.82, v2.1.83, v2.1.84, v2.1.85, v2.1.86, v2.1.87, v2.1.88, v2.1.89, v2.1.90, v2.1.91, v2.1.92, v2.1.93, v2.1.94, v2.1.96, v2.1.97, v2.1.105, v2.1.106, v2.1.107, v2.1.109, v2.1.110, v2.1.115, v2.1.117, v2.1.120, v2.1.121, v2.1.122, v2.1.123, v2.1.125, v2.1.126, v2.1.127, v2.1.128, v2.1.129, v2.1.130, v2.1.131, v2.1.133, v2.1.134, v2.1.136, v2.1.137, v2.1.141, v2.1.142, v2.1.143, v2.1.144, v2.1.145, v2.1.146, v2.1.147, v2.1.148, v2.1.149, v2.1.150, v2.1.151, v2.1.152, v2.1.153, v2.1.154, v2.1.155, v2.1.156, v2.1.157, v2.1.158, v2.1.159, v2.1.160, v2.1.161, v2.1.162, v2.1.163, v2.1.165, v2.1.166, v2.1.172, v2.1.173, v2.1.174, v2.1.175, v2.1.176, v2.1.177, v2.1.178, v2.1.179, v2.1.180, v2.1.181, v2.1.183, v2.1.184, v2.1.185, v2.1.187, v2.1.188, v2.1.189, v2.1.190, v2.1.191, v2.1.192, v2.1.193, v2.1.194, v2.1.195, v2.1.196, v2.1.198, v2.1.199, v2.1.200, v2.1.202, v2.1.203, v2.1.205, v2.1.206, v2.1.207, v2.1.208, v2.1.211, v2.1.212, v2.1.213, v2.1.214, v2.1.218, v2.1.219, v2.1.220, v2.1.221, v2.1.222, v2.1.223, v2.1.224, v2.1.225, v2.1.226.

[Unreleased]: https://github.com/ArMaTeC/redball/compare/v3.0.0...HEAD
[3.0.0]: https://github.com/ArMaTeC/redball/compare/v2.1.1...v3.0.0
[2.0.0]: https://github.com/ArMaTeC/redball/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/ArMaTeC/redball/releases/tag/v1.0.0
