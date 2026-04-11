# Contributing

## How to Contribute

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Build and test (`dotnet build src/Redball.UI.WPF && dotnet test`)
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

## Development Setup

```powershell
git clone https://github.com/ArMaTeC/Redball.git
cd Redball

# Build the WPF application
dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj

# Run in development mode
dotnet run --project src/Redball.UI.WPF/Redball.UI.WPF.csproj

# Run unit tests
dotnet test

# Full build pipeline (build, test, lint, NSIS installer)
pwsh -File scripts/build.ps1 all

# Build Windows artifacts only
pwsh -File scripts/build.ps1 windows
```

**Linux/Wine Setup (for building Windows on Linux):**

```bash
# Build everything (Windows artifacts via Wine, update-server, website)
./scripts/build.sh all

# Build Windows artifacts only (requires Wine + .NET SDK setup)
./scripts/build.sh windows
```

## Code Style

- Follow existing C# conventions used in the codebase
- Use `PascalCase` for public members, `_camelCase` for private fields
- Services are singletons accessed via `ServiceName.Instance`
- Use `Logger.Info/Warning/Error` for all logging
- Use XML doc comments (`///`) for public API documentation
- Keep services focused — one responsibility per service class

## Testing

The project uses MSTest for unit testing:

```powershell
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity detailed

# Run specific test class
dotnet test --filter "ClassName=ConfigServiceTests"
```

Tests are located in the `tests/` directory. When adding new features, include corresponding unit tests.

## CI Pipeline

All PRs are validated by GitHub Actions (`ci.yml`):

- **WPF Build** — Build the .NET 10 WPF application
- **Unit Tests** — Run the full test suite
- **PSScriptAnalyzer** — Lint build scripts
- **JSON Validation** — Config and locale files
- **Security Scan** — Basic security checks

Ensure all checks pass before requesting review.

## Adding New Features

### Adding a New Monitoring Feature

1. Add config properties to `Models/RedballConfig.cs`
2. Create a new service class in `Services/` following the singleton pattern
3. Implement `CheckAndUpdate(KeepAwakeService service)` method
4. Register the check in `KeepAwakeService`'s duration timer
5. Add UI controls to the appropriate section in `MainWindow.xaml`
6. Add settings apply/save logic in `MainWindow.Settings.cs`
7. Add a tray menu item if needed
8. Update the wiki documentation
9. Add unit tests

### Adding a New Locale

1. Add the locale strings to `LocalizationService`
2. Optionally add to `locales.json` for external override
3. See [Localization](Localization.md) for details

### Adding TypeThing Features

1. Add config properties to the TypeThing section of `RedballConfig.cs`
2. Update `MainWindow.TypeThing.cs` with the new functionality
3. Update the TypeThing section UI in `MainWindow.xaml`
4. See [TypeThing](TypeThing.md) for architecture details

## Reporting Issues

- Use [GitHub Issues](https://github.com/ArMaTeC/Redball/issues) to report bugs
- Include:
  - Redball version (from **About** dialog or Diagnostics section)
  - Windows version
  - Relevant log entries (export via Diagnostics → Export Diagnostics)
  - Steps to reproduce

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
