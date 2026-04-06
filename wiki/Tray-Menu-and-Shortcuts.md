# Tray Menu & Keyboard Shortcuts

## System Tray Icon

Redball displays a 3D ball icon in the Windows system tray (notification area). The icon color indicates the current state:

| Color | State |
| ----- | ----- |
| **Bright Red** (crimson/tomato gradient) | Keep-awake active — preventing sleep |
| **Orange/Red** (dark orange gradient) | Keep-awake timed mode — countdown in progress |
| **Dark Red/Gray** (muted colors) | Keep-awake paused / idle state |

**Note:** TypeThing operates independently of the keep-awake state. The icon color only reflects keep-awake status.

The tooltip shows the current status including keep-awake state, display sleep, F15 heartbeat, and timer countdown.

## Context Menu

Right-click the tray icon to access the full context menu:

### TypeThing (Primary Feature)

| Menu Item | Shortcut | Description |
|-----------|----------|-------------|
| **TypeThing →** | — | Clipboard typer submenu |
| ↳ **Type Clipboard** | Ctrl+Shift+V | Start typing clipboard contents |
| ↳ **Stop Typing** | Ctrl+Shift+X | Emergency stop typing |
| ↳ **Status: Idle** | — | Current TypeThing status (read-only) |
| ↳ **TypeThing Settings...** | — | Open TypeThing settings dialog |

### Keep-Awake (Secondary Feature)

| Menu Item | Shortcut | Description |
|-----------|----------|-------------|
| **Resume / Pause** | Space | Toggle keep-awake on/off |
| **Indefinite** | I | Stay awake without timer |
| **Prevent Display Sleep** | D | Toggle display sleep prevention |
| **Heartbeat (F15)** | H | Toggle F15 keypress heartbeat |

### Smart Features (Keep-Awake)

| Menu Item | Shortcut | Description |
|-----------|----------|-------------|
| **Battery-Aware** | B | Toggle battery-based auto-pause |
| **Network-Aware** | N | Toggle network-based auto-pause |
| **Idle Detection (30 min)** | L | Toggle idle-based auto-pause |

### System

| Menu Item | Shortcut | Description |
|-----------|----------|-------------|
| **Settings...** | G | Open the full tabbed settings dialog |
| **About...** | A | Version info and update checker |
| **Exit** | X | Close Redball gracefully |

## Keyboard Shortcuts

These shortcuts work when the tray menu is open:

| Shortcut | Action |
| -------- | ------ |
| **Space** | Toggle keep-awake pause/resume |
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
| **Ctrl+Shift+V** | TypeThing: Start typing clipboard (configurable) |
| **Ctrl+Shift+X** | TypeThing: Emergency stop typing (configurable) |
| **Ctrl+Alt+Pause** | Keep-awake: Toggle pause/resume (Redball global hotkey) |

## Mouse Interactions

| Action | Result |
| ------ | ------ |
| **Right-click** | Open context menu |
| **Double-click** | Toggle keep-awake pause/resume |

## TypeThing Workflow

The typical TypeThing usage workflow:

1. **Copy text** — Use Ctrl+C in any application
2. **Press Ctrl+Shift+V** — Global hotkey to start TypeThing
3. **Switch windows** — During the 3-second countdown, click your target application
4. **Type types** — Characters appear with human-like delays
5. **Emergency stop** — Press Ctrl+Shift+X if needed

## Accessibility

All menu items include `AccessibleName` and `AccessibleDescription` properties for screen reader compatibility. The tray icon tooltip provides real-time status information.
