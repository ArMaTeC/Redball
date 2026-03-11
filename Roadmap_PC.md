# Redball — Comprehensive Static Analysis & Implementation Roadmap

> Generated: 2026-03-11 | Analyst: Senior Full-Stack Architect, Security Auditor & QA Lead
> Codebase: `Redball.ps1` (5,471 lines), `Redball.Tests.ps1` (803 lines), CI/CD workflows, installer, supporting files

---

## Analysis Summary

| Phase | Findings | Critical | High | Medium | Polish |
| ----- | -------- | -------- | ---- | ------ | ------ |
| 1. Structural Integrity | 14 | 1 | 5 | 5 | 3 |
| 2. Security & Data Integrity | 8 | 2 | 3 | 2 | 1 |
| 3. UI/UX & Accessibility | 11 | 0 | 4 | 5 | 2 |
| 4. Performance & Optimization | 7 | 0 | 1 | 4 | 2 |
| 5. Uniformity & Code Quality | 10 | 0 | 2 | 5 | 3 |
| **TOTAL** | **50** | **3** | **15** | **21** | **11** |

---

## Phase 1: Structural Integrity & Logic Deep Scan

### 1.1 Missing Definitions & Ghost Calls

No ghost calls found. All function calls resolve to definitions within `Redball.ps1`. Functions like `Get-ClipboardText`, `Get-RedballStatus`, `Switch-ActiveState`, `Set-ActiveState`, `Start-TimedAwake`, `Update-BatteryAwareState`, `Update-NetworkAwareState`, `Update-IdleAwareState`, `Update-ScheduleState`, `Update-PresentationModeState`, `Save-RedballState`, `Restore-RedballState`, `Test-RedballStartup`, `Install-RedballStartup`, `Uninstall-RedballStartup`, and `Get-IdleTimeMinutes` are all defined within the script.

### 1.2 Duplicate / Dead Code

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 1 | Duplicate Settings Dialog | `Redball.ps1:4925-5119` (`Show-RedballSettingsDialog`) vs `Redball.ps1:1390-1776` (`Show-RedballSettings`) | Two completely separate settings dialogs exist. `Show-RedballSettingsDialog` is a simpler/older version that doesn't handle TypeThing settings, locale changes, or hotkey re-registration. It is never called from any menu item — only `Show-RedballSettings` is wired up. | Remove `Show-RedballSettingsDialog` entirely (lines 4925-5119) as dead code. If kept for CLI use, rename to `Show-RedballSettingsDialogLegacy` and document. | Logic | **[HIGH]** |
| 2 | Version Mismatch in Script Header | `Redball.ps1:38` (`.NOTES Version: 2.0.29`) vs `Redball.ps1:84` (`$script:VERSION = '2.0.42'`) | The comment-based help `.NOTES` section says `Version: 2.0.29` but the actual runtime version is `2.0.42`. This misleads anyone reading the script header. | Update the `.NOTES` version to match `$script:VERSION`, or better, remove the hardcoded version from `.NOTES` and reference `$script:VERSION` in documentation. | Logic | **[MEDIUM]** |

### 1.3 Swallowed Errors (Empty Catch Blocks)

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 3 | Empty catch in `Unregister-GlobalHotkey` | `Redball.ps1:3666` | `catch {}` completely swallows hotkey unregistration errors. If unregistration fails, the hotkey may remain registered to a dead window handle, causing the hotkey to stop working system-wide until reboot. | Add `Write-RedballLog -Level 'DEBUG' -Message "Hotkey unregister skipped: $_"` | Logic | **[HIGH]** |
| 4 | Empty catch in crash report writer | `Redball.ps1:4023` | If crash report writing fails inside the global exception handler, the error is completely lost. The original exception AND the reporting failure are both silently discarded. | Add minimal fallback: `Write-Verbose "Crash report write failed: $_"` | Logic | **[MEDIUM]** |
| 5 | Empty catch in `Show-RedballError` | `Redball.ps1:1079` | The centralized error display helper swallows its own errors. If `ShowBalloonTip` fails, the user gets no feedback at all. | Add `Write-RedballLog -Level 'DEBUG' -Message "Balloon tip failed: $_"` | Logic | **[POLISH]** |
| 6 | 5× Empty catch on `ShowBalloonTip` calls | `Redball.ps1:2240,2280,2412,2454` | TypeThing notification balloon tips have empty catch blocks. While individually low-risk, a systematic failure (e.g., NotifyIcon disposed) would be invisible. | Add DEBUG-level logging to all five instances. | Logic | **[POLISH]** |

