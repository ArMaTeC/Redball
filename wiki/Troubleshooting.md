# Troubleshooting

Use the decision trees below to quickly diagnose common issues. Start at the top of the relevant section and follow the Yes/No branches.

---

## 🔴 Is the tray icon visible?

**→ No:** Is it hidden behind the overflow arrow (`^`) in the system tray?
  - **→ Yes:** Click the arrow and drag the Redball icon onto the taskbar. Done.
  - **→ No:** Is Redball running? Check Task Manager for `Redball.UI.WPF`.
    - **→ Not running:** Launch Redball. If it exits immediately, check for a crash log in `%LocalAppData%\Redball\UserData\`.
    - **→ Running but no icon:** Redball auto-recovers the tray icon every 30 seconds. If it doesn't appear:
      1. Restart Windows Explorer: `Stop-Process -Name explorer -Force`
      2. Check **Settings → Personalization → Taskbar → Select which icons appear**

**→ Yes:** Continue to the relevant section below.

---

## 💤 Is the system still sleeping despite Redball being active?

**→ Is Redball showing "Active" in the tray tooltip?**
  - **→ No:** Redball is paused. Check if auto-pause triggered (battery, network, idle, schedule).
  - **→ Yes:** Is **Prevent Display Sleep** enabled?
    - **→ No:** Enable it in the tray right-click menu or Settings.
    - **→ Yes:** Is a Windows Group Policy overriding `SetThreadExecutionState`?
      - **→ Check:** Run `powercfg /requests` in an admin terminal. If Redball's request isn't listed, a GPO may be blocking it.
      - **→ Still sleeping:** Enable **Verbose Logging** in Settings → General, reproduce the issue, then check Diagnostics → Recent Log for clues.

---

## ⌨️ TypeThing not working?

**→ Does the hotkey respond at all?**
  - **→ No:** Is TypeThing enabled in Settings?
    - **→ No:** Enable `TypeThingEnabled` in Settings.
    - **→ Yes:** Another app may have claimed the hotkey. Check Diagnostics → Log for "Failed to register" messages. Change the hotkey in Settings.
  - **→ Yes, but characters aren't appearing:**
    - Click directly in the target text field before pressing the hotkey.
    - Try switching to **Service input mode** in Settings if you're using RDP/remote desktop.
  - **→ Yes, but wrong characters or double newlines:**
    - **Double newlines:** Source text may have `\r\n` endings — Redball handles this, but check for unusual encodings.
    - **Wrong characters:** Ensure the target app's keyboard layout matches your system layout.

---

## 🔋 Battery-aware not detecting battery?

**→ Is this a desktop PC (no battery)?**
  - **→ Yes:** Expected — battery monitoring is automatically skipped.
  - **→ No (laptop):** WMI battery queries may have failed. Check Diagnostics for "WMI battery query failed" messages.
    - After 3 consecutive failures, battery monitoring is automatically disabled with a log warning.
    - Restart Redball to retry.

---

## 🌐 Network-aware causing false disconnects?

**→ Are you on WiFi?**
  - **→ Yes:** Brief WiFi dropouts will trigger pause/resume — this is by design.
  - **→ No:** Virtual adapters (VPN, Hyper-V) may be excluded from detection. Check Diagnostics for network-related log entries.

---

## ⏰ Schedule not activating?

1. Is `ScheduleEnabled` set to `true`? → If not, enable it.
2. Is today's day listed in `ScheduleDays`? → Check Settings.
3. Is the time format correct (`HH:mm`, 24-hour)? → e.g., `09:00`, not `9:00 AM`.
4. The schedule is checked every 30 seconds — wait up to 30s after the start time.

---

## 💥 "Previous crash detected" message?

**→ What happened:**
  - The previous session did not shut down cleanly.
  - All monitoring features are disabled as a safety measure.

**→ What to do:**
  1. Re-enable your desired features in Settings.
  2. Check Diagnostics → Log for crash-related entries.
  3. If crashes persist, reset config stores:
     - Delete `%LocalAppData%\Redball\UserData\Redball.json`
     - Remove `HKCU\Software\Redball\UserData`

---

## 🔒 Multiple instances conflict?

**→ Redball says "Another instance is already running":**
  - A previous instance may not have released the singleton mutex.
  - Kill it manually: `Get-Process Redball.UI.WPF | Stop-Process -Force`
  - Then relaunch.

---

## 📁 Log file locked?

Redball handles this automatically:
1. Retries with exponential backoff (3 attempts)
2. Falls back to `%TEMP%\Redball_fallback.log`

If the main log is permanently locked, delete it when Redball is not running.

---

## 🖥️ High DPI / Blurry icon?

- Ensure Windows display scaling is consistent across monitors.
- The tray icon is rendered at 32×32px with GDI+ anti-aliasing.

---

## 🐌 Performance issues (high CPU/memory)?

1. **Is `EnablePerformanceMetrics` on?** → Disable it to reduce overhead.
2. **Check Diagnostics for rapid error loops** — a failing service retrying constantly.
3. **Disable unused monitors** — battery, network, idle, schedule, presentation, thermal.
4. **Is the log file very large?** → Reduce `MaxLogSizeMB` or delete the log file.

---

## 📋 Getting Diagnostic Info

1. Open the main window → **Diagnostics** section
2. View runtime state, config validation, logging paths, and recent log entries
3. Click **Export Diagnostics** to save a full diagnostic report
4. Click **Open Logs** to open the log folder in File Explorer
