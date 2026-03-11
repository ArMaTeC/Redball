# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 2.0.x   | :white_check_mark: |
| < 2.0   | :x:                |

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

- Redball supports Authenticode code signing via `Set-AuthenticodeSignature`
- MSI installers are signed via [SignPath](https://signpath.io) in CI/CD
- Update downloads can optionally verify digital signatures (`VerifyUpdateSignature` config)

### Update Security

- Updates are fetched exclusively over HTTPS from GitHub API
- Downloaded files can be verified against Authenticode signatures
- Backup of current version is created before applying updates

### Data Handling

- All configuration is stored locally in `Redball.json` (no cloud sync)
- Log files are stored locally with configurable rotation
- No credentials or sensitive data are stored in configuration
- Telemetry is opt-in and logged locally only (never transmitted)

### Instance Security

- Singleton mutex prevents duplicate instances
- Named mutex uses `Global\` prefix for session-wide protection

## Security Best Practices for Users

1. **Verify downloads** — Always download from official [GitHub Releases](https://github.com/ArMaTeC/Redball/releases)
2. **Enable signature verification** — Set `VerifyUpdateSignature` to `true` in config
3. **Review execution policy** — Use `RemoteSigned` or `AllSigned` execution policy
4. **Keep updated** — Use the built-in auto-updater or check for updates regularly
5. **Review config permissions** — Ensure `Redball.json` is only writable by your user account

## Threat Model

| Threat | Mitigation |
| ------ | ---------- |
| Tampered configuration file | Config integrity hash verification |
| Malicious update injection | HTTPS-only downloads, optional signature verification |
| Credential exposure in logs | Sensitive data redacted from log output |
| Unauthorized instance control | Named mutex singleton with session isolation |
| DLL injection via Add-Type | Inline C# compiled at runtime, no external DLLs loaded |