### 1.4 Closure Scope Bug

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 7 | TypeThing retry timer `$retryCount` closure | `Redball.ps1:5439-5453` | `$retryCount` is declared outside the timer tick handler and incremented inside it. In PowerShell, the timer closure captures the *variable* (not the value), and `$retryCount++` on a local copy may not persist across ticks depending on PS version. The `$maxRetries` variable has the same issue. | Convert to `$script:typeThingRetryCount` or use a reference type (e.g., hashtable `$retryState = @{ Count = 0 }`) to ensure the counter persists across tick invocations. | Logic | **[HIGH]** |

### 1.5 Locale Detection Bug

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 8 | `$env:LANG` used for locale detection | `Redball.ps1:652` | `Import-RedballLocales` uses `$env:LANG` (a Unix convention) to detect the system locale. On Windows, `$env:LANG` is typically `$null`. The config `Locale` defaults correctly via `(Get-Culture).TwoLetterISOLanguageName` on line 200, but the locale loader function's own detection is broken. | Replace `($env:LANG -split '_')[0]` with `(Get-Culture).TwoLetterISOLanguageName` or use `$script:config.Locale` which is already set correctly. | Logic | **[HIGH]** |

### 1.6 Embedded vs External Locale Mismatch

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 9 | `bl` locale missing from `locales.json` | `Redball.ps1:578-609` (embedded) vs `locales.json` | Embedded locales include `bl` (hacker locale) but `locales.json` only has `en/es/fr/de`. The Settings dialog locale dropdown also only lists `en/es/fr/de`. Users cannot select the `bl` locale through the UI. | Add `bl` locale to `locales.json` and add `bl` to the locale dropdown options in `Show-RedballSettings`. | Logic | **[MEDIUM]** |

### 1.7 Global Error Handling

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 10 | Global exception handler present | `Redball.ps1:3994-4026` | ✅ Global `ThreadException` handler exists and correctly routes to crash report. `trap` statement handles `PipelineStoppedException`. `ProcessExit` event clears crash flag. This is well-implemented. | No action needed. | Logic | — |

### 1.8 Incomplete Logic

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 11 | `Update-DarkModeUI` is a no-op | `Redball.ps1:4907-4921` | Function detects dark mode and logs it but doesn't actually apply any dark-mode styling to the UI. It returns `$isDarkMode` but no caller uses the return value. | Either implement actual dark mode application to WinForms controls, or remove the function and replace with `Test-DarkMode` usage where needed. | Logic | **[MEDIUM]** |
| 12 | `Update-HighContrastUI` sets non-existent config key | `Redball.ps1:4844` | Sets `$script:config.UseSystemIcons = $true` but `UseSystemIcons` is never defined in the config hashtable and is never checked by `Get-CustomTrayIcon`. | Add `UseSystemIcons` to default config, and add a check in `Get-CustomTrayIcon` to return system icons when enabled. | Logic | **[MEDIUM]** |
| 13 | `Send-RedballTelemetry` is a stub | `Redball.ps1:5123-5147` | Comment says "in a real implementation, this would send to analytics endpoint" — it only logs locally. This is fine for privacy, but the function name is misleading. | Rename to `Write-RedballTelemetryEvent` to clarify it's local-only, or document clearly that telemetry is local logging only. | Logic | **[POLISH]** |
| 14 | `Export-RedballSettings -Encrypt` is not encryption | `Redball.ps1:4725-4729` | Comment acknowledges it's "Simple obfuscation (not true encryption)" — just base64 encoding. The `-Encrypt` parameter name misleads users into thinking data is secured. | Rename parameter from `-Encrypt` to `-Obfuscate` and update documentation. Or implement actual encryption using `ConvertTo-SecureString` / DPAPI. | Logic | **[HIGH]** |

