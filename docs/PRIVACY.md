# Privacy Policy

**Effective Date:** March 2026
**Application:** Redball v3.x (WPF)

## Overview

Redball is a local-only desktop application. It does not collect, transmit, or share any personal data with third parties. All data remains on your machine.

## Data Collection

### What Redball Stores Locally

| Data | Location | Purpose |
| ---- | -------- | ------- |
| Configuration | Registry `HKCU\Software\Redball\UserData` (primary) and `%LocalAppData%\Redball\UserData\Redball.json` (local copy) | User preferences and settings |
| Session state | `Redball.state.json` | Resume after restart |
| Log file | `Redball.log` | Troubleshooting and diagnostics |
| Crash flag | `Redball.crash.flag` | Crash recovery detection |

### What Redball Does NOT Collect

- No personal information (name, email, IP address)
- No usage analytics transmitted to any server
- No clipboard contents stored or logged (TypeThing reads clipboard in-memory only)
- No keystrokes recorded or logged
- No screenshots or screen content captured
- No network traffic monitored or recorded

## Network Requests

Redball makes the following outbound network requests **only when explicitly triggered by the user**:

| Request | Destination | Trigger |
| ------- | ----------- | ------- |
| Update check | `api.github.com` | User clicks "Check for Updates" or enables automatic update checks |
| Update download | `github.com` (Releases) | User clicks "Download & Update" |

If automatic update checks are enabled, background checks occur at the configured interval.

## Telemetry

Redball includes an opt-in telemetry feature (`EnableTelemetry` in config). When enabled:

- Events are **logged locally only** to the log file
- No data is transmitted to any external server
- Telemetry can be disabled at any time by setting `EnableTelemetry` to `false`

## Third-Party Services

| Service | Usage | Data Shared |
| ------- | ----- | ----------- |
| GitHub API | Update checks | HTTP User-Agent header ("Redball-Updater") |
| SignPath | MSI signing (CI/CD only) | Build artifacts only, no user data |

## Data Retention

- Log files rotate automatically at the configured size limit (default: 10 MB)
- Session state files are deleted after successful restore
- Crash flags are cleared on clean startup
- No data is retained after uninstallation (except files in the install directory)

## Your Rights

Since all data is stored locally on your machine, you have full control:

- **Access**: Open any `.json` or `.log` file in a text editor
- **Delete**: Remove any data file at any time
- **Portability**: Export/import config as JSON or copy `%LocalAppData%\Redball\UserData\Redball.json`
- **Opt-out**: Disable telemetry and performance metrics in settings

## Changes to This Policy

Changes to this privacy policy will be documented in the [CHANGELOG](CHANGELOG.md) and included in release notes.

## Contact

For privacy-related questions: [GitHub Issues](https://github.com/ArMaTeC/Redball/issues)
