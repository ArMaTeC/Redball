# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 2.1.x   | :white_check_mark: |
| 2.0.x   | :white_check_mark: |
| < 2.0   | :x:                |

## Reporting a Vulnerability

If you discover a security vulnerability in Redball, please report it responsibly:

1. **Do NOT open a public GitHub issue** for security vulnerabilities.
2. **Email:** Send details to the repository owner via GitHub's private vulnerability reporting feature.
3. **GitHub Security Advisories:** Use [GitHub's security advisory feature](https://github.com/ArMaTeC/Redball/security/advisories/new) to report privately.

### What to Include

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

### Response Timeline

- **Acknowledgment:** Within 48 hours
- **Assessment:** Within 7 days
- **Fix/Patch:** Within 30 days for critical issues

## Security Measures

### Code Integrity

- All releases are built via GitHub Actions CI/CD
- PSScriptAnalyzer linting on every commit
- Security scan checks for hardcoded credentials and `Invoke-Expression` usage
- Optional Authenticode code signing for scripts and MSI installers

### Data Protection

- All data is stored locally — no cloud transmission
- Clipboard data (TypeThing) is processed in-memory only and cleared after use
- No keystrokes are logged or recorded
- Log files contain only operational data, never sensitive user content

### Network Security

- TLS 1.2+ enforced for all HTTPS connections
- Network requests only made when explicitly triggered by user (update checks)
- No background telemetry or phone-home functionality

### Runtime Security

- Singleton mutex prevents unauthorized duplicate instances
- Named pipe IPC uses local-only connections
- Crash recovery detects and recovers from abnormal termination

## Best Practices for Users

1. **Download from official sources only** — Use GitHub Releases or the official repository
2. **Verify signatures** — Enable `VerifyUpdateSignature` in settings for signed updates
3. **Review config changes** — `Redball.json` is human-readable; review after updates
4. **Keep updated** — Security fixes are included in patch releases
