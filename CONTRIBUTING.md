# Contributing to Redball

Thank you for your interest in contributing to Redball! This guide will help you get started.

## Code of Conduct

Please read our [Code of Conduct](docs/CODE_OF_CONDUCT.md) before contributing. We are committed to providing a welcoming and inclusive environment.

## Getting Started

### Prerequisites

- **Windows 10+** (or cross-platform build systems via Node/Playwright)
- **.NET 10.0 SDK** (Core development)
- **Node.js 22+** (for Update Server and UI Control Test E2E automation)
- **Git**
- **Playwright** (for E2E tests)

### Development Setup

```bash
# Clone the repository
git clone https://github.com/ArMaTeC/Redball.git
cd Redball

# Build the WPF UI
dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj

# Run all tests (Unit, Service, Interop)
dotnet test tests/Redball.Tests.csproj

# Use the unified build script for full validation
./scripts/build.sh all
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

- All new features must include Pester tests (or NUnit/Playwright tests depending on the component)
- All bug fixes should include a regression test
- Run the full suite before submitting: `Invoke-Pester -Path tests\Redball.Tests.ps1` or run the comprehensive `pwsh ./scripts/Get-CodeCoverage.ps1`
- Target: 100% code coverage across statement, branch, and line metrics. No changes will be merged that degrade this standard.

## Pull Request Process

1. Ensure CI checks pass (tests, lint, security scan)
2. Update documentation if behaviour changes
3. Add a CHANGELOG entry under `[Unreleased]`
4. PRs require at least one approving review
5. Squash-merge is preferred for clean history

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
