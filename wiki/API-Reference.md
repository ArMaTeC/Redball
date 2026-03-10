# API Reference

Complete reference for all functions in Redball.ps1 (v2.0.29).

## Core Keep-Awake Functions

### `Set-KeepAwakeState`

Controls the Windows power state using `SetThreadExecutionState`.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Enable` | bool | `$true` to prevent sleep, `$false` to allow normal sleep |

**Supports:** `ShouldProcess` (ConfirmImpact: Medium)

```powershell
Set-KeepAwakeState -Enable:$true   # Prevent sleep
Set-KeepAwakeState -Enable:$false  # Allow sleep (reset)
```

When `$script:state.PreventDisplaySleep` is `$true`, the `ES_DISPLAY_REQUIRED` flag is also set.

### `Set-ActiveState`

Sets the active state with optional timer.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Active` | bool | Whether to activate or deactivate |
| `-Until` | DateTime? | Optional expiration time |
| `-ShowBalloon` | bool | Show tray notification (default: `$true`) |

**Supports:** `ShouldProcess` (ConfirmImpact: Medium)

```powershell
Set-ActiveState -Active:$true                                    # Active indefinitely
Set-ActiveState -Active:$true -Until (Get-Date).AddMinutes(30)  # Active for 30 min
Set-ActiveState -Active:$false                                   # Deactivate
```

Handles process isolation mode: starts/stops the background runspace when `ProcessIsolation` is enabled.

### `Switch-ActiveState`

Toggles between active and paused states.

```powershell
Switch-ActiveState  # If active → pause; if paused → resume
```

### `Start-TimedAwake`

Starts a timed keep-awake session.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Minutes` | int | Duration in minutes (1–720) |

**Supports:** `ShouldProcess` (ConfirmImpact: Medium)

```powershell
Start-TimedAwake -Minutes 60  # Active for 1 hour
```

### `Send-HeartbeatKey`

Sends an invisible F15 keypress to prevent idle detection. Only fires when:

- Redball is active
- `UseHeartbeatKeypress` is enabled
- System has been idle for at least 1 minute (prevents interfering with active work)

Uses a cached `WScript.Shell` COM object for performance.

---

## Configuration Functions

### `Import-RedballConfig`

Loads Redball configuration from JSON. If the file doesn't exist, creates it with defaults.

**Alias:** `Load-RedballConfig`

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Path` | string | Path to config JSON file |

```powershell
Import-RedballConfig -Path '.\Redball.json'
```

### `Save-RedballConfig`

Persists runtime settings to disk.

```powershell
Save-RedballConfig -Path '.\Redball.json'
```

Syncs state values (`PreventDisplaySleep`, `UseHeartbeatKeypress`, `BatteryAware`, etc.) back into the config hashtable before writing.

### `Export-RedballSettings`

Exports all config and state values to a backup JSON file.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Path` | string | Output file path (default: `Redball.backup.json`) |
| `-Encrypt` | switch | Base64-encode the output |

```powershell
Export-RedballSettings -Path '.\Redball.backup.json'
```

### `Import-RedballSettings`

Restores settings from a backup file.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Path` | string | Backup file path |
| `-Encrypted` | switch | Decode from Base64 |

```powershell
Import-RedballSettings -Path '.\Redball.backup.json'
```

---

## Session State Functions

### `Save-RedballState`

Saves current session state to `Redball.state.json` for restore on next launch.

Saved properties: `Active`, `PreventDisplaySleep`, `UseHeartbeatKeypress`, `Until`, `BatteryAware`, `BatteryThreshold`.

### `Restore-RedballState`

Restores session state from `Redball.state.json`. Validates that timer expiration is still in the future. Deletes the state file after successful restore.

Returns `$true` if state was restored, `$false` otherwise.

---

## Logging

### `Write-RedballLog`

