# Redball API Documentation

Welcome to the Redball API documentation. This site provides detailed information about the classes, interfaces, and methods available in the Redball solution.

## Overview

Redball v3.x is a system tray utility to prevent Windows from going to sleep, built as a native WPF desktop application (.NET 10).

## Getting Started

Browse the API reference to understand the internal architecture and extensibility points.

## Namespaces

- **Redball.UI.Services** - Core services (KeepAwake, Config, Analytics, Security, Performance, etc.)
- **Redball.UI.ViewModels** - MVVM view models
- **Redball.UI.Views** - WPF views and windows
- **Redball.UI.Models** - Data models and configuration
- **Redball.Core** - Cross-platform core utilities and sync infrastructure

## Key Services

| Service | Purpose |
| ------- | ------- |
| `KeepAwakeService` | Core keep-awake engine |
| `SecurityService` | Tamper detection, threat model, CI gates |
| `SecretManagerService` | Windows Credential Manager integration |
| `StartupTimingService` | Startup SLO instrumentation |
| `ResourceBudgetService` | Per-service CPU/RAM budgets |
| `MemoryPressureService` | Memory pressure handling |
| `PerformanceTestService` | Continuous performance testing |
| `RolloutService` | Staged release channels |
| `CommandPaletteService` | Searchable command surface |
| `WindowsShellIntegrationService` | Jump lists, URI protocol |
| `OutboxDispatcherService` | Offline sync with SQLite |
| `CrashTelemetryService` | Privacy-safe crash reporting |
| `AccessibilityService` | WCAG AA compliance |
| `DesignSystemService` | Tokenized design system |

## Contributing

See the [GitHub repository](https://github.com/ArMaTeC/Redball) for contribution guidelines.
