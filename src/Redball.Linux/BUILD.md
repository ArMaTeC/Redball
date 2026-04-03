# Redball Ubuntu Build System

This directory contains the Ubuntu/Debian build system for Redball Linux - a native GTK4/libadwaita implementation that replaces the Windows-specific WPF version.

## Overview

The build system uses:
- **Meson** - Build configuration and compilation
- **Ninja** - Build execution
- **GTK4/libadwaita** - Native Linux UI framework
- **Flatpak** - Primary distribution method
- **DEB packages** - Native Ubuntu/Debian packages

## Quick Start

### 1. Setup your Ubuntu server as a GitHub self-hosted runner

```bash
# On your Ubuntu server, run:
curl -fsSL https://github.com/actions/runner/releases/latest/download/actions-runner-linux-x64-2.319.1.tar.gz -o runner.tar.gz
tar xzf runner.tar.gz
./config.sh --url https://github.com/ArMaTeC/Redball --token YOUR_TOKEN
./run.sh
```

### 2. Build locally

```bash
# Install dependencies
sudo apt-get update
sudo apt-get install -y meson ninja-build python3-pip \
    libgtk-4-dev libadwaita-1-dev gir1.2-gtk-4.0 gir1.2-adw-1 \
    desktop-file-utils appstream-util gettext flatpak flatpak-builder

# Build
./scripts/build-linux.sh --install-deps
./scripts/build-linux.sh -a
```

### 3. Build with GitHub Actions

The `.github/workflows/linux-ci.yml` workflow will automatically:
- Build on your self-hosted Ubuntu runner
- Create DEB and tarball packages
- Create GitHub releases for the Linux version

## File Structure

```
src/Redball.Linux/
├── redball/                    # Python application source
│   ├── __init__.py
│   ├── main.py                 # Application entry point
│   ├── window.py               # Main GTK4 window
│   ├── tray.py                 # System tray indicator
│   ├── keepawake.py            # Keep-awake engine (X11/Wayland/systemd)
│   ├── config.py               # GSettings configuration
│   ├── timer.py                # Pomodoro/session timers
│   └── widgets/                # UI widgets
│       ├── __init__.py
│       ├── status_indicator.py
│       └── preferences.py
├── data/                       # Desktop integration files
│   ├── com.armatec.Redball.desktop.in
│   ├── com.armatec.Redball.metainfo.xml.in
│   ├── com.armatec.Redball.gschema.xml
│   └── icons/                  # Application icons
├── flatpak/                    # Flatpak manifest
│   └── com.armatec.Redball.yml
├── po/                         # Translations (i18n)
├── tests/                      # Unit tests
├── meson.build                 # Main build configuration
├── redball.in                  # Launcher script template
└── README.md                   # This file
```

## Build Options

| Option | Description |
|--------|-------------|
| `./scripts/build-linux.sh` | Standard build only |
| `./scripts/build-linux.sh -d` | Build + create tarball |
| `./scripts/build-linux.sh --deb` | Build + create DEB package |
| `./scripts/build-linux.sh -f` | Build + create Flatpak |
| `./scripts/build-linux.sh -a` | Build all package types |
| `./scripts/build-linux.sh -c` | Clean build artifacts |
| `./scripts/build-linux.sh --install-deps` | Install build dependencies |

## Manual Build (without script)

```bash
cd src/Redball.Linux

# Setup
meson setup build --prefix=/usr --buildtype=release

# Compile
ninja -C build

# Test
meson test -C build

# Install locally
sudo ninja -C build install
sudo glib-compile-schemas /usr/share/glib-2.0/schemas/

# Create distribution
DESTDIR=$(pwd)/dist/install ninja -C build install
cd dist && tar -czf redball-linux.tar.gz -C install .
```

## Flatpak Build

```bash
cd src/Redball.Linux

# Install runtime
flatpak remote-add --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install flathub org.gnome.Platform//47 org.gnome.Sdk//47

# Build
flatpak-builder --force-clean --repo=flatpak/repo \
    flatpak/build flatpak/com.armatec.Redball.yml

# Create bundle
flatpak build-bundle flatpak/repo redball.flatpak com.armatec.Redball

# Install
flatpak install --user redball.flatpak
```

## GitHub Actions Workflow

The workflow file `.github/workflows/linux-ci.yml` runs on your self-hosted runner:

1. **Lint job** - Code quality checks (on GitHub-hosted runner)
2. **build-self-hosted** - Build on your Ubuntu server
   - Compiles with Meson
   - Creates DEB package
   - Creates tarball distribution
   - Uploads artifacts
3. **release** - Creates GitHub release with Linux packages

### Setting up the self-hosted runner

1. Go to **Settings > Actions > Runners** in your GitHub repo
2. Click **New self-hosted runner**
3. Follow the instructions to download and configure
4. Run `./run.sh` to start the runner

The runner will automatically pick up jobs from the `linux-ci.yml` workflow.

## Package Installation

### DEB Package
```bash
sudo dpkg -i redball_2.1.19_amd64.deb
sudo apt-get install -f  # Fix dependencies if needed
```

### Tarball
```bash
tar -xzf redball-2.1.19-linux-amd64.tar.gz
sudo cp -r usr/* /usr/
sudo glib-compile-schemas /usr/share/glib-2.0/schemas/
```

### Flatpak
```bash
flatpak install redball.flatpak
```

## Requirements

### Build Requirements
- Python 3.9+
- Meson 0.59+
- GTK 4.6+
- libadwaita 1.0+
- gobject-introspection

### Runtime Requirements
- Python 3.9+
- GTK 4.0+
- libadwaita 1.0+
- gobject-introspection
- dbus-python (optional, for enhanced idle detection)

## Troubleshooting

### GSettings schema not found
```bash
sudo glib-compile-schemas /usr/share/glib-2.0/schemas/
```

### Missing GTK4 theme
```bash
sudo apt-get install gnome-themes-extra
```

### AppIndicator not showing
```bash
sudo apt-get install gir1.2-appindicator3-0.1
```

## Differences from Windows Version

| Feature | Windows (WPF) | Linux (GTK4) |
|---------|---------------|--------------|
| UI Framework | WPF | GTK4 + libadwaita |
| Service | Windows Service | systemd user service |
| Keep-Awake | P/Invoke SendInput | xdg-screensaver/idle-inhibit portal |
| TypeThing | HID/CGEvent | AT-SPI / xdotool |
| Tray | NotifyIcon | AppIndicator |
| Notifications | Toast | libnotify |
| Installer | MSI | DEB/Flatpak |

## Contributing

When modifying the Linux build:
1. Test with `./scripts/build-linux.sh`
2. Verify desktop file: `desktop-file-validate data/com.armatec.Redball.desktop`
3. Test installation in a clean VM if possible

## License

GPL-3.0-or-later - Same as the main Redball project.
