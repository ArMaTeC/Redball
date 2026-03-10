# Settings GUI

Redball provides two settings dialogs: a full tabbed dialog and a dedicated TypeThing settings dialog.

## Main Settings Dialog

Open via **Settings...** in the tray menu (or press **G**).

### General Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Default Duration (minutes) | NumericUpDown (1–720) | `DefaultDuration` | Default timer duration |
| Heartbeat Interval (seconds) | NumericUpDown (10–300) | `HeartbeatSeconds` | F15 keypress and refresh interval |
| Show Notification on Start | CheckBox | `ShowBalloonOnStart` | Tray notification on startup |
| Start Minimized | CheckBox | `MinimizeOnStart` | Start without visible window |
| Exit When Timer Completes | CheckBox | `AutoExitOnComplete` | Auto-close after timed session |
| Language | Dropdown (en/es/fr/de) | `Locale` | Display language |

### Power & Monitoring Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Prevent Display Sleep | CheckBox | `PreventDisplaySleep` | Keep display on |
| Use F15 Heartbeat Keypress | CheckBox | `UseHeartbeatKeypress` | Send invisible F15 |
| Battery-Aware Mode | CheckBox | `BatteryAware` | Auto-pause on low battery |
| Battery Threshold (%) | NumericUpDown (5–95) | `BatteryThreshold` | Pause below this % |
| Network-Aware Mode | CheckBox | `NetworkAware` | Auto-pause on disconnect |
| Idle Detection (30 min) | CheckBox | `IdleDetection` | Auto-pause when idle |
| Presentation Mode Detection | CheckBox | `PresentationModeDetection` | Auto-activate for presentations |

### Schedule Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Enable Scheduled Operation | CheckBox | `ScheduleEnabled` | Toggle scheduling |
| Start Time | TextBox (HH:mm) | `ScheduleStartTime` | Daily start time |
| Stop Time | TextBox (HH:mm) | `ScheduleStopTime` | Daily stop time |
| Active Days | CheckedListBox | `ScheduleDays` | Days of the week |

### Advanced Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Max Log File Size (MB) | NumericUpDown (1–100) | `MaxLogSizeMB` | Log rotation threshold |
| Process Isolation | CheckBox | `ProcessIsolation` | Background runspace |
| Performance Metrics | CheckBox | `EnablePerformanceMetrics` | Track CPU/memory |
| Anonymous Telemetry | CheckBox | `EnableTelemetry` | Opt-in telemetry |
| Update Channel | Dropdown (stable/beta) | `UpdateChannel` | Release channel |
| Verify Update Signatures | CheckBox | `VerifyUpdateSignature` | Signature enforcement |
| Update Repository Owner | TextBox | `UpdateRepoOwner` | GitHub owner |
| Update Repository Name | TextBox | `UpdateRepoName` | GitHub repo |

### TypeThing Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Enable TypeThing | CheckBox | `TypeThingEnabled` | Master switch |
| Min Delay (ms) | NumericUpDown (10–1000) | `TypeThingMinDelayMs` | Minimum keystroke delay |
| Max Delay (ms) | NumericUpDown (10–2000) | `TypeThingMaxDelayMs` | Maximum keystroke delay |
| Start Delay (seconds) | NumericUpDown (0–30) | `TypeThingStartDelaySec` | Countdown duration |
| Start Hotkey | TextBox | `TypeThingStartHotkey` | Start typing hotkey |
| Stop Hotkey | TextBox | `TypeThingStopHotkey` | Stop typing hotkey |
| Random Pauses | CheckBox | `TypeThingAddRandomPauses` | Add realistic pauses |
| Type Newlines | CheckBox | `TypeThingTypeNewlines` | Press Enter for newlines |
| Show Notifications | CheckBox | `TypeThingNotifications` | Tray notifications |
| Settings Theme | Dropdown (light/dark/hacker) | `TypeThingTheme` | Dialog theme |

### Saving

Clicking **OK** applies all changes immediately and saves to `Redball.json`. Clicking **Cancel** discards changes.

If TypeThing hotkeys were changed, the old hotkeys are unregistered and new ones are registered automatically.

---

## TypeThing Settings Dialog

Open via **TypeThing → TypeThing Settings...** in the tray menu.

This is a dedicated, themed dialog with grouped controls:

### Typing Speed Group

- **Min Delay (ms)** — Minimum delay between keystrokes
- **Max Delay (ms)** — Maximum delay between keystrokes
- **Approx speed: ~N WPM** — Live WPM estimate (updates as you change values)
- **Start Delay (seconds)** — Countdown before typing starts

### Behaviour Group

- **Add random pauses for realism** — Toggle random pauses
- **Random pause chance (%)** — Probability per character
- **Random pause max (ms)** — Maximum pause duration
- **Type newline characters** — Press Enter for newlines
- **Show tray notifications** — Typing start/stop notifications

### Hotkeys Group

- **Start typing** — Click the field and press your desired key combination
- **Stop typing** — Click the field and press your desired key combination
- Hint text explains how to set hotkeys

### Appearance Group

- **Theme** — Dropdown with live preview (light/dark/hacker)

### Buttons

- **OK** — Save and apply (validates min < max delay)
- **Cancel** — Discard changes
- **Reset** — Restore all TypeThing settings to defaults

### Theme System

The dialog supports three themes applied via `Set-TypeThingFormTheme`:

| Theme | Background | Text | Accent | Font |
| ----- | ---------- | ---- | ------ | ---- |
| `light` | #F5F5F5 | #1A1A1A | #0078D7 | Segoe UI |
| `dark` | #1E1E1E | #E0E0E0 | #4FC3F7 | Segoe UI |
| `hacker` | #0A0A0A | #00FF00 | #00FF41 | Consolas |

Switching themes applies instantly via the `SelectedIndexChanged` event.

---

## About Dialog

Open via **About...** in the tray menu (or press **A**).

Displays:

- Application name and version
- Description
- Latest release version (after clicking "Check for Updates")
- Update status message (up-to-date / update available)
- Release notes (if update available)
- **Check for Updates** button
- **Download Update** button (opens GitHub release page)
- **Close** button

The dialog uses a dark theme (background #1E1E1E) with the Redball name in red (#E63C3C).
