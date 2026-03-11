# Redball Roadmap

> Last updated: March 2026

## Vision

Redball aims to be the most reliable, feature-rich, and user-friendly keep-awake utility for Windows — going beyond simple sleep prevention to become an intelligent power and productivity management tool.

## Value Proposition

**Redball keeps your Windows PC awake — intelligently.** Unlike simple mouse jigglers or basic caffeine-style tools, Redball is context-aware: it knows when you're on battery, when you're presenting, when you're idle, and when your network drops. It adapts automatically so you never have to think about it.

### Why Redball over alternatives?

| Feature | Redball | Caffeine | Mouse Jiggler | PowerToys Awake |
| --------- | --------- | ---------- | --------------- | ----------------- |
| Context-aware (battery, network, idle) | ✅ | ❌ | ❌ | ❌ |
| Scheduled operation | ✅ | ❌ | ❌ | ❌ |
| Presentation mode detection | ✅ | ❌ | ❌ | ❌ |
| Clipboard typer (TypeThing) | ✅ | ❌ | ❌ | ❌ |
| MSI installer | ✅ | ❌ | ✅ | ✅ |
| Auto-updater | ✅ | ❌ | ❌ | ✅ |
| Code signing | ✅ | ✅ | ❌ | ✅ |
| Localization (i18n) | ✅ | ❌ | ❌ | ✅ |
| Single script / portable | ✅ | ❌ | ❌ | ❌ |
| Open source (MIT) | ✅ | ❌ | ✅ | ✅ |

## User Personas

### 1. Corporate Developer — "Alex"

- Works remotely, needs PC awake during long builds and deployments
- Uses VPN that disconnects on sleep; needs network-aware mode
- Wants scheduled operation for work hours only

### 2. IT Administrator — "Sam"  

- Manages multiple workstations, deploys via MSI
- Needs silent install with preconfigured defaults
- Values code signing and update verification

### 3. Presenter — "Jordan"

- Frequently gives demos and presentations via Teams/PowerPoint
- Needs automatic activation during presentations
- Wants clipboard typer for live coding demos

### 4. Power User — "Casey"

- Runs long downloads, renders, or data processing overnight
- Needs battery-aware mode on laptops
- Values CLI automation and JSON status output

## Milestones

### ✅ v2.0 — Feature Complete (Current)

- [x] 3D GDI+ tray icon with state colors
- [x] Battery, network, idle, schedule, presentation awareness
- [x] TypeThing clipboard typer with global hotkeys
- [x] MSI installer with WiX v4
- [x] CI/CD with GitHub Actions
- [x] Auto-updater from GitHub Releases
- [x] Code signing support (Authenticode + SignPath)
- [x] Localization (en, es, fr, de)
- [x] Pester test suite

### ✅ v2.1 — Polish & Reliability (Current)

- [x] First-run onboarding experience
- [x] Config schema validation and integrity checks
- [x] Improved error messages and crash reporting
- [ ] Consistent dark/light theme across all dialogs
- [x] Code coverage thresholds in CI
- [x] Feature usage analytics (opt-in)

### � v2.2 — Enterprise Features (Next)

- [ ] Group Policy / registry-based configuration
- [ ] Multi-monitor awareness
- [ ] Remote management via named pipe IPC
- [ ] Plugin/extension architecture
- [ ] SCCM/Intune deployment guide
- [ ] Centralized logging endpoint

### 🔮 v3.0 — Next Generation (In Progress)

- [x] WPF UI project structure
- [x] Modern tray icon with WPF (Hardcodet.NotifyIcon)
- [x] Fluent Design System themes (Dark/Light)
- [x] Settings dialog with tabbed interface
- [x] Named pipe IPC for PowerShell communication
- [ ] MAUI cross-platform support (Windows/macOS)
- [ ] Acrylic/glass background effects
- [ ] Smooth animations and transitions
- [ ] Full accessibility (WCAG 2.1 AA)
- [ ] PowerShell core integration testing

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to contribute to the roadmap. Feature requests and priority discussions happen in [GitHub Discussions](https://github.com/ArMaTeC/Redball/discussions).
