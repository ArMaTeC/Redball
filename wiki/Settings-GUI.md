# Settings GUI

Redball provides a comprehensive settings dialog accessible from the main window navigation panel or via the tray menu.

## Main Settings Dialog

Open via **Settings** in the main window navigation panel, or **Settings...** in the tray menu (or press **G**).

The settings are organized into dedicated sections accessible via the left navigation panel:

### General Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Theme | Dropdown (14 options) | `Theme` | Visual theme: System, Dark, Light, Midnight Blue, Forest Green, Ocean Blue, Sunset Orange, Royal Purple, Slate Gray, Rose Gold, Cyberpunk, Coffee, Arctic Frost, High Contrast |
| Minimize to tray on start | CheckBox | `MinimizeOnStart` | Start without visible window |
| Show notifications | CheckBox | `ShowBalloonOnStart` | Tray notifications on startup and events |
| Notification Mode | Dropdown | `NotificationMode` | All events, Important only, Errors only, or Silent |
| Enable verbose logging | CheckBox | `VerboseLogging` | Record extra diagnostic details |
| Max Log Size (MB) | Slider (1–100) | `MaxLogSizeMB` | Log rotation threshold |

### Behavior Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Prevent Display Sleep | CheckBox | `PreventDisplaySleep` | Keep display awake while active |
| Heartbeat Input | Dropdown (F13–F16) | `HeartbeatInputMode` | Invisible function key for idle prevention |
| Default Duration (minutes) | Slider (1–720) | `DefaultDuration` | Default timer duration for timed sessions |
| Auto-exit after duration completes | CheckBox | `AutoExitOnComplete` | Exit when timed session finishes |

### Smart Features Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Battery-aware mode | CheckBox | `BatteryAware` | Auto-pause when battery is low |
| Pause threshold (%) | Slider (5–50) | `BatteryThreshold` | Battery % below which to auto-pause |
| Network-aware mode | CheckBox | `NetworkAware` | Auto-pause when network disconnects |
| Idle detection | CheckBox | `IdleDetection` | Auto-pause after user inactivity |
| Idle threshold (minutes) | Slider (5–120) | `IdleThreshold` | Minutes of inactivity before auto-pause |
| Presentation mode detection | CheckBox | `PresentationModeDetection` | Auto-activate for PowerPoint/Teams |
| Scheduled operation | CheckBox | `ScheduleEnabled` | Enable daily scheduled activation |

### TypeThing Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Enable TypeThing | CheckBox | `TypeThingEnabled` | Master switch for clipboard typer |
| Start Hotkey | Hotkey capture | `TypeThingStartHotkey` | Key combination to start typing |
| Stop Hotkey | Hotkey capture | `TypeThingStopHotkey` | Key combination to stop typing |
| Typing Speed (ms per char) | Slider (10–500) | `TypeThingMinDelayMs` / `TypeThingMaxDelayMs` | Character typing delay range |
| Add random pauses | CheckBox | `TypeThingAddRandomPauses` | Insert natural-feeling random delays |
| Type newlines as Enter key | CheckBox | `TypeThingTypeNewlines` | Send Enter key for line breaks |
| **Input Method** | Dropdown | `TypeThingInputMode` | **Standard (SendInput)** or **Service** for elevated/RDP typing |
| **Service Mode** | CheckBox | `TypeThingServiceMode` | Use Windows Service for input injection (works over RDP). |
| **Audio Feedback** | CheckBox | `TypeThingAudioFeedback` | Play a subtle click sound when characters are successfully sent. |
| **Install Input Service** | Button | — | Install the Redball Input Service for elevated typing support. |
| **Service Status** | Visual + Text | — | Live status: Ready (pulsing green) / Not Ready / Error (flashing red). |
| **Last refresh / Next refresh** | Detail text | — | Diagnostics: timestamps, last action, init failures, errors. |

### TypeThing Service Features

* **Service Mode**: Uses a Windows Service to inject input, which works across UAC boundaries and Remote Desktop sessions.
* **Automatic Fallback**: If Service mode fails, automatically falls back to SendInput.
* **Progress Notifications**: Live progress bar and cancel button for long typing sessions.

### Updates Tab

