# Troubleshooting

## Tray Icon Not Appearing

- Check Windows notification area settings: **Settings → Personalization → Taskbar → Select which icons appear on the taskbar**
- Click "Show hidden icons" (the `^` arrow) in the system tray
- Redball includes automatic tray icon recovery — it checks every 30 seconds and recreates the icon if needed
- Restart Windows Explorer if needed:

```powershell
Stop-Process -Name explorer -Force
```

## System Still Sleeps

- Check Windows power plan settings (some plans override API calls)
- Ensure no group policy is overriding `SetThreadExecutionState`
- Try enabling **Prevent Display Sleep** in the tray menu
- Enable **Verbose Logging** in Settings → General to diagnose the issue
- Check the Diagnostics section in the main window for runtime state

## Multiple Instances Conflict

Redball uses a named mutex (`Global\Redball_Singleton_Mutex`) to enforce a single instance. If a stale instance is detected, it will show a message and exit.

If that fails:

```powershell
# Find and stop Redball processes manually
Get-Process Redball.UI.WPF | Stop-Process -Force
```

## Log File Locked

If the log file is locked by a previous instance, Redball automatically:

1. Retries with exponential backoff (3 attempts)
2. Falls back to `%TEMP%\Redball_fallback.log`

## Crash Recovery Activated

If you see "Previous crash detected. Settings reset to safe defaults":

- Redball detected that the previous session did not shut down cleanly
- All monitoring features are disabled as a safety measure
- Re-enable your desired features via the Settings sections in the main window
- Check the Diagnostics section for log entries about the crash

## TypeThing Hotkeys Not Working

- **Hotkey already in use:** Another application may have registered the same hotkey. Change the hotkey in the TypeThing section.
- **Not enabled:** Check that `TypeThingEnabled` is `true` in settings
- **Check the log:** Open the Diagnostics section and look for "Failed to register" messages in the recent log viewer

## TypeThing Typing Issues

- **Characters not appearing:** The target application may not accept `SendInput`. Try clicking directly in the text field before pressing the hotkey.
- **Double newlines:** This can happen with `\r\n` line endings. Redball skips `\r` when followed by `\n` to prevent this — check if the source text has unusual line endings.
- **Wrong characters:** Ensure the target application's keyboard layout matches your system. `KEYEVENTF_UNICODE` bypasses the keyboard layout but some apps may not support it.

## Battery-Aware Not Detecting Battery

- Desktop systems without batteries are unaffected — this is expected
- WMI queries can fail on some systems. Check the Diagnostics section for battery-related errors.

## Network-Aware False Disconnects

- Network monitoring checks hardware interfaces
- Virtual adapters (VPN, Hyper-V) may be excluded
- If you're on WiFi and it briefly disconnects, Redball will pause and resume

## Schedule Not Activating

- Ensure `ScheduleEnabled` is `true`
- Check that today's day is in `ScheduleDays`
- Verify time format is `HH:mm` (24-hour)
- The schedule is checked every 30 seconds by the duration timer

## High DPI / Blurry Icon

Redball is DPI-aware and renders the icon with anti-aliasing. If the icon appears blurry:

- Ensure your Windows display scaling is set consistently
- The GDI+ icon is rendered at 32x32 pixels with anti-aliasing

## Performance Issues

If Redball uses too much CPU or memory:

- Disable `EnablePerformanceMetrics` if enabled (reduces overhead)
- Check the Diagnostics section for rapid error loops
- Disable monitoring features you don't need (battery, network, idle, schedule, presentation, thermal)
- Check log file size — if it's very large, reduce `MaxLogSizeMB` or delete the log

## Getting Diagnostic Info

1. Open the main window → **Diagnostics** section
2. View runtime state, config validation, logging paths, and recent log entries
3. Click **Export Diagnostics** to save a full diagnostic report
4. Click **Open Logs** to open the log folder in File Explorer
