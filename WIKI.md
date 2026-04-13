# Redball Documentation Wiki

Welcome to the Redball Wiki. This document provides a deep dive into the architecture, security, and advanced features of the Redball project.

---

## 1. Architecture Overview

Redball is built as a highly modular, service-oriented WPF application. The core philosophy is "Separation of Concerns" through a robust singleton service layer.

### Core Components
- **Redball.UI.WPF**: The main desktop application. Orchestrates all services, handles the system tray, and provides the user interface.
- **Redball.Core**: Shared logic, models, and low-level Win32 Interop helpers.
- **Redball.Service**: (Optional) Elevated Windows Service for HID-level input support.

### Service Orchestration
The application uses a `ServiceLocator` pattern to manage 40+ specialized services. Each service is responsible for a single domain (e.g., `BatteryMonitorService`, `TypeThingService`).

### Non-Blocking Startup
Redball implements an asynchronous startup pipeline:
1. **Bootstrap**: MUTEX check & Crash recovery.
2. **Background Engine**: Configuration loading and DI building off the UI thread.
3. **Lazy Views**: Main window and secondary UI initialized on-demand to ensure "Instant Tray" appearance.

---

## 2. Advanced Features

### TypeThing — The Human-Like Typer
Unlike standard "paste" operations, TypeThing uses the native `SendInput` API with `KEYEVENTF_UNICODE`. 
- **Adaptive Jitter**: Random delays (ms) between every keypress mimic human typing patterns.
- **HID Support**: Optionally uses a driver-level service to bypass anti-cheat or secure desktop restrictions.

### Keep-Awake Engine
Redball uses a dual-method approach to prevent system sleep:
1. **System Re-assertion**: Periodic calls to `SetThreadExecutionState`.
2. **Synthetic Heartbeat**: Sending invisible `F15` keypresses to prevent idle timers in surveillance tools.
- **Adaptive Jitter**: Heartbeat intervals fluctuate slightly to look organic.

### Intelligent Awareness
Redball is context-aware:
- **Privacy Mode**: Detects active camera streams (Webcam) and automatically pauses keep-awake to protect user status.
- **Gaming Mode**: Detects full-screen DirectX/Vulkan applications and reduces resource usage.
- **Thermal Protection**: Monitors CPU Temps and pauses during high thermal stress.

---

## 3. Security Model

Redball is designed for enterprise-grade security and data integrity.

### Tiered Configuration Protection
Configurations can be optionally encrypted using the Windows Data Protection API (DPAPI), ensuring that only the logged-in user can read the settings file.

### Forward-Secure Auditing
Critical security events (Debugger detection, session changes) are logged using a **SHA-256 Hash-Chaining** algorithm. Each log entry is cryptographically tied to the previous one, making the audit trail immutable.

### Anti-Tamper
The application performs proactive checks for external debuggers and memory injection during sensitive operations.

---

## 4. UI Control Testing

Redball employs a multi-layered testing strategy to ensure all interactive elements behave predictably under diverse conditions.

### Interactive Elements Tracked
- **Main Dashboard**: Pause/Resume buttons, Mode selectors.
- **Settings Forms**: Sliders (Delay, Threshold), Hotkey recorders.
- **Command Palette**: Search input, list navigation.
- **Mini Widget**: Floating window position persistence and preset toggles.

### Test Scenarios
- **Positive Paths**: Successful clipboard parsing and typing simulation.
- **Negative Paths**: Handling empty clipboards, conflicting global hotkeys, and rapid state transitions.
- **State Persistence**: Ensuring UI controls reflect the underlying service state (e.g., greyed out settings during active typing).

### Tooling
- **FlaUI (UIA3)**: Native WPF automation used for simulating mouse clicks and keyboard events in the desktop app.
- **Playwright**: End-to-end automation for the Web Admin dashboard, validating real-time log streaming and build triggers.

---

## 5. Accessibility & Responsiveness

Redball is committed to being an inclusive tool for all users.

### Accessibility Standards
- **WCAG AA Compliance**: All themes undergo contrast ratio validation.
- **Screen Reader Support**: Semantic usage of `AutomationProperties.Name` and `HelpText` on all interactive controls.
- **Keyboard Navigation**: Full Tab-index support and searchable command palette (Ctrl+K) for mouse-less operation.

### Responsiveness
- **DPI-Aware Layouts**: Adaptive scaling (100% - 300%) for high-resolution 4K/8K monitors and multi-monitor setups with mixed scaling.
- **Dynamic Orientation**: Mini-widget and HUD automatically reposition themselves based on valid screen bounds.

---

## 6. Development & Testing

### Test Strategy
We aim for 100% statement and branch coverage.
- **Unit Tests**: MSTest suites for core services.
- **E2E Tests**: Playwright scripts for the Update Server and Admin Dashboard.
- **UI Tests**: FlaUI integration for WPF control validation.

### Build Pipeline
The unified `scripts/build.sh` orchestrates:
1. **Linux-only builds**: Cross-compilation via Wine and .NET SDK.
2. **Setup Generation**: NSIS-based installers with code signing.
3. **Auto-Release**: Integrated publication to GitHub and the Unified Update Server.

---

## 5. Troubleshooting Reference

- **Tray Icon missing?** Restart Explorer or check "Hidden Icons".
- **Typing too fast?** Increase the MaxDelay in TypeThing settings.
- **Updates failing?** Ensure `http://localhost:3500` is reachable or check your channel settings.

---

*Last Updated: April 2026*