Writes a timestamped log entry with retry logic and fallback.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Level` | string | `INFO`, `WARN`, `ERROR`, or `DEBUG` |
| `-Message` | string | Log message text |

**Features:**

- Log rotation when file exceeds `MaxLogSizeMB`
- Retry with exponential backoff (3 attempts) for locked files
- Fallback to `%TEMP%\Redball_fallback.log` if primary path fails
- Uses `FileStream` with `FileShare.Read` for concurrent access

---

## Monitoring Functions

### `Get-BatteryStatus`

Returns a hashtable with battery information. Cached for 30 seconds.

**Returns:** `@{ HasBattery; OnBattery; ChargePercent; BatteryStatus }`

### `Test-BatteryThreshold`

Returns `$false` if on battery and charge is at or below `BatteryThreshold`.

### `Update-BatteryAwareState`

Auto-pauses on low battery, auto-resumes when power is restored. Shows toast notifications.

### `Get-NetworkStatus`

Returns `@{ IsConnected; Name; InterfaceType }` using `Get-NetAdapter`.

### `Update-NetworkAwareState`

Auto-pauses on disconnect, auto-resumes on reconnect.

### `Get-IdleTimeMinutes`

Returns user idle time in minutes via `user32.dll!GetLastInputInfo`.

### `Update-IdleAwareState`

Auto-pauses after 30 minutes idle, auto-resumes when user becomes active.

### `Test-ScheduleActive`

Returns `$true` if current time and day are within the configured schedule window.

### `Update-ScheduleState`

Auto-starts/stops based on schedule. Respects `ManualOverride`.

### `Test-PresentationMode`

Detects PowerPoint, Teams screen-sharing, or Windows presentation mode.

**Returns:** `@{ IsPresenting; Source }`

### `Update-PresentationModeState`

Auto-activates when a presentation is detected.

---

## Instance Management

### `Test-RedballInstanceRunning`

Checks if another Redball instance is running by attempting to open `Global\Redball_Singleton_Mutex`.

### `Initialize-RedballSingleton`

Creates and acquires the singleton mutex. Returns `$true` on success.

### `Get-RedballProcess`

Finds other PowerShell processes with `Redball.ps1` in their command line (excluding current PID).

### `Stop-RedballProcess`

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-ProcessId` | int | PID to stop |
| `-Force` | switch | Skip graceful shutdown, force kill immediately |

**Supports:** `ShouldProcess`

Attempts `CloseMainWindow()` first, waits 2 seconds, then force kills if needed.

### `Clear-RedballLogLock`

Tests if the log file is locked. If locked, finds and stops Redball processes holding it.

**Supports:** `ShouldProcess`

---

## Crash Recovery

### `Test-CrashRecovery`

Checks for `Redball.crash.flag`. If found:

- Resets state to safe defaults (Active=false, all monitoring off)
- Shows a toast notification
- Removes the crash flag

Then creates a new crash flag for the current session.

### `Clear-CrashFlag`

Removes the crash flag file. Called via `ProcessExit` event on clean shutdown.

---

## Update Functions

### `Get-RedballLatestRelease`

Queries `https://api.github.com/repos/{owner}/{repo}/releases/latest`.

### `Test-RedballUpdateAvailable`

Compares current `$script:VERSION` against the latest release tag.

**Returns:** `@{ UpdateAvailable; CurrentVersion; LatestVersion; Release; Reason }`

### `Install-RedballUpdate`

Downloads `Redball.ps1` from the latest release, creates a backup, and replaces the current script.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-RestartAfterUpdate` | switch | Launch the new version after install |

Verifies digital signature if `VerifyUpdateSignature` is enabled.

---

## Code Signing Functions

### `Get-RedballCodeSigningCertificate`

Searches `Cert:\CurrentUser\My` for code-signing certificates.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Thumbprint` | string | Optional specific thumbprint to find |

Returns the matching cert, or the newest valid cert if no thumbprint specified.

### `New-RedballSelfSignedCodeSigningCertificate`

Creates a self-signed code-signing certificate valid for 3 years.

**Supports:** `ShouldProcess` (ConfirmImpact: High)

### `Set-RedballCodeSignature`

Signs a script file with Authenticode.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Path` | string | Script path to sign |
| `-Thumbprint` | string | Optional certificate thumbprint |
| `-TimestampServer` | string | RFC 3161 timestamp URL (default: `http://timestamp.digicert.com`) |

**Supports:** `ShouldProcess` (ConfirmImpact: High)

If no cert is found, offers to create a self-signed one.

### `Test-RedballFileSignature`

Verifies the Authenticode signature of a file.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Path` | string | File to verify |
| `-AllowedThumbprints` | string[] | Optional trusted signer thumbprints |

```powershell
Test-RedballFileSignature -Path '.\Redball.ps1'
Test-RedballFileSignature -Path '.\update.ps1' -AllowedThumbprints @('ABC123...')
```

---

## UI Functions

### `Get-CustomTrayIcon`

Generates a 32x32 3D ball icon using GDI+ path gradients.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-State` | string | `active`, `timed`, or `paused` |

