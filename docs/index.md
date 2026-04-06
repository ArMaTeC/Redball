# Redball API Documentation

Welcome to the Redball API documentation. This site provides detailed information about the classes, interfaces, and methods available in the Redball solution.

## Overview

Redball v3.x is a **clipboard automation tool** for Windows with a companion keep-awake utility. Built as a native WPF desktop application (.NET 10), its flagship feature **TypeThing** simulates human-like typing of clipboard contents into any application.

## Primary Feature: TypeThing

The TypeThing service (`TypeThingService`) provides clipboard typing automation:

- **SendInput API** — Native Windows API with `KEYEVENTF_UNICODE` for full Unicode support
- **Global Hotkeys** — Configurable start/stop hotkeys via `HotkeyService`
- **Human-like Delays** — Random keystroke timing for natural typing simulation
- **Progress Tracking** — Real-time typing progress and status
- **Text-to-Speech** — Optional TTS integration via `TextToSpeechService`
- **HID/Driver Support** — Windows Service mode for elevated environments

## Secondary Feature: Keep-Awake

The keep-awake service (`KeepAwakeService`) prevents Windows sleep:

- **SetThreadExecutionState** — Windows API for sleep prevention
- **Heartbeat Keypress** — Invisible F13–F16 via `SendInput`
- **Smart Monitoring** — Battery, network, idle, schedule, presentation detection

## Namespaces

- **Redball.UI.Services** - Core services (TypeThing, KeepAwake, Config, Analytics, Security, Performance, etc.)
- **Redball.UI.ViewModels** - MVVM view models
- **Redball.UI.Views** - WPF views and windows
- **Redball.UI.Models** - Data models and configuration
- **Redball.Core** - Cross-platform core utilities and sync infrastructure

## Key Services

### Primary (TypeThing)

| Service | Purpose |
| ------- | ------- |
| `TypeThingService` | Core clipboard typing engine |
| `HotkeyService` | Global hotkey registration |
| `TextToSpeechService` | TTS for TypeThing |

### Secondary (Keep-Awake)

| Service | Purpose |
| ------- | ------- |
| `KeepAwakeService` | Keep-awake engine |
| `BatteryMonitorService` | WMI battery monitoring |
| `NetworkMonitorService` | Network connectivity monitoring |
| `IdleDetectionService` | GetLastInputInfo idle detection |
| `ScheduleService` | Time/day scheduled activation |
| `PresentationModeService` | PowerPoint/Teams/Windows detection |
| `TemperatureMonitorService` | CPU thermal protection |

### Shared Infrastructure

| Service | Purpose |
| ------- | ------- |
| `SecurityService` | Tamper detection, threat model, CI gates |
| `SecretManagerService` | Windows Credential Manager integration |
| `ConfigService` | JSON config with export/import/validation |
| `AnalyticsService` | Session tracking and feature metrics |
| `UpdateService` | GitHub release auto-updater |
| `NotificationService` | Tray/toast notifications |
| `LocalizationService` | i18n (en, es, fr, de, bl) |

## Contributing

See the [GitHub repository](https://github.com/ArMaTeC/Redball) for contribution guidelines.

## Documentation

- [Wiki Home](https://github.com/ArMaTeC/Redball/wiki) — User documentation
- [TypeThing Guide](https://github.com/ArMaTeC/Redball/wiki/TypeThing) — Primary feature documentation
- [KeepAwake Guide](https://github.com/ArMaTeC/Redball/wiki/KeepAwake) — Secondary feature documentation
