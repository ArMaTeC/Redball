# Redball v3.0 - WPF/MAUI UI Modernization

## Architecture Overview

Redball v3.0 introduces a hybrid architecture:

- **Core**: PowerShell backend (proven, well-tested)
- **UI Layer**: Modern WPF/MAUI frontend with native Windows integration
- **Communication**: Named pipes for PowerShell ↔ UI communication

## Project Structure

```text
Redball/
├── src/
│   ├── Redball.Core/           # PowerShell core (existing)
│   ├── Redball.UI.WPF/        # WPF modern UI
│   ├── Redball.UI.MAUI/       # MAUI cross-platform UI (future)
│   └── Redball.Common/        # Shared interfaces
├── ui/
│   ├── themes/                # Modern theme definitions
│   ├── components/            # Reusable UI components
│   └── assets/                # Icons, images, fonts
└── docs/
    └── ui-migration.md        # Migration guide
```

## Key Features

### WPF UI (v3.0)

- Modern Fluent Design System
- Acrylic/Glass backgrounds
- Smooth animations
- High DPI support
- Accessibility (WCAG 2.1 AA)

### Hybrid Communication

- Named pipe IPC
- JSON message protocol
- Async operations
- Error handling

## Getting Started

### Prerequisites

- .NET 8.0 or later
- Windows 10 1903+ (for WinUI 3 APIs)
- Visual Studio 2022 or VS Code

### Build

```powershell
# Build WPF UI
dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj

# Run with PowerShell core
./Redball.ps1 -UseModernUI
```
