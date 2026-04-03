# Redball for macOS

Native macOS implementation of Redball using Swift and SwiftUI.

## Project Structure

```text
src/Redball.macOS/
├── Redball.xcodeproj/          # Xcode project
├── Redball/
│   ├── App/
│   │   ├── RedballApp.swift    # App entry point
│   │   └── AppDelegate.swift   # App lifecycle
│   ├── Core/
│   │   ├── KeepAwakeEngine.swift    # IOKit power management
│   │   ├── BatteryMonitor.swift     # Battery status
│   │   ├── IdleDetector.swift       # User idle detection
│   │   └── DoNotDisturb.swift       # DND detection
│   ├── UI/
│   │   ├── MenuBarController.swift  # Menu bar extra
│   │   ├── StatusMenuView.swift     # Dropdown menu
│   │   ├── SettingsWindow.swift     # Preferences
│   │   └── TimerView.swift          # Timer UI
│   ├── Models/
│   │   ├── RedballConfig.swift      # Configuration
│   │   └── SessionState.swift       # Session tracking
│   └── Utils/
│       ├── Logger.swift             # Logging
│       ├── ConfigStorage.swift      # Settings persistence
│       └── AutoUpdater.swift        # Sparkle integration
├── RedballTests/
└── RedballUITests/
```

## Requirements

- macOS 12.0+ (Monterey)
- Xcode 14.0+
- Swift 5.7+

## Key Features

### Menu Bar Integration

- Status bar icon with dropdown menu
- Quick toggle for keep-awake
- Visual state indicators

### Keep-Awake Engine (IOKit)

- `IOPMAssertionCreateWithName` for preventing sleep
- Display sleep control
- System idle prevention

### macOS-Specific Features

- Do Not Disturb detection (Focus modes)
- Battery optimization
- Touch Bar support (if available)
- Shortcuts app integration

### Configuration

- UserDefaults for settings storage
- JSON config import/export (compatible with Windows)

## Building

```bash
cd src/Redball.macOS
xcodebuild -project Redball.xcodeproj -scheme Redball -configuration Release
```

## Platform Parity

| Feature           | Windows       | macOS    | Status |
| ----------------- | ------------- | -------- | ------ |
| Keep-Awake Engine | ✓             | IOKit    | Ready  |
| Menu/Tray         | ✓             | Menu Bar | Ready  |
| Battery Aware     | ✓             | ✓        | Ready  |
| Idle Detection    | ✓             | ✓        | Ready  |
| Timed Sessions    | ✓             | ✓        | Ready  |
| TypeThing         | HID/SendInput | CGEvent  | Ready  |
| Mini Widget       | WPF           | SwiftUI  | Ready  |
| Browser Extension | ✓             | ✓        | Shared |
| Teams Integration | ✓             | N/A*     | N/A    |
| Slack Integration | ✓             | N/A*     | N/A    |
| Zoom Integration  | ✓             | N/A*     | N/A    |

- Teams/Slack/Zoom integrations are Windows-specific. macOS versions use native meeting detection.

## Development Plan

### Phase 1: Core Engine

- [ ] IOKit power assertion wrapper
- [ ] Menu bar controller
- [ ] Basic keep-awake toggle

### Phase 2: UI

- [ ] Settings window
- [ ] Timer UI
- [ ] Status indicators

### Phase 3: Features

- [ ] Battery monitoring
- [ ] Idle detection
- [ ] Do Not Disturb detection
- [ ] Mini widget

### Phase 4: Advanced

- [ ] TypeThing (CGEvent implementation)
- [ ] Auto-updater (Sparkle)
- [ ] Shortcuts integration
