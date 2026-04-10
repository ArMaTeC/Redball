# Contributing to Redball

Thank you for your interest in contributing to Redball! This guide will help you get started.

## Code of Conduct

Please read our [Code of Conduct](docs/CODE_OF_CONDUCT.md) before contributing. We are committed to providing a welcoming and inclusive environment.

## Getting Started

### Prerequisites

- **Windows 8.1+**
- **PowerShell 5.1+** (PowerShell 7+ recommended)
- **.NET 8.0 SDK** (for WPF UI development)
- **Git**
- **Pester 5.5.0** (for running tests)
- **PSScriptAnalyzer** (for linting)

### Development Setup

```powershell
# Clone the repository
git clone https://github.com/ArMaTeC/Redball.git
cd Redball

# Install test dependencies
Install-Module Pester -RequiredVersion 5.5.0 -Force -SkipPublisherCheck
Install-Module PSScriptAnalyzer -Force

# Run the PowerShell core
.\Redball.ps1

# Build the WPF UI
dotnet build src\Redball.UI.WPF\Redball.UI.WPF.csproj

# Run all tests
Invoke-Pester -Path tests\Redball.Tests.ps1 -Output Detailed

# Run linter
Invoke-ScriptAnalyzer -Path .\Redball.ps1 -Severity Warning,Error
```

## How to Contribute

### Reporting Bugs

1. Check [existing issues](https://github.com/ArMaTeC/Redball/issues) first
2. Use the bug report template
3. Include:
   - Windows version and PowerShell version
   - Steps to reproduce
   - Expected vs. actual behaviour
   - Log file contents (if relevant)

### Suggesting Features

1. Open a [feature request](https://github.com/ArMaTeC/Redball/issues/new)
2. Describe the use case and expected behaviour
3. Explain why existing features don't cover it

### Submitting Code

1. **Fork** the repository
2. **Create a branch** from `main`: `git checkout -b feature/my-feature`
3. **Make your changes** following the coding standards below
4. **Add tests** for new functionality
5. **Run the full test suite**: `.\build.ps1 -SkipWPF -SkipMSI`
6. **Commit** with a clear message: `git commit -m "Add: brief description"`
7. **Push** to your fork: `git push origin feature/my-feature`
8. **Open a Pull Request** against `main`

## Coding Standards

### PowerShell (Redball.ps1)

- Follow [PSScriptAnalyzer](https://github.com/PowerShell/PSScriptAnalyzer) recommendations
- Use `Verb-Noun` naming for functions (approved verbs only)
- Add comment-based help (`.SYNOPSIS`, `.DESCRIPTION`, `.PARAMETER`) to all public functions
- Use `[CmdletBinding()]` on functions that modify state
- Log all state changes via `Write-RedballLog`
- Handle errors gracefully — never let exceptions bubble to the user

### C# / WPF (src/Redball.UI.WPF)

- Follow standard C# naming conventions (PascalCase for public, camelCase for private)
- Use MVVM pattern — keep code-behind minimal
- Use `DynamicResource` for theme-dependent brush references
- Add XML doc comments to public classes and methods
- Ensure all new UI elements support both Dark and Light themes

### General

- No hardcoded credentials or secrets
- No `Invoke-Expression` with user-supplied input
- Keep commits focused — one feature or fix per commit
- Write descriptive commit messages

## Commit Message Format

```text
Type: Brief description

Detailed explanation if needed.

Fixes #123
```

**Types:** `Add`, `Fix`, `Update`, `Remove`, `Refactor`, `Test`, `Docs`, `CI`

## Testing

- All new features must include Pester tests
- All bug fixes should include a regression test
- Run the full suite before submitting: `Invoke-Pester -Path tests\Redball.Tests.ps1`
- Target: 40%+ code coverage (current baseline)

## Pull Request Process

1. Ensure CI checks pass (tests, lint, security scan)
2. Update documentation if behaviour changes
3. Add a CHANGELOG entry under `[Unreleased]`
4. PRs require at least one approving review
5. Squash-merge is preferred for clean history

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
