# Redball v3.0 WPF/MAUI UI Modernization Guide

## Overview

Redball v3.0 introduces a modern WPF UI layer that works alongside the existing PowerShell core. This hybrid architecture maintains backward compatibility while providing a modern user experience.

## Architecture

```
┌─────────────────────────────────────────────┐
│           Redball v3.0                      │
├─────────────────────────────────────────────┤
│  WPF UI Layer (Redball.UI.WPF)             │
│  ├─ Modern tray icon (Hardcodet.NotifyIcon)  │
│  ├─ Fluent Design themes                    │
│  ├─ Settings dialog with tabs               │
│  └─ Named pipe client                       │
├─────────────────────────────────────────────┤
│  IPC Bridge (Named Pipes)                   │
│  ├─ JSON message protocol                   │
│  └─ Async bidirectional communication        │
├─────────────────────────────────────────────┤
│  PowerShell Core (Redball.ps1)               │
│  ├─ Existing business logic                 │
│  ├─ Keep-awake functionality                │
│  ├─ TypeThing clipboard typer               │
│  └─ Named pipe server                       │
└─────────────────────────────────────────────┘
```

## Project Structure

```
src/Redball.UI.WPF/
├── App.xaml                  # Application resources and tray icon
├── App.xaml.cs               # IPC server initialization
├── ThemeManager.cs           # Dark/Light theme management
├── Redball.UI.WPF.csproj     # .NET 8 WPF project file
├── Views/
│   ├── MainWindow.xaml       # Hidden main window (tray-only)
│   ├── MainWindow.xaml.cs
│   ├── SettingsWindow.xaml   # Tabbed settings dialog
│   ├── SettingsWindow.xaml.cs
│   ├── AboutWindow.xaml      # About dialog
│   └── AboutWindow.xaml.cs
├── ViewModels/
│   └── MainViewModel.cs      # MVVM bindings and commands
├── Converters/
│   └── Converters.cs         # Value converters
└── Themes/
    ├── DarkTheme.xaml        # Dark mode colors
    ├── LightTheme.xaml       # Light mode colors
    └── Controls.xaml         # Control styles
```

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- Windows 10 1903+ or Windows 11
- Visual Studio 2022 (optional) or VS Code

### Build

```powershell
# Restore packages
dotnet restore src/Redball.UI.WPF/Redball.UI.WPF.csproj

# Build debug
dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj

# Build release (single file, self-contained)
dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj -c Release
```

### Run

```powershell
# Start PowerShell core with modern UI flag
.\Redball.ps1 -UseModernUI

# Or run WPF UI directly (connects to existing PowerShell instance)
dotnet run --project src/Redball.UI.WPF/Redball.UI.WPF.csproj
```

## Features

### Modern Tray Icon
- Native WPF tray icon with Hardcodet.NotifyIcon
- Custom context menu with status display
- Visual state indicators (Active/Paused)

### Fluent Design System
- Dark and Light themes
- Consistent color palette (Redball brand)
- Modern control templates (buttons, checkboxes, sliders)
- Acrylic/glass effects (Windows 10/11)

### Tabbed Settings Dialog
- General (Theme, Language)
- Behavior (Sleep prevention, Heartbeat)
- Features (Battery, Network, Idle, Schedule)
- TypeThing (Hotkeys, typing settings)
- Updates (Channel selection)
- About (Version, GitHub link)

### IPC Communication
- Named pipe: `\\.\pipe\RedballUI`
- JSON message protocol
- Actions: GetStatus, SetActive, ShowSettings

## Migration from v2.x

### For Users
1. Existing `Redball.ps1` continues to work unchanged
2. New `-UseModernUI` parameter enables WPF interface
3. Settings automatically migrate from JSON config

### For Developers
1. PowerShell functions remain the core business logic
2. UI layer communicates via named pipes
3. Config file format unchanged

## Roadmap Items

### Completed ✅
- [x] WPF project structure
- [x] Modern tray icon
- [x] Dark/Light themes
- [x] Settings dialog
- [x] Named pipe IPC

### In Progress 🚧
- [ ] PowerShell named pipe server
- [ ] Config synchronization
- [ ] Update integration

### Future 🔮
- [ ] MAUI cross-platform support
- [ ] Acrylic background effects
- [ ] Advanced animations
- [ ] Plugin architecture

## Technical Notes

### Why WPF + PowerShell?
- Keep proven PowerShell core (well-tested, portable)
- Add modern UI without rewriting business logic
- Easier testing and debugging
- Backward compatibility

### Why Not WinUI 3?
- WPF has better PowerShell integration
- WinUI 3 requires packaged apps for some features
- WPF has mature third-party libraries (Hardcodet.NotifyIcon)

### Security
- Named pipes are local machine only
- JSON serialization with System.Text.Json
- No remote code execution possible

## Contributing

See [CONTRIBUTING.md](../CONTRIBUTING.md) for guidelines on contributing to the v3.0 modernization effort.

## License

MIT License - See [LICENSE](../LICENSE) file.