Disposes the previous icon to prevent GDI+ handle leaks.

### `Get-StatusText`

Returns a string like `"Active | Display On | F15 On | 45min left"`.

### `Update-RedballUI`

Refreshes icon, tooltip, and all menu items. Only updates when state has changed (performance optimization).

### `Show-RedballSettings`

Opens the full 5-tab settings dialog (General, Power & Monitoring, Schedule, Advanced, TypeThing).

### `Show-RedballSettingsDialog`

Alternative simplified 3-tab settings dialog (General, Smart Features, Logging).

### `Show-AboutDialog`

Shows version info with:

- Update check button
- Release notes display
- Download update button (opens browser)

### `Send-RedballToast`

Sends a Windows 10/11 toast notification via WinRT. Falls back to balloon tip on older systems.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Title` | string | Notification title |
| `-Message` | string | Notification body |
| `-Icon` | string | `info`, `warning`, or `error` |

---

## System Detection Functions

### `Test-HighContrastMode`

Detects Windows high contrast mode via registry and `SystemParametersInfo`.

### `Update-HighContrastUI`

Switches to system icons when high contrast is detected.

### `Enable-HighDPI`

Calls `SetProcessDpiAwarenessContext` (or `SetProcessDPIAware` fallback) for sharp rendering on high-DPI displays.

### `Test-DarkMode`

Checks `HKCU:\...\Themes\Personalize\AppsUseLightTheme` for dark mode.

### `Update-DarkModeUI`

Logs dark mode detection status.

---

## Startup Functions

### `Install-RedballStartup`

Creates a `.lnk` shortcut in the Windows Startup folder. Detects PowerShell 7 (`pwsh`), windowless PowerShell 5.1 (`powershellw.exe`), or standard `powershell.exe`.

### `Uninstall-RedballStartup`

Removes the startup shortcut.

### `Test-RedballStartup`

Returns `$true` if the startup shortcut exists.

### `Import-RedballInstallerDefaults`

Reads MSI installer default values from `HKCU:\Software\Redball\InstallerDefaults`.

---

## Localization Functions

### `Import-RedballLocales`

Loads embedded locales (en, es, fr, de) then merges any external `locales.json` overrides.

### `Get-LocalizedString`

Returns the localized string for a given key. Falls back to English, then returns the key name.

**Parameters:**

| Parameter | Type | Description |
| --------- | ---- | ----------- |
| `-Key` | string | Locale key (e.g., `MenuPause`) |
| `-Locale` | string | Override locale (default: `$script:currentLocale`) |

---

## Other Functions

### `Test-ExecutionPolicy`

Validates that the PowerShell execution policy permits running Redball.

### `Update-PerformanceMetrics`

Tracks heartbeat count, CPU time, memory usage, and handle count. Logs every 5 minutes when `EnablePerformanceMetrics` is enabled.

### `Send-RedballTelemetry`

Logs anonymous usage events locally when `EnableTelemetry` is enabled.

### `Get-RedballStatus`

Returns full status as JSON for the `-Status` CLI parameter.

### `Start-KeepAwakeRunspace` / `Stop-KeepAwakeRunspace`

Creates/destroys a background runspace that independently calls `SetThreadExecutionState` for process isolation reliability.

### `Exit-Application`

Full graceful shutdown sequence:

1. Stop TypeThing typing and unregister hotkeys
2. Destroy TypeThing hotkey window
3. Hide and dispose tray icon
4. Stop keep-awake runspace (if active)
5. Reset power state (`SetThreadExecutionState` to `ES_CONTINUOUS`)
6. Save session state and configuration
7. Stop and dispose heartbeat and duration timers
8. Dispose previous icon
9. Release `WScript.Shell` COM object
10. Release singleton mutex
11. Force garbage collection
12. Call `[Application]::Exit()`

### `Register-GlobalHotkey` / `Unregister-GlobalHotkey`

Registers/unregisters the Ctrl+Alt+Pause global hotkey for toggling Redball.

---

## TypeThing Functions

See [TypeThing](TypeThing.md) for full documentation of all TypeThing functions.
