# Redball for Linux

Native Linux implementation using GTK4 and libadwaita for GNOME/Flatpak distribution.

## Project Structure

```
src/Redball.Linux/
├── redball/
│   ├── __init__.py
│   ├── main.py              # Application entry
│   ├── window.py            # Main window
│   ├── tray.py              # System tray indicator
│   ├── keepawake.py         # Screensaver/idle inhibition
│   ├── config.py            # Settings management
│   ├── timer.py             # Session timer
│   └── widgets/
│       ├── __init__.py
│       ├── status_indicator.py
│       └── preferences.py
├── data/
│   ├── com.armatec.Redball.desktop
│   ├── com.armatec.Redball.metainfo.xml
│   ├── com.armatec.Redball.gschema.xml
│   └── icons/
├── po/                      # Translations
├── tests/
├── flatpak/
│   └── com.armatec.Redball.yml
├── meson.build
└── README.md
```

## Requirements

- Python 3.9+
- GTK 4.0+
- libadwaita 1.0+
- PyGObject
- dbus-python (for notifications)

## Key Features

### System Tray (AppIndicator)
- Status indicator in panel
- Quick toggle menu
- Right-click context menu

### Keep-Awake Methods
- `xdg-screensaver` reset (X11)
- `org.freedesktop.ScreenSaver` inhibit (DBus)
- `idle-inhibit` portal (Flatpak/Wayland)
- `systemd-inhibit` for system idle

### Platform-Specific
- Wayland compatibility (via portals)
- X11 support (traditional screensaver)
- Flatpak sandboxing
- systemd integration (optional service)

### Desktop Integration
- Desktop notifications (libnotify)
- GNOME settings sync
- KDE Plasma tray support

## Installation

### From Source
```bash
cd src/Redball.Linux
meson setup build
ninja -C build
sudo ninja -C build install
```

### Flatpak
```bash
flatpak-builder --user --install flatpak/build flatpak/com.armatec.Redball.yml
```

## Platform Parity

| Feature | Windows | macOS | Linux | Status |
|---------|---------|-------|-------|--------|
| Keep-Awake Engine | ✓ | IOKit | xdg/portal | Ready |
| System Tray | ✓ | Menu Bar | AppIndicator | Ready |
| Notifications | ✓ | ✓ | libnotify | Ready |
| Timed Sessions | ✓ | ✓ | ✓ | Ready |
| Battery Aware | ✓ | ✓ | UPower | Ready |
| Idle Detection | ✓ | ✓ | XScreenSaver | Ready |
| TypeThing | HID/CGEvent | CGEvent | AT-SPI | Ready |
| Mini Widget | WPF | SwiftUI | GTK4 | Ready |
| Browser Extension | ✓ | ✓ | ✓ | Shared |

## Keep-Awake Implementation

```python
# Multi-backend approach
class KeepAwakeEngine:
    def __init__(self):
        self.backend = self._detect_backend()
    
    def _detect_backend(self):
        if os.environ.get('WAYLAND_DISPLAY'):
            return WaylandBackend()
        elif os.environ.get('DISPLAY'):
            return X11Backend()
        else:
            return SystemdBackend()
    
    def start(self):
        self.backend.inhibit()
    
    def stop(self):
        self.backend.uninhibit()
```

## Development

```bash
# Run directly
cd src/Redball.Linux
python3 -m redball

# With hot reload
pip3 install pygobject-stubs
python3 -m redball --dev
```

## Distribution

- **Flatpak**: Primary distribution method (Flathub)
- **DEB/RPM**: Native packages for Debian/Fedora
- **AUR**: Arch User Repository
- **Snap**: Ubuntu Store (secondary)
