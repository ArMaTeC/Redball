# Redball Wiki

> A professional clipboard typer that keeps your computer awake — type anything, anywhere.

Welcome to the Redball wiki — the complete documentation for the TypeThing clipboard automation tool and its companion keep-awake utility. Cross-platform support for Windows and Linux.

## Quick Start

New to Redball? Start here:

1. **[Getting Started](Getting-Started)** — Installation, first run, and your first TypeThing typing session
2. **[TypeThing — Clipboard Typer](TypeThing)** — Complete guide to the flagship clipboard typing feature
3. **[Keep-Awake Guide](KeepAwake)** — Smart system monitoring to prevent Windows sleep (secondary feature)

## Contents

### Core Features

- **[TypeThing — Clipboard Typer](TypeThing)** — Full documentation for the flagship clipboard typing feature
  - Global hotkeys, typing speed, human-like delays
  - Unicode support, newline handling, emergency stop
  - Text-to-speech, HID/Driver-level support
- **[Tray Menu & Keyboard Shortcuts](Tray-Menu-and-Shortcuts)** — UI interaction guide with TypeThing hotkeys
- **[Settings GUI](Settings-GUI)** — Main window navigation and all settings sections

### Configuration & Reference

- **[Configuration](Configuration)** — Full settings reference and persistence behavior
- **[Monitoring & Smart Features](Monitoring-and-Smart-Features)** — Battery, network, idle, schedule, presentation, thermal, process watcher, VPN, session lock, and more
- **[Keep-Awake Guide](KeepAwake)** — Dedicated guide for the keep-awake utility (secondary feature)

### Development & Advanced

- **[API Reference](API-Reference)** — C# service API with properties, methods, and events
- **[Auto-Updater & Code Signing](Updates-and-Signing)** — Update system and digital signatures
- **[Building & CI/CD](Building-and-CICD)** — MSI installer, build pipeline, and GitHub Actions
- **[Architecture](Architecture)** — Component flow, state management, and internals
- **[Localization](Localization)** — i18n system and adding new languages
- **[Contributing](Contributing)** — How to contribute to Redball

### Support

- **[Troubleshooting](Troubleshooting)** — Common issues and solutions

## Feature Overview

### TypeThing — Primary Feature

TypeThing reads text from your clipboard and types it character-by-character using the Windows `SendInput` API. Perfect for:

- Remote desktop sessions that block Ctrl+V
- Secure terminals and password fields
- Virtual machines and nested RDP
- Web forms with paste restrictions
- Any application where standard paste doesn't work

**Default Hotkeys:**

- `Ctrl+Shift+V` — Start typing clipboard contents
- `Ctrl+Shift+X` — Emergency stop typing

### Keep-Awake — Secondary Feature

A smart utility that prevents Windows from sleeping using `SetThreadExecutionState`. Includes:

- Battery-aware, network-aware, idle detection
- Scheduled operation and presentation mode detection
- Thermal protection and VPN auto-activation

## Quick Links

| Topic                                                                          | Description                               |
| ------------------------------------------------------------------------------ | ----------------------------------------- |
| [Getting Started](Getting-Started)                                             | Installation and first run                |
| [TypeThing](TypeThing)                                                         | Clipboard typing feature                  |
| [KeepAwake](KeepAwake)                                                         | Keep-awake utility                        |
| [Configuration](Configuration)                                                 | Registry + local config storage reference |
| [CHANGELOG.md](https://github.com/ArMaTeC/Redball/blob/main/docs/CHANGELOG.md) | Version history                           |
| [LICENSE](https://github.com/ArMaTeC/Redball/blob/main/LICENSE)                | MIT License                               |

## Version

See [Releases](https://github.com/ArMaTeC/Redball/releases) for the latest version.
