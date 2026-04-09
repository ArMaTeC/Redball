# Getting Started

Welcome to Redball! This guide will get you up and running with **TypeThing**, the flagship clipboard typing feature, and introduce you to the secondary keep-awake utility.

## Prerequisites

- **Windows:** Windows 10 or later (WPF application)
- **Linux:** Ubuntu 20.04+ or compatible distributions (GTK application)
- **.NET 10 Runtime** (included in the self-contained EXE — no separate install needed)

## Installation

### Windows

#### Option A — NSIS Installer (Recommended)

Download the latest `Redball-{version}-Setup.exe` from the [Releases](https://github.com/ArMaTeC/Redball/releases) page and run it.

The installer provides:

- Per-user installation to `%LocalAppData%\Redball`
- Start Menu and Desktop shortcuts
- Optional "Start with Windows" shortcut
- Service installation for input injection
- Optional default behavior features (battery-aware, network-aware, idle detection, etc.)
- "Launch Redball" checkbox on the finish page
- Silent install support (`/S`)

### Linux

Download the appropriate package for your distribution:

- **Flatpak:** `redball.flatpak` — Universal Linux package
- **DEB:** `redball.deb` — For Debian/Ubuntu-based distributions
- **Tarball:** `redball-linux.tar.gz` — Portable archive

### Option B — Run the Executable

If you have the self-contained EXE from the repository or a custom build:

```powershell
# Run the WPF application
.\Redball.UI.WPF.exe

# The application will start minimized to the system tray
# Right-click the tray icon to access all features
```

### Option C — Maintenance Arguments (Advanced)

Current command-line arguments are maintenance-focused:

| Argument              | Description                                           |
| --------------------- | ----------------------------------------------------- |
| `--install-service`   | Install the Redball Input Service for elevated typing |
| `--uninstall-service` | Uninstall the Redball Input Service                   |

## Your First TypeThing Session

TypeThing is the primary feature of Redball. Here's how to use it:

### 1. Copy Some Text

Copy any text to your clipboard using `Ctrl+C`:

```text
Hello, this is a test of TypeThing!
It will type this text character by character.
```

### 2. Start TypeThing

Press the default start hotkey: **`Ctrl+Shift+V`**

You'll see a notification: "TypeThing starting in 3 seconds..."

### 3. Switch to Your Target Application

During the countdown, click into the application where you want the text typed:

- A text editor
- A remote desktop session
- A web form
- Any application that blocks Ctrl+V

### 4. Watch It Type

Redball will type the clipboard contents with human-like delays between keystrokes.

### 5. Emergency Stop (if needed)

Press **`Ctrl+Shift+X`** at any time to instantly stop typing.

## Using the Tray Menu

Right-click the Redball tray icon to access all features:

### TypeThing Submenu

- **Type Clipboard** (Ctrl+Shift+V) — Start typing
- **Stop Typing** (Ctrl+Shift+X) — Emergency stop
- **Status: Idle** — Shows current progress when typing
- **TypeThing Settings...** — Configure typing behavior

### Keep-Awake Controls (Secondary Feature)

- **Pause/Resume** — Toggle keep-awake state
- **Indefinite** — Stay awake without timer
- **Display Sleep** — Toggle display sleep prevention
- **Heartbeat** — Toggle F15 keypress heartbeat

## Configuring TypeThing

Open TypeThing settings via:

- **Tray Menu → TypeThing → TypeThing Settings...**
- **Main Window → TypeThing tab**

### Essential Settings

| Setting      | Default      | Description                     |
| ------------ | ------------ | ------------------------------- |
| Start Hotkey | Ctrl+Shift+V | Press this to start typing      |
| Stop Hotkey  | Ctrl+Shift+X | Press this to emergency stop    |
| Min Delay    | 30ms         | Minimum time between keystrokes |
| Max Delay    | 120ms        | Maximum time between keystrokes |
| Start Delay  | 3 seconds    | Countdown before typing begins  |

### Typing Speed Reference

| Min   | Max   | Approx Speed         |
| ----- | ----- | -------------------- |
| 10ms  | 50ms  | ~400 WPM (very fast) |
| 30ms  | 120ms | ~160 WPM (natural)   |
| 50ms  | 200ms | ~96 WPM (slow)       |
| 100ms | 300ms | ~60 WPM (very slow)  |

## Keep-Awake Feature (Secondary)

The keep-awake feature is disabled by default. Enable it if you need to prevent Windows from sleeping:

1. Right-click the tray icon
2. Select **Resume** (or press Space in the menu)
3. The icon turns bright red when active

### Keep-Awake Modes

- **Indefinite** — Stay awake until manually paused
- **Timed** — Set a duration (15, 30, 60, 120 minutes)

### Smart Features

Enable smart monitoring in Settings:

- **Battery-Aware** — Pause when battery is low
- **Network-Aware** — Pause when disconnected
- **Idle Detection** — Pause after inactivity
- **Scheduled** — Auto start/stop on schedule

See the [KeepAwake](KeepAwake) guide for full documentation.

## First Run Experience

On first run, Redball will:

1. Create or load your configuration from `HKCU\Software\Redball\UserData` (with file copy in `%LocalAppData%\Redball\UserData\Redball.json`)
2. Check for singleton instance (only one Redball can run at a time)
3. Check for crash recovery from a previous abnormal termination
4. Display a bright red 3D ball icon in your system tray
5. Initialize TypeThing with default hotkeys (Ctrl+Shift+V / Ctrl+Shift+X)
6. Keep-awake starts in **paused** state (secondary feature)

The main window opens with a modern custom chrome design featuring:

- **Title bar** with app icon, title, and window controls (minimize, maximize, close)
- **Left navigation panel** with sections: Home, Analytics, Diagnostics, Behavior, Smart Features, TypeThing, Settings, and Updates
- **Content area** showing the selected section's controls and information

## Next Steps

- **[TypeThing Guide](TypeThing)** — Master the clipboard typing feature
- **[KeepAwake Guide](KeepAwake)** — Configure the keep-awake utility
- **[Settings GUI](Settings-GUI)** — Explore all configuration options
- **[Tray Menu & Shortcuts](Tray-Menu-and-Shortcuts)** — Keyboard shortcuts reference
