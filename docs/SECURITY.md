# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 3.x     | :white_check_mark: |
| < 3.0   | :x:                |

## Reporting a Vulnerability

We take security seriously. If you discover a security vulnerability in Redball, please report it responsibly.

### How to Report

1. **DO NOT** open a public GitHub issue for security vulnerabilities
2. Email your report to: **<security@gcinetworksolutions.com>**
3. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

### What to Expect

- **Acknowledgment** within 48 hours
- **Assessment** within 7 days
- **Fix timeline** communicated within 14 days
- **Credit** in the release notes (unless you prefer anonymity)

## Security Features

### Code Signing

- Redball release artifacts are Authenticode signed in CI/CD
- MSI installers are signed via [SignPath](https://signpath.io) in CI/CD
- Update downloads can optionally verify digital signatures (`VerifyUpdateSignature` config)

### Update Security

- Updates are fetched exclusively over HTTPS from GitHub API
- Downloaded files can be verified against Authenticode signatures
- Backup of current version is created before applying updates

### Data Handling

- Configuration is stored locally in registry `HKCU\Software\Redball\UserData` with file copy at `%LocalAppData%\Redball\UserData\Redball.json`
- Log files are stored locally with configurable rotation
- No credentials or sensitive data are stored in configuration
- Telemetry is opt-in and logged locally only (never transmitted)

### Instance Security

- Singleton mutex prevents duplicate instances
- Named mutex uses `Global\` prefix for session-wide protection

## Security Best Practices for Users

1. **Verify downloads** — Always download from official [GitHub Releases](https://github.com/ArMaTeC/Redball/releases)
2. **Enable signature verification** — Set `VerifyUpdateSignature` to `true` in config
3. **Keep updated** — Use the built-in auto-updater or check for updates regularly
4. **Review config permissions** — Ensure your user profile data under `%LocalAppData%\Redball\UserData` is writable only by your account

## Threat Model

| Threat | Mitigation |
| ------ | ---------- |
| Tampered configuration file | Validation and self-healing normalization on load |
| Malicious update injection | HTTPS-only downloads, optional signature verification |
| Credential exposure in logs | Sensitive data redacted from log output |
| Unauthorized instance control | Named mutex singleton with session isolation |
| Input hook misuse | Defensive Interception hook initialization and deterministic cleanup |
