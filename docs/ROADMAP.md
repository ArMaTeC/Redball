# Redball Product Roadmap

## Version History

- **v1.0** - Initial PowerShell implementation
- **v2.0** - WPF UI with basic themes
- **v2.1** - Auto-updater, settings GUI, TypeThing
- **v3.0** - Current: 14 themes, smart monitoring, mini widget presets, analytics, MSI installer, .NET 10, security framework, performance monitoring, staged rollouts

## Current Version: 3.0.x

### Q1 2024 (Active)

- [x] WPF native UI with 12 themes
- [x] Smart monitoring (battery, network, idle)
- [x] TypeThing clipboard typer
- [x] MSI installer with code signing
- [x] Analytics dashboard
- [x] Session restore
- [x] RDP support for TypeThing
- [x] Tray icon robustness improvements

## Roadmap

### Q2 2024 - v3.1.x "Performance & Polish"

**Theme:** System stability and user experience refinements

#### Q2 Features

- [x] **High Contrast Theme Support**
  - Windows accessibility compliance
  - WCAG 2.1 AA color contrast
  - Full accessibility framework
  
- [x] **Auto Theme Switching**
  - Follow Windows light/dark mode changes
  - Scheduled theme transitions
  
- [x] **Performance Optimization**
  - Resource budgets per service
  - Memory pressure handling
  - Startup SLO instrumentation (<1.5s cold)
  - Continuous performance test suite

- [x] **Enhanced Keyboard Navigation**
  - Full tray menu keyboard access (Alt+letter access keys)
  - Tab order optimization
  - Shortcut key customization

#### Technical Debt

- [~] Unit test coverage >80% (in progress - added 54 new tests, coverage improved from 10.5% to 12.0%, ongoing effort)
- [x] E2E testing with FlaUI (Critical Path tests implemented)
- [x] Performance benchmarks in CI

---

### Q3 2024 - v3.2.x "Collaboration & Enterprise"

**Theme:** Team features and admin controls

#### Q3 Features

- [x] **Team Settings Sync**
  - Shared configuration via cloud
  - Organization policies
  
- [x] **Admin Dashboard**
  - Usage reports for IT departments
  - Policy enforcement
  
- [x] **Silent Installation Options**
  - MSI properties for enterprise deployment
  - Registry-based configuration
  
- [x] **Audit Logging**
  - Detailed usage logs
  - Compliance reporting

#### Integrations

- [x] Microsoft Teams status API
- [x] Slack huddle detection
- [x] Zoom meeting detection

---

### Q4 2024 - v4.0.x "Cross-Platform"

**Theme:** Expand beyond Windows

#### Q4 Features

- [x] **macOS port**
  - Native Swift/SwiftUI app
  - Menu bar integration
  - macOS-specific features (Do Not Disturb detection)
  
- [x] **Linux Support**
  - GTK/GNOME tray app
  - systemd integration
  - Wayland compatibility

- [x] **Browser extension**
  - Chrome/Edge/Firefox
  - Web-based keep-awake
  - Sync with desktop app

#### Platform Parity

- [x] Feature parity across all platforms
- [x] Shared configuration format
- [x] Cross-platform analytics

---

### 2025 - v4.x "Intelligence & Automation"

**Theme:** AI-powered features and predictive automation

#### 2025 Features

- [x] **Smart Schedule Learning**
  - Learn user patterns
  - Auto-start based on calendar
  - Predictive battery management

- [x] **Focus Mode Integration**
  - Windows Focus Assist integration
  - ~~Pomodoro timer mode~~ (removed in v3.0)
  - Distraction-free typing

- [x] **Advanced Analytics**
  - Usage predictions
  - Battery life optimization suggestions
  - Productivity insights

#### Experimental

- [ ] Voice command support
- [ ] Gesture detection (camera-based)
- [ ] Integration with smart home devices

---

## Long-Term Vision (2025+)

### Redball Platform

- Open plugin ecosystem
- Third-party integrations marketplace
- Custom automation scripting (Lua/Python)

### Enterprise Suite

- [x] Centralized management console
- [x] SSO integration
- [x] Advanced security policies
- [x] Usage-based licensing

### Mobile Companion

- [x] iOS/Android remote control
- [x] QR code pairing
- [x] Mobile notifications

## Success Metrics

### Adoption

- Target: 100K downloads by end of 2024
- Target: 10K GitHub stars by end of 2024
- Target: 50 contributors by end of 2024

### Quality

- Maintain >4.5 star rating
- <1% crash rate
- <2 second average startup time

### Business

- Break-even on infrastructure costs
- 100+ enterprise trials
- 10 paying enterprise customers

## Contribution Opportunities

### Good First Issues

- Theme color refinements
- Additional language translations
- Documentation improvements

### Major Projects

- [x] **macOS port**
- [x] **Browser extension**
- [x] **Plugin system architecture**

See [CONTRIBUTING.md](../CONTRIBUTING.md) for details on how to get involved.

## Feedback & Prioritization

We use the following criteria to prioritize features:

1. **User Impact** - How many users benefit?
2. **Technical Feasibility** - Can we build it well?
3. **Strategic Alignment** - Does it fit our vision?
4. **Maintenance Burden** - Can we support it long-term?

Submit feature requests via [GitHub Issues](https://github.com/ArMaTeC/Redball/issues).

Redball v3.x is implemented as a pure C# WPF application (.NET 10). The core functionality is organised into services across multiple namespaces:

## Namespaces

- **Redball.UI.Services** - Core services (KeepAwake, Config, Analytics, Security, Performance, etc.)
- **Redball.UI.ViewModels** - MVVM view models
- **Redball.UI.Views** - WPF views and windows
- **Redball.UI.Models** - Data models and configuration
- **Redball.Core** - Cross-platform core utilities and sync infrastructure

## Key Services

| Service                          | Purpose                                      |
| -------------------------------- | -------------------------------------------- |
| `KeepAwakeService`               | Core keep-awake engine                       |
| `SecurityService`                | Tamper detection, threat model, CI gates     |
| `SecretManagerService`           | Windows Credential Manager integration       |
| `StartupTimingService`           | Startup SLO instrumentation                  |
| `ResourceBudgetService`          | Per-service CPU/RAM budgets                  |
| `MemoryPressureService`          | Memory pressure handling                     |
| `PerformanceTestService`         | Continuous performance testing               |
| `RolloutService`                 | Staged release channels                      |
| `CommandPaletteService`          | Searchable command surface                   |
| `WindowsShellIntegrationService` | Jump lists, URI protocol                     |
| `OutboxDispatcherService`        | Offline sync with SQLite                     |
| `CrashTelemetryService`          | Privacy-safe crash reporting                 |
| `AccessibilityService`           | WCAG AA compliance                           |
| `DesignSystemService`            | Tokenized design system                      |
