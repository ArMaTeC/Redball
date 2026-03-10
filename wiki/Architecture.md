# Architecture

## Project Structure

```text
Redball/
├── .github/
│   └── workflows/
│       ├── ci.yml                # CI pipeline (test, lint, security)
│       └── release.yml           # Release pipeline (tag, build MSI, publish)
├── installer/
│   ├── Build-MSI.ps1             # WiX MSI build script
│   ├── Deploy-Redball.ps1        # Full deploy pipeline (EXE + MSI + signing)
│   ├── Launch-Redball.vbs        # Hidden-window launcher for MSI post-install
│   ├── Redball.wxs               # WiX v4 installer definition
│   ├── Redball.ico               # Application icon
│   ├── Redball-License.rtf       # License for installer UI
│   └── redball.png               # Readme icon image
├── wiki/                         # Full documentation wiki
├── dist/                         # Build output (MSI, EXE)
├── Redball.ps1                   # Main application script (~5000 lines)
├── Redball.json                  # Configuration file
├── Redball.Tests.ps1             # Pester test suite
├── locales.json                  # External locale overrides (en, es, fr, de)
├── .buildversion                 # Auto-incremented build number
├── CHANGELOG.md                  # Version history
├── LICENSE                       # MIT License
└── README.md                     # Project readme
```

## Component Flow

### Startup Sequence

```text
1. Parse CLI parameters
2. Resolve script root ($PSScriptRoot fallback chain)
3. Load assemblies (System.Windows.Forms, System.Drawing)
4. Define Win32 P/Invoke signatures (SetThreadExecutionState)
5. Initialize state hashtable and config defaults
6. Handle CLI-only commands (if -Status/-CheckUpdate/-Update/-SignScript/-Install/-Uninstall → exit)
7. Check execution policy
8. Singleton instance check → stop stale instances if needed
9. Claim singleton mutex
10. Clear log file locks from previous instances
11. Load persisted config (Import-RedballConfig)
12. Check for crash recovery (Test-CrashRecovery)
13. Restore previous session state (Restore-RedballState)
14. Apply installer defaults if no saved state (Import-RedballInstallerDefaults)
15. Apply CLI parameters (-Duration, -Minimized, -ExitOnComplete)
16. Initialize locales (Import-RedballLocales)
17. Create tray icon and context menu
18. Start heartbeat timer (every N seconds)
19. Start duration timer (every 1 second)
20. Register global hotkey (Ctrl+Alt+Pause)
21. Initialize TypeThing (hotkey window, register Ctrl+Shift+V / Ctrl+Shift+X)
22. Record start time
23. Enter WinForms application message loop
```

### Heartbeat Timer (every N seconds)

```text
→ Check if shutting down
→ If active:
    → Refresh SetThreadExecutionState (keep-awake)
    → Send F15 keypress (if idle > 1 min and heartbeat enabled)
    → Update UI (only if state changed)
```

### Duration Timer (every 1 second)

```text
→ Check if shutting down
→ If battery-aware → Update-BatteryAwareState
→ If network-aware → Update-NetworkAwareState
→ If idle-detection → Update-IdleAwareState
→ If schedule-enabled → Update-ScheduleState
→ If presentation-detection → Update-PresentationModeState
→ If timed mode and expired:
    → Deactivate keep-awake
    → If auto-exit → toast notification → Exit-Application
→ Else → Update UI
```

### Shutdown Sequence

```text
1. Set IsShuttingDown = true
2. Stop TypeThing typing (if active)
3. Unregister TypeThing hotkeys
4. Destroy TypeThing hotkey window
5. Hide and dispose tray icon
6. Stop keep-awake runspace (if process isolation)
7. Reset power state (SetThreadExecutionState → ES_CONTINUOUS)
8. Save session state (Redball.state.json)
9. Save config (Redball.json)
10. Stop and dispose heartbeat timer
11. Stop and dispose duration timer
12. Dispose previous icon (GDI+ cleanup)
13. Release WScript.Shell COM object
14. Release singleton mutex
15. Force garbage collection
16. Call [Application]::Exit()
17. Clear crash flag (via ProcessExit event)
```

## State Management

### `$script:state` (Ordered Hashtable)

Runtime state for the current session. Not persisted directly — selected properties are saved via `Save-RedballState`.

Key categories:

- **Core state:** `Active`, `Until`, `IsShuttingDown`, `SessionId`, `StartTime`
- **Feature toggles:** `PreventDisplaySleep`, `UseHeartbeatKeypress`, `BatteryAware`, `NetworkAware`, `IdleDetection`
- **Auto-pause tracking:** `AutoPausedBattery`, `AutoPausedNetwork`, `AutoPausedIdle`, `AutoPausedPresentation`, `AutoPausedSchedule`
- **UI references:** `NotifyIcon`, `Context`, `HeartbeatTimer`, `DurationTimer`, menu item references
- **TypeThing state:** `TypeThingIsTyping`, `TypeThingShouldStop`, `TypeThingText`, `TypeThingIndex`, etc.

### `$script:config` (Hashtable)

Persisted configuration loaded from/saved to `Redball.json`. Contains all user-configurable settings.

### Sync Between State and Config

Certain properties exist in both `$script:state` and `$script:config` (e.g., `PreventDisplaySleep`, `BatteryAware`). These are synchronized:

- On config load: `Import-RedballConfig` copies config → state
- On config save: `Save-RedballConfig` copies state → config → disk
- On settings dialog OK: dialog values → config + state, then save

## Win32 Interop

Redball compiles several C# types at runtime via `Add-Type`:

| Type/Class | Source DLL | Purpose |
| ---------- | ---------- | ------- |
| `Win32.Power` | `kernel32.dll` | `SetThreadExecutionState` for keep-awake |
| `HotkeyHelper` | `user32.dll` | `RegisterHotKey` / `UnregisterHotKey` |
| `IdleHelper` | `user32.dll` | `GetLastInputInfo` for idle detection |
| `HighContrastHelper` | `user32.dll` | `SystemParametersInfo` for high contrast |
| `DPIHelper` | `user32.dll` | `SetProcessDpiAwarenessContext` for high DPI |
| `TypeThingInput` | `user32.dll` | `SendInput` for character typing |
| `KEYBDINPUT` / `INPUT` | — | Win32 structs for keyboard input |
| `HotkeyMessageWindow` | — | `NativeWindow` subclass for `WM_HOTKEY` messages |

## Singleton Pattern

Redball uses a named mutex (`Global\Redball_Singleton_Mutex`) to enforce a single instance:

1. `Test-RedballInstanceRunning` — tries to open the existing mutex
2. If another instance is found, `Get-RedballProcess` + `Stop-RedballProcess` attempt to stop it
3. `Initialize-RedballSingleton` — creates and acquires the mutex
4. On shutdown, the mutex is released and disposed

## Crash Recovery

1. On startup, `Test-CrashRecovery` checks for `Redball.crash.flag`
2. If found → previous session crashed → reset to safe defaults, show toast
3. A new crash flag is written for the current session
4. On clean shutdown, `Clear-CrashFlag` removes the flag via `ProcessExit` event
5. If Redball crashes, the flag remains → detected on next startup

## Performance Optimizations

- **UI updates:** Icon and tooltip only refresh when state actually changes (`LastIconState`, `LastStatusText`)
- **Battery cache:** WMI query results cached for 30 seconds
- **COM object reuse:** `WScript.Shell` created once and reused for all F15 keypresses
- **Idle-gated F15:** Heartbeat key only fires when system has been idle > 1 minute
- **TypeThing status:** Menu status text only updates every 10 characters to reduce overhead