| Control | Type | Config Key | Description |
| ------- | ---- | ---------- | ----------- |
| Auto-check for updates | CheckBox | `AutoUpdateCheckEnabled` | Enable background update checks |
| Check interval (minutes) | Slider | `AutoUpdateCheckIntervalMinutes` | How often to check automatically |
| Update Channel | Dropdown (stable/beta) | `UpdateChannel` | Release channel preference |
| Verify update signatures | CheckBox | `VerifyUpdateSignature` | Require valid digital signature on updates |
| Check for Updates Now | Button | — | Query update service immediately |
| Current Version | Label | — | Display installed version |

### Actions Section (General Tab)

Quick action buttons at the bottom of the General tab:

* **Open Logs** — Opens the log folder in File Explorer
* **Export Diagnostics** — Creates a diagnostics export for troubleshooting
* **Start TypeThing** — Launches the TypeThing typing workflow
* **Settings auto-apply** — Changes are saved and applied automatically as controls change

### Saving

Settings save automatically to registry-backed config (`HKCU\Software\Redball\UserData`) with a local copy in `%LocalAppData%\Redball\UserData\Redball.json`. Changes are applied immediately to the running instance.

If TypeThing hotkeys were changed, the old hotkeys are unregistered and new ones are registered automatically.

---

## TypeThing Settings Dialog

Open via **TypeThing** in the main window navigation panel, or via **TypeThing → TypeThing Settings...** in the tray menu.

This is a dedicated, themed dialog with grouped controls:

### Typing Speed Group

* **Min Delay (ms)** — Minimum delay between keystrokes
* **Max Delay (ms)** — Maximum delay between keystrokes
* **Approx speed: ~N WPM** — Live WPM estimate (updates as you change values)
* **Start Delay (seconds)** — Countdown before typing starts

### Behaviour Group

* **Add random pauses for realism** — Toggle random pauses
* **Random pause chance (%)** — Probability per character
* **Random pause max (ms)** — Maximum pause duration
* **Type newline characters** — Press Enter for newlines
* **Show tray notifications** — Typing start/stop notifications

### Hotkeys Group

* **Start typing** — Click the field and press your desired key combination
* **Stop typing** — Click the field and press your desired key combination
* Hint text explains how to set hotkeys

### Appearance Group

* **Theme** — Dropdown with live preview (light/dark/hacker)

### Buttons

* **OK** — Save and apply (validates min < max delay)
* **Cancel** — Discard changes
* **Reset** — Restore all TypeThing settings to defaults

### Theme System

The dialog supports three visual themes:

| Theme | Background | Text | Accent | Font |
| ----- | ---------- | ---- | ------ | ---- |
| `light` | #F5F5F5 | #1A1A1A | #0078D7 | Segoe UI |
| `dark` | #1E1E1E | #E0E0E0 | #4FC3F7 | Segoe UI |
| `hacker` | #0A0A0A | #00FF00 | #00FF41 | Consolas |

Switching themes applies instantly.

---

## Main Window UI

The main Redball window features a modern custom chrome design:

### Title Bar

* **App Icon** — Red "R" logo in a rounded corner border
* **Title** — "Redball" with subtitle "Desktop Control Center"
* **Window Controls** — Minimize (—), Maximize (□), Close (✕) with hover effects
* **Draggable** — Click and drag anywhere on the title bar to move the window

### Navigation Panel

Left-side navigation with ten sections:

| Section | Description |
| ------- | ----------- |
| **Home** | Overview dashboard with quick access cards |
| **Analytics** | Session counts, usage patterns, feature events with CSV/JSON export |
| **Metrics** | Feature adoption rates, retention, and product usage metrics |
| **Diagnostics** | Runtime state, logging paths, temperature, session stats, app health |
| **Settings** | General application settings (theme, notifications, logging) |
| **Behavior** | Keep-awake controls (display sleep, heartbeat key, duration) |
| **Smart Features** | Battery, network, idle, schedule, presentation, process watcher, VPN, thermal, session lock, app rules |
| **TypeThing** | Typing automation hotkeys, speed, and behavior |
| **Updates** | Update channels, auto-check, and version management |

Click any navigation item to switch the content area. All navigation items show descriptive tooltips on hover.

### Content Area

The main content area displays the selected section's controls and information. Each section is organized into cards with consistent styling across all themes.

---

## About Dialog

Open via **About...** in the tray menu (or press **A**).

Displays:

* Application name and version
* Description
* Latest release version (after clicking "Check for Updates")
* Update status message (up-to-date / update available)
* Release notes (if update available)
* **Check for Updates** button
* **Download Update** button (opens GitHub release page)
* **Close** button

The dialog uses a dark theme (background #1E1E1E) with the Redball name in red (#E63C3C).
