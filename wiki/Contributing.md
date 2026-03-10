# Contributing

## How to Contribute

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Run tests (`Invoke-Pester -Path .\Redball.Tests.ps1 -Output Detailed`)
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

## Development Setup

```powershell
git clone https://github.com/karl-lawrence/Redball.git
cd Redball

# Run in development mode
.\Redball.ps1 -ConfigPath ".\Redball.json"

# Run tests
Invoke-Pester -Path ".\Redball.Tests.ps1" -Output Detailed

# Build installer locally
.\installer\Deploy-Redball.ps1 -BuildMsi
```

## Code Style

- Follow existing PowerShell conventions used in the codebase
- Use `PascalCase` for function names following PowerShell verb-noun convention
- Use `$script:` scope for module-level state and configuration
- Include `[CmdletBinding()]` and `SupportsShouldProcess` on functions that modify state
- Add `.SYNOPSIS`, `.DESCRIPTION`, `.PARAMETER`, and `.EXAMPLE` comment-based help for all public functions
- Use `Write-RedballLog` for all logging (never `Write-Host`)

## Testing

The project uses [Pester](https://pester.dev/) for testing.

```powershell
# Install Pester if needed
Install-Module Pester -Force -SkipPublisherCheck

# Run all tests
Invoke-Pester -Path ".\Redball.Tests.ps1"

# Run with detailed output
Invoke-Pester -Path ".\Redball.Tests.ps1" -Output Detailed

# Run specific test block
Invoke-Pester -Path ".\Redball.Tests.ps1" -TestName "*Icon*"
```

When adding new features, include corresponding Pester tests in `Redball.Tests.ps1`.

## CI Pipeline

All PRs are validated by GitHub Actions (`ci.yml`):

- **Pester Tests** — Full test suite
- **PSScriptAnalyzer** — PowerShell linting
- **JSON Validation** — Config and locale files
- **Security Scan** — Basic security checks

Ensure all checks pass before requesting review.

## Adding New Features

### Adding a New Monitoring Feature

1. Add config settings to `$script:config` defaults
2. Add state tracking properties to `$script:state`
3. Create `Update-*State` function following the existing pattern (auto-pause/resume)
4. Add the check to the duration timer tick handler
5. Add UI controls to the Settings dialog
6. Add settings apply/save logic in the Settings OK handler
7. Add a tray menu item if needed
8. Update the wiki documentation
9. Add Pester tests

### Adding a New Locale

1. Add the locale block to `$script:embeddedLocales` JSON
2. Add the locale code to the Settings dialog dropdown
3. Optionally add to `locales.json` for external override
4. See [Localization](Localization.md) for details

### Adding TypeThing Features

1. Add config settings to the `TypeThing` section of `$script:config`
2. Add state properties to the `TypeThing` section of `$script:state`
3. Update both the main Settings dialog TypeThing tab and the dedicated TypeThing settings dialog
4. See [TypeThing](TypeThing.md) for architecture details

## Reporting Issues

- Use [GitHub Issues](https://github.com/karl-lawrence/Redball/issues) to report bugs
- Include:
  - Redball version (`.\Redball.ps1 -Status | ConvertFrom-Json | Select Version`)
  - PowerShell version (`$PSVersionTable.PSVersion`)
  - Windows version
  - Relevant log entries from `Redball.log`
  - Steps to reproduce

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
