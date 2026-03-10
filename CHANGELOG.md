# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0] - 2024-03-09

### Added

- **Branding**: "Redball" with new red ball icon
- **3D Icon**: Custom-drawn 3D red sphere with specular highlight and shadow effects
- **Color States**: Three distinct icon states:
  - Active: Bright red ball (crimson/tomato gradient)
  - Timed: Orange/red ball (dark orange gradient)
  - Paused: Dark red/gray ball (muted colors)
- **Configuration File Support**: JSON-based configuration with `Redball.json`
- **Structured Logging**: `Write-RedballLog` function with log rotation at 10MB
- **Pester Tests**: Comprehensive test suite covering 40+ test cases
- **Graceful Shutdown**: Pipeline stop exception handling for Ctrl+C/terminal close
- **Error Handling**: `PipelineStoppedException` catches throughout all functions
- **Memory Management**: Proper disposal of GDI+ objects and previous icons
- **Parameter Validation**: `[ValidateRange(1, 720)]` for timer duration
- **Help Documentation**: Full PowerShell help with `.SYNOPSIS`, `.DESCRIPTION`, `.PARAMETER`, `.EXAMPLE`
- **Trap Handler**: Global trap for `PipelineStoppedException` with graceful exit

### Changed

- **Icon System**: Replaced coffee cup design with 3D red ball
- **Function Names**: Renamed `Update-Ui` to `Update-RedballUI`
- **Log Files**: Changed from `Redball.log` to `Redball.log`
- **Config Files**: Changed from `Redball.json` to `Redball.json`
- **UI Text**: Updated all references from "Redball" to "Redball"
- **Tray Tooltip**: Now shows `[REDBALL] Redball`

### Fixed

- **Pipeline Stop Error**: No more "pipeline stopped" exception on external termination
- **$PSScriptRoot Empty**: Added fallback to current directory when `$PSScriptRoot` is empty
- **Memory Leaks**: Proper disposal of `PreviousIcon` prevents GDI+ handle leaks
- **UInt32 Overflow**: Fixed constant definitions to prevent signed integer overflow
- **DateTime Nullability**: Used `[Nullable[datetime]]` for proper null handling

### Security

- **Execution Policy**: Requires `-Version 5.1` and administrative privileges
- **Error Suppression**: Sensitive error details only logged, not displayed to user

## [1.0.0] - 2024-03-01

### Initial Features

- Initial release as "Redball"
- System tray icon with basic functionality
- Keep-awake state using `SetThreadExecutionState` API
- F15 heartbeat keypress
- Duration timer (15, 30, 60, 120 minutes)
- Prevent display sleep toggle
- Basic context menu with pause/resume

[Unreleased]: https://github.com/username/redball/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/username/redball/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/username/redball/releases/tag/v1.0.0
