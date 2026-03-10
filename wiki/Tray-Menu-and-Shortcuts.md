# Tray Menu & Keyboard Shortcuts

## System Tray Icon

Redball displays a 3D ball icon in the Windows system tray (notification area). The icon color indicates the current state:

| Color | State |
| ----- | ----- |
| **Bright Red** (crimson/tomato gradient) | Active — keeping system awake |
| **Orange/Red** (dark orange gradient) | Timed mode — countdown in progress |
| **Dark Red/Gray** (muted colors) | Paused / idle state |

The tooltip shows the current status including active state, display sleep, F15 heartbeat, and timer countdown.

## Context Menu

Right-click the tray icon to access the full context menu:

| Menu Item | Shortcut | Description |
| --------- | -------- | ----------- |
| **Status** | — | Read-only status line (active state, display, F15, timer) |
| **Pause / Resume Keep-Awake** | Space | Toggle the keep-awake state |
| **Prevent Display Sleep** | D | Toggle display sleep prevention (checkbox) |
| **Use F15 Heartbeat** | H | Toggle invisible F15 keypresses (checkbox) |
| **Stay Awake For →** | — | Submenu with duration options (15 / 30 / 60 / 120 min) |
| **Stay Awake Until Paused** | I | Run indefinitely |
| **Battery-Aware Mode** | B | Toggle auto-pause on low battery (checkbox) |
| **Start with Windows** | S | Toggle startup shortcut (checkbox) |
| **Network-Aware Mode** | N | Toggle auto-pause on disconnect (checkbox) |
| **Idle Detection (30min)** | L | Toggle idle-based auto-pause (checkbox) |
| **TypeThing →** | — | Clipboard typer submenu |
| ↳ **Type Clipboard** | Ctrl+Shift+V | Start typing clipboard contents |
| ↳ **Stop Typing** | Ctrl+Shift+X | Emergency stop typing |
| ↳ **Status: Idle** | — | Current TypeThing status (read-only) |
| ↳ **TypeThing Settings...** | — | Open TypeThing settings dialog |
| **Settings...** | G | Open the full tabbed settings dialog |
| **About...** | A | Version info and update checker |
| **Exit** | X | Close Redball gracefully |

## Keyboard Shortcuts

These shortcuts work when the tray menu is open:

| Shortcut | Action |
| -------- | ------ |
| **Space** | Toggle pause / resume |
| **D** | Toggle display sleep prevention |
| **H** | Toggle F15 heartbeat |
| **I** | Stay awake indefinitely |
| **B** | Toggle battery-aware mode |
| **S** | Toggle startup with Windows |
| **N** | Toggle network-aware mode |
| **L** | Toggle idle detection |
| **G** | Open settings dialog |
| **A** | Open About dialog |
| **X** | Exit application |

## Global Hotkeys

These hotkeys work system-wide, even when the tray menu is closed:

| Hotkey | Action |
| ------ | ------ |
| **Ctrl+Alt+Pause** | Toggle pause / resume (Redball global hotkey) |
| **Ctrl+Shift+V** | TypeThing: Start typing clipboard (configurable) |
| **Ctrl+Shift+X** | TypeThing: Emergency stop typing (configurable) |

## Mouse Interactions

| Action | Result |
| ------ | ------ |
| **Right-click** | Open context menu |
| **Double-click** | Toggle pause / resume |

## Accessibility

All menu items include `AccessibleName` and `AccessibleDescription` properties for screen reader compatibility. The tray icon tooltip provides real-time status information.
