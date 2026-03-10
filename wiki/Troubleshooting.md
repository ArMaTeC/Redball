# Troubleshooting

## Tray Icon Not Appearing

- Check Windows notification area settings: **Settings → Personalization → Taskbar → Select which icons appear on the taskbar**
- Click "Show hidden icons" (the `^` arrow) in the system tray
- Restart Windows Explorer if needed:

```powershell
Stop-Process -Name explorer -Force
```

## System Still Sleeps

- Check Windows power plan settings (some plans override API calls)
- Ensure no group policy is overriding `SetThreadExecutionState`
- Try enabling **Prevent Display Sleep** in the tray menu
- Try enabling **Process Isolation** in Settings → Advanced (runs keep-awake in a separate runspace)
- Check the log file for errors:

```powershell
Get-Content .\Redball.log -Tail 50
```

## Multiple Instances Conflict

Redball uses a named mutex (`Global\Redball_Singleton_Mutex`) to enforce a single instance. If a stale instance is detected, it will attempt to stop it automatically.

If that fails:

```powershell
# Find and stop Redball processes manually
Get-Process powershell, pwsh | Where-Object { $_.CommandLine -like '*Redball*' } | Stop-Process -Force
```

## Log File Locked

If the log file is locked by a previous instance, Redball automatically:

1. Retries with exponential backoff (3 attempts)
2. Attempts to find and stop processes holding the lock (`Clear-RedballLogLock`)
3. Falls back to `%TEMP%\Redball_fallback.log`

To manually clear:

```powershell
# Check if the fallback log was used
Get-Content "$env:TEMP\Redball_fallback.log" -Tail 20
```

## `$PSScriptRoot` Empty

This happens when running from ISE or VS Code without saving. Either:

- Save the file first, then run
- Specify the config path explicitly:

```powershell
.\Redball.ps1 -ConfigPath ".\Redball.json"
```

Redball has a fallback chain: `$PSScriptRoot` → `$MyInvocation.MyCommand.Path` → `(Get-Location).Path`.

## Crash Recovery Activated

If you see "Previous crash detected. Settings reset to safe defaults":

- Redball detected that the previous session did not shut down cleanly
- All monitoring features are disabled as a safety measure
- Re-enable your desired features via the Settings dialog
- Check the log file for the cause of the crash

## TypeThing Hotkeys Not Working

- **Hotkey already in use:** Another application may have registered the same hotkey. Change the hotkey in TypeThing Settings.
- **Not enabled:** Check that `TypeThingEnabled` is `true` in settings
- **Check the log:** Look for "Failed to register start hotkey" messages

```powershell
Select-String "TypeThing.*hotkey" .\Redball.log
```

## TypeThing Typing Issues

- **Characters not appearing:** The target application may not accept `SendInput`. Try clicking directly in the text field before pressing the hotkey.
- **Double newlines:** This can happen with `\r\n` line endings. Redball skips `\r` when followed by `\n` to prevent this — check if the source text has unusual line endings.
- **Wrong characters:** Ensure the target application's keyboard layout matches your system. `KEYEVENTF_UNICODE` bypasses the keyboard layout but some apps may not support it.

## Battery-Aware Not Detecting Battery

- Desktop systems without batteries will always show `HasBattery = $false` — this is expected
- WMI queries can fail on some systems. Check:

```powershell
Get-CimInstance -ClassName Win32_Battery
```

## Network-Aware False Disconnects

- `Get-NetAdapter` only checks hardware interfaces (`HardwareInterface = $true`)
- Virtual adapters (VPN, Hyper-V) are excluded
- If you're on WiFi and it briefly disconnects, Redball will pause and resume

## Schedule Not Activating

- Ensure `ScheduleEnabled` is `true`
- Check that today's day is in `ScheduleDays`
- Verify time format is `HH:mm` (24-hour)
- The schedule is checked every second by the duration timer

## High DPI / Blurry Icon

Redball calls `SetProcessDpiAwarenessContext` on startup for sharp rendering. If the icon appears blurry:

- Ensure your Windows display scaling is set consistently
- The GDI+ icon is rendered at 32x32 pixels with anti-aliasing

## Performance Issues

If Redball uses too much CPU or memory:

- Disable `EnablePerformanceMetrics` if enabled (reduces overhead)
- Check the log for rapid error loops
- Disable monitoring features you don't need (battery, network, idle, schedule, presentation)
- Check log file size — if it's very large, reduce `MaxLogSizeMB` or delete the log

## Execution Policy Errors

```powershell
# Check current policy
Get-ExecutionPolicy -List

# Set for current user (persistent)
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Or run with bypass (one-time)
PowerShell -ExecutionPolicy Bypass -File .\Redball.ps1
```

## Getting Diagnostic Info

```powershell
# Get full status as JSON
.\Redball.ps1 -Status | ConvertFrom-Json | Format-List

# View recent log entries
Get-Content .\Redball.log -Tail 100

# Check for running instances
Get-CimInstance -ClassName Win32_Process -Filter "Name = 'powershell.exe' OR Name = 'pwsh.exe'" |
    Where-Object { $_.CommandLine -like '*Redball*' } |
    Select-Object ProcessId, CommandLine, CreationDate
```