---

## Phase 2: Security & Data Integrity

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 15 | Update repo owner/name are user-editable | `Redball.ps1:1614-1617`, `Redball.ps1:220-221` | `UpdateRepoOwner` and `UpdateRepoName` are freely editable in both config JSON and the Settings UI. A malicious actor with config file access could redirect auto-updates to a trojan repository. | Add validation that update repo URLs resolve to `github.com` only. Optionally, hardcode the repo owner/name and only allow override via `-UpdateRepoOwner` CLI parameter (not config file). Add a warning in the UI when these values differ from defaults. | Security | **[CRITICAL]** |
| 16 | No TLS version enforcement on update downloads | `Redball.ps1:4672` | `Invoke-WebRequest` and `Invoke-RestMethod` for update checks/downloads don't enforce TLS 1.2+. On older systems, PS may default to TLS 1.0/1.1, which are vulnerable to downgrade attacks. | Add `[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12` before any network calls. | Security | **[CRITICAL]** |
| 17 | Base64 "encryption" misleads users | `Redball.ps1:4725-4729` | `Export-RedballSettings -Encrypt` uses base64 encoding only. Anyone can decode it with a one-liner. The parameter name creates a false sense of security. | (Same as #14) Rename to `-Obfuscate` or implement DPAPI via `[System.Security.Cryptography.ProtectedData]`. | Security | **[HIGH]** |
| 18 | Config file not integrity-checked on load | `Redball.ps1:794-821` | `Import-RedballConfig` loads JSON directly without checking file integrity. A tampered config file could set malicious `UpdateRepoOwner` values or extreme `HeartbeatSeconds` values (mitigated by `Test-RedballConfigSchema` but only for range, not tampering). | Optionally compute and store a SHA-256 hash alongside the config. On load, verify the hash matches. This detects manual tampering. | Security | **[MEDIUM]** |
| 19 | Log file may expose full file paths | `Redball.ps1` (throughout) | Log messages include full file paths (e.g., `"Configuration loaded from: C:\Users\karl\..."`, `"Backup: C:\Users\karl\..."`) which could expose the username and directory structure if logs are shared for troubleshooting. | Add a log sanitization helper that replaces `$env:USERPROFILE` with `~` and `$env:TEMP` with `%TEMP%` before writing. | Security | **[MEDIUM]** |
| 20 | `Invoke-Expression` in test file | `Redball.Tests.ps1:24` | `Invoke-Expression $functionAst.Extent.Text` is used to load functions from the AST. While legitimate for testing (loading functions without running the full script), `Invoke-Expression` is inherently dangerous and flagged by security scanners. | Already excluded from CI security scan (correct). Add a comment explaining why this is safe in the test context. | Security | **[POLISH]** |
| 21 | No rate limiting on update checks | `Redball.ps1:4591-4610` | `Get-RedballLatestRelease` hits the GitHub API with no caching or rate limiting. Rapid repeated clicks on "Check for Updates" could trigger GitHub API rate limits (60 req/hr unauthenticated). | Cache the last check result with a 5-minute TTL. Return cached result if checked within the TTL window. | Security | **[HIGH]** |
| 22 | Downloaded update temp file not cleaned up | `Redball.ps1:4668-4682` | `Install-RedballUpdate` downloads to `$env:TEMP` but never deletes the temp file after successful installation. Over time, multiple update temp files accumulate. | Add `Remove-Item $tempPath -Force -ErrorAction SilentlyContinue` after successful copy. | Security | **[HIGH]** |

---

## Phase 3: UI/UX & Accessibility

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 23 | Two settings dialogs confuse codebase | `Redball.ps1:1390` vs `Redball.ps1:4925` | `Show-RedballSettings` (polished, tabbed, complete) and `Show-RedballSettingsDialog` (simpler, incomplete) both exist. Only the former is wired to the menu. The latter is dead code that could confuse contributors. | Remove `Show-RedballSettingsDialog` or clearly mark as deprecated/legacy. | UI/UX | **[HIGH]** |
| 24 | About dialog ignores system theme | `Redball.ps1:1794-1795` | About dialog hardcodes dark theme (`BackColor = FromArgb(30,30,30)`, `ForeColor = White`). On a system using light mode, this clashes with system appearance. | Use `Test-DarkMode` to conditionally apply light/dark colors to the About dialog. | UI/UX | **[MEDIUM]** |
| 25 | Main Settings dialog hardcodes light theme | `Redball.ps1:1411` | `Show-RedballSettings` hardcodes `BackColor = FromArgb(245, 245, 245)` (light gray). Doesn't respect system dark mode preference. | Apply system theme detection and use dark colors when appropriate. | UI/UX | **[MEDIUM]** |
| 26 | No loading indicator during update check | `Redball.ps1:1778-1836` (About dialog) | When the user clicks "Check for Updates", the UI freezes during the HTTP request (up to 15 seconds timeout) with no visual feedback. | Show "Checking..." text in the label immediately, then update on completion. Ideally use a background job or timer to avoid blocking the UI thread. | UI/UX | **[HIGH]** |
| 27 | Idle detection threshold hardcoded in UI text | `Redball.ps1:3797,5007` | Menu item says "Idle Detection (30min)" and description says "30 minutes" but config has `IdleThresholdMinutes = 30` as a configurable value. If the user changes the threshold, the UI text remains "30min". | Update the menu text dynamically: `"Idle Detection ($($script:config.IdleThresholdMinutes)min)"`. | UI/UX | **[MEDIUM]** |
| 28 | No exit confirmation dialog | `Redball.ps1:5261-5269` | Clicking "Exit" immediately shuts down. No confirmation if TypeThing is actively typing or if a timed session is running. | Add a `MessageBox::Show` confirmation when TypeThing is typing or a timer is active. | UI/UX | **[MEDIUM]** |
| 29 | `bl` locale not selectable in UI | `Redball.ps1:1543` | Locale dropdown options are `@('en', 'es', 'fr', 'de')` but embedded locales include `bl`. | Add `'bl'` to the locale dropdown items array. | UI/UX | **[POLISH]** |
| 30 | Settings dialog forms not disposed on error | `Redball.ps1:1773` | `$form.Dispose()` is only called on line 1771 inside the try block after `ShowDialog` returns. If an exception occurs during dialog construction (before `ShowDialog`), the form is never disposed. | Wrap in `try/finally { if ($form) { $form.Dispose() } }` pattern. | UI/UX | **[MEDIUM]** |
| 31 | Hardcoded pixel positions in dialogs | `Redball.ps1:4930-5071` (legacy dialog), `Redball.ps1:1787-1836` (About) | All UI elements use absolute pixel coordinates (`New-Object System.Drawing.Point(170, 13)`). This breaks on high-DPI systems or when system font size is changed. | Use `TableLayoutPanel` or `FlowLayoutPanel` for automatic layout, or scale coordinates by DPI factor. The main settings dialog (`Show-RedballSettings`) already handles this better with `AutoScroll` panels. | UI/UX | **[HIGH]** |
| 32 | Accessible names and descriptions | `Redball.ps1:3682-3714` | ✅ Context menu items have `AccessibleName` and `AccessibleDescription` set. Good accessibility practice. However, dialog controls (labels, textboxes, checkboxes) in settings dialogs lack `AccessibleName` properties. | Add `AccessibleName` to dialog input controls for screen reader support. | UI/UX | **[POLISH]** |
| 33 | No empty state for TypeThing status | `Redball.ps1:3857` | TypeThing status menu item shows "Status: Idle" but doesn't indicate when TypeThing is disabled entirely (e.g., if `TypeThingEnabled = false` but the menu was already built). | Show "Status: Disabled" when `TypeThingEnabled` is `$false`. | UI/UX | **[MEDIUM]** |

---

## Phase 4: Performance & Optimization

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 34 | GDI+ icon recreation on every state change | `Redball.ps1:1188-1282` | `Get-CustomTrayIcon` creates a full GDI+ bitmap with gradients, highlights, and shadows every time the icon state changes. While icon caching exists (`LastIconState` check in `Update-RedballUI`), the icon is recreated from scratch each time the state actually changes. | Pre-render all 3 icon states at startup and cache them. Return cached icons from `Get-CustomTrayIcon` instead of re-rendering. | Performance | **[MEDIUM]** |
| 35 | Battery check via CIM every second | `Redball.ps1:3452-3456,3952-3954` | When battery-aware mode is enabled, `Update-BatteryAwareState` calls `Get-CimInstance Win32_Battery` via the duration timer that ticks every 1 second. CIM queries are relatively expensive. The `$script:lastBatteryCheck` caching exists but should be verified it's actually throttling. | Ensure battery checks are throttled to once per 30-60 seconds using the existing `$script:lastBatteryCheck` timestamp. Verify the cache TTL is working. | Performance | **[HIGH]** |
| 36 | Presentation mode process scan every second | `Redball.ps1:4195-4218,3964-3966` | `Test-PresentationMode` uses `Get-Process -Name "POWERPNT"`, `Get-Process -Name "Teams"`, and registry reads every second when presentation detection is enabled. | Cache presentation mode state with a 10-second TTL. Process enumeration is expensive to run every second. | Performance | **[MEDIUM]** |
| 37 | `Add-Type` called repeatedly for helper classes | `Redball.ps1:4801-4827,4857-4881` | `Test-HighContrastMode` and `Enable-HighDPI` both compile C# types via `Add-Type` every time they're called. While `-ErrorAction SilentlyContinue` suppresses the "type already exists" error on subsequent calls, the compilation attempt still has overhead. | Move `Add-Type` calls to a one-time initialization block at script startup, or gate them with `if (-not ([type]::GetType('HighContrastHelper')))`. | Performance | **[MEDIUM]** |
| 38 | Log rotation check on every write | `Redball.ps1:730-736` | `Write-RedballLog` checks log file size via `Get-Item` on every single log write. With frequent logging (heartbeat, UI updates, battery checks), this adds filesystem overhead. | Check log size only every Nth write (e.g., every 100 writes) using a counter, or check once per minute using a timestamp. | Performance | **[MEDIUM]** |
| 39 | Runspace worker has no clean stop | `Redball.ps1:5184-5187` | `Start-KeepAwakeRunspace` creates an infinite `while($true)` loop. Stopping requires `$powershell.Stop()` which throws a `PipelineStoppedException` inside the runspace — this is the intended mechanism but could leave the runspace in an unclean state. | Add a `[Threading.CancellationToken]` or a shared `[ref]$running` variable that the loop checks. | Performance | **[POLISH]** |
| 40 | Embedded locale JSON parsed on every `Import-RedballLocales` call | `Redball.ps1:638` | `$script:embeddedLocales | ConvertFrom-Json` parses the 130-line JSON string from scratch every time locales are loaded. | Parse once at script initialization and store the result. Only re-parse if the external file changes. | Performance | **[POLISH]** |

---

## Phase 5: Uniformity & Code Quality

| # | Task Name | File/Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|---------------|-------------------|--------------|----------|----------|
| 41 | State/config duplication for identical keys | `Redball.ps1:137-193` (state), `Redball.ps1:197-242` (config) | `BatteryAware`, `BatteryThreshold`, `NetworkAware`, `IdleDetection`, `PreventDisplaySleep`, `UseHeartbeatKeypress`, `HeartbeatSeconds` all exist in BOTH `$script:state` and `$script:config` with manual sync code throughout. This dual-source-of-truth pattern leads to subtle bugs when one is updated but the other isn't. | Designate config as the single source of truth for settings. Have state reference config directly where possible. Remove duplicate keys from `$script:state` and update all readers to use `$script:config.BatteryAware` instead of `$script:state.BatteryAware`. | Cleanup | **[HIGH]** |
| 42 | Mixed path resolution: `$PSScriptRoot` vs `$script:AppRoot` | `Redball.ps1:4307,4339,4706,4746` vs `Redball.ps1:84-92` | Some functions use `$PSScriptRoot` directly (e.g., `Test-CrashRecovery`, `Clear-CrashFlag`, `Export-RedballSettings`) while the script defines `$script:AppRoot` specifically to handle edge cases (ps2exe, dot-sourcing). Using `$PSScriptRoot` directly bypasses these fixes. | Replace all `$PSScriptRoot` references in function bodies with `$script:AppRoot` for consistency. | Cleanup | **[HIGH]** |
| 43 | Magic numbers for hotkey IDs | `Redball.ps1:195-196` | `TypeThingHotkeyStartId = 100` and `TypeThingHotkeyStopId = 101` are magic numbers in the state hashtable. The global hotkey also uses a `$script:hotkeyId` that's presumably defined elsewhere. | Define these as named constants: `$script:HOTKEY_ID_TOGGLE = 1`, `$script:HOTKEY_ID_TYPETHING_START = 100`, `$script:HOTKEY_ID_TYPETHING_STOP = 101`. | Cleanup | **[POLISH]** |
| 44 | Magic number for large clipboard threshold | `Redball.ps1:2247` | `if ($text.Length -gt 10000)` — the 10,000 character threshold is a hardcoded magic number. | Move to config as `TypeThingLargeClipboardThreshold` or define as a named constant. | Cleanup | **[POLISH]** |
| 45 | Magic hex value in runspace | `Redball.ps1:5185` | `0x80000003` is used directly instead of the named constants `$ES_CONTINUOUS`, `$ES_SYSTEM_REQUIRED`, etc. | Use named constants or add a comment explaining the value. | Cleanup | **[POLISH]** |
| 46 | Hardcoded colors throughout dialogs | `Redball.ps1:1411,1664,1794,2460-2500` | Colors like `FromArgb(245, 245, 245)`, `FromArgb(30, 30, 30)`, `FromArgb(0, 120, 215)` are scattered across dialog code. The TypeThing settings dialog has a proper theme engine, but other dialogs don't use it. | Create a unified `$script:UITheme` hashtable (similar to `$script:TypeThingThemes`) and apply it across all dialogs. | Cleanup | **[MEDIUM]** |
| 47 | Inconsistent error output patterns | `Redball.ps1` (throughout) | Some functions use `Write-Warning` (e.g., `Stop-RedballProcess:381`), some use `Write-RedballLog` (majority), some use `Write-Error` (e.g., line 5355), and some use `Write-Output` (e.g., `Install-RedballUpdate:4664`). | Standardize on `Write-RedballLog` for internal logging and `Show-RedballError` for user-facing messages. Reserve `Write-Error`/`Write-Output` for CLI-only code paths. | Cleanup | **[MEDIUM]** |
| 48 | `$ES_*` constants not script-scoped | `Redball.ps1:138-140` | `$ES_CONTINUOUS`, `$ES_SYSTEM_REQUIRED`, `$ES_DISPLAY_REQUIRED` are defined without `$script:` prefix. While they work due to PowerShell's scope resolution, this is inconsistent with the rest of the codebase. | Rename to `$script:ES_CONTINUOUS`, etc., or move into a constants hashtable. | Cleanup | **[MEDIUM]** |
| 49 | DRY violation: balloon tip notification pattern | `Redball.ps1:2236-2241,2274-2281,2405-2413,2447-2455` | The `try { $script:state.NotifyIcon.ShowBalloonTip(...) } catch {}` pattern is copy-pasted 5+ times in TypeThing code. | Refactor into a helper: `Send-TypeThingNotification -Title $t -Message $m -Icon $i`. This already partially exists as `Send-RedballToast` but isn't used in TypeThing. | Cleanup | **[MEDIUM]** |
| 50 | DRY violation: menu item click handler pattern | `Redball.ps1:3732-3744,3755-3769,3780-3792,3803-3815,3828-3836,3843-3851,3865-3873,3885-3893,3900-3908` | All 9+ menu item click handlers follow identical pattern: `try { if ($script:state.IsShuttingDown) { return } ... } catch { Write-RedballLog ... }`. | Create a wrapper: `New-SafeClickHandler -ScriptBlock { ... }` that handles the shutdown check and catch block automatically. | Cleanup | **[MEDIUM]** |

---

## Implementation Priority Order

### Immediate (Critical + High — Fix before next release)

1. **#16** — Enforce TLS 1.2+ on all network calls
2. **#15** — Validate/restrict update repository configuration
3. **#7** — Fix TypeThing retry timer closure scope bug
4. **#8** — Fix `$env:LANG` locale detection to use `Get-Culture`
5. **#14/#17** — Rename `-Encrypt` to `-Obfuscate` or implement real encryption
6. **#1/#23** — Remove duplicate `Show-RedballSettingsDialog`
7. **#22** — Clean up temp files after update download
8. **#21** — Add rate limiting / caching to update checks
9. **#3** — Log errors in `Unregister-GlobalHotkey` catch block
10. **#41** — Resolve state/config duplication (architectural)
11. **#42** — Standardize on `$script:AppRoot` for all path resolution
12. **#26** — Add loading indicator for update check in About dialog
13. **#31** — Fix hardcoded pixel layouts for DPI scaling
14. **#35** — Throttle battery CIM queries to 30-60 second intervals

### Next Sprint (Medium — Improve quality and UX)

15. **#2** — Fix version mismatch in `.NOTES` header
16. **#4** — Add fallback logging in crash report catch block
17. **#9** — Add `bl` locale to `locales.json` and dropdown
18. **#11** — Implement or remove `Update-DarkModeUI`
19. **#12** — Wire up `UseSystemIcons` config key
20. **#18** — Add config file integrity hash
21. **#19** — Sanitize file paths in log messages
22. **#24/#25** — Apply system theme to all dialogs
23. **#27** — Dynamic idle threshold in menu text
24. **#28** — Exit confirmation when active
25. **#30** — Fix form disposal in error paths
26. **#33** — Show "Disabled" status for TypeThing
27. **#34** — Cache pre-rendered tray icons
28. **#36** — Throttle presentation mode process scans
29. **#37** — Move `Add-Type` to one-time initialization
30. **#38** — Throttle log rotation size checks
31. **#46** — Unified theme system across all dialogs
32. **#47** — Standardize error output patterns
33. **#48** — Script-scope ES constants
34. **#49** — DRY: TypeThing notification helper
35. **#50** — DRY: Safe click handler wrapper

### Polish (Low priority — Code quality refinement)

36. **#5/#6** — Add DEBUG logging to empty balloon tip catches
37. **#13** — Rename `Send-RedballTelemetry` to clarify local-only
38. **#20** — Add safety comment to `Invoke-Expression` in tests
39. **#29** — Add `bl` to locale dropdown
40. **#32** — Add `AccessibleName` to dialog input controls
41. **#39** — Clean runspace stop mechanism
42. **#40** — Cache parsed embedded locale JSON
43. **#43/#44/#45** — Replace magic numbers with named constants
