# Third-Party Notices

This file contains the licenses and notices for third-party software used by Redball.

## Runtime Dependencies

### Windows APIs (SetThreadExecutionState, SendInput, RegisterHotKey)

- **Source:** Microsoft Windows SDK
- **License:** Microsoft Software License Terms (included with Windows)
- **Usage:** Keep-awake power state management, simulated keyboard input, global hotkey registration

### .NET 10 Runtime

- **Source:** Microsoft
- **License:** MIT License
- **URL:** <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>
- **Usage:** WPF runtime, base class libraries, and platform interop support

### Windows Runtime (WinRT)

- **Source:** Microsoft
- **License:** Microsoft Software License Terms
- **Usage:** Toast notifications on Windows 10/11

## Build Dependencies

### NSIS (Nullsoft Scriptable Install System)

- **Source:** NSIS Team
- **License:** zlib/libpng License
- **URL:** <https://nsis.sourceforge.io/>
- **Usage:** EXE installer generation

### SignPath

- **Source:** SignPath GmbH
- **License:** Commercial (free for OSS)
- **URL:** <https://signpath.io/>
- **Usage:** Code signing of EXE installers in CI/CD

## Test Dependencies

### MSTest

- **Source:** Microsoft
- **License:** MIT License
- **URL:** <https://github.com/microsoft/testfx>
- **Usage:** Unit testing framework for `tests/` and related .NET test projects

### PSScriptAnalyzer

- **Source:** Microsoft
- **License:** MIT License
- **URL:** <https://github.com/PowerShell/PSScriptAnalyzer>
- **Usage:** PowerShell static analysis and linting

## CI/CD Dependencies

### GitHub Actions

- **actions/checkout@v6** — MIT License
- **actions/upload-artifact@v5** — MIT License
- **actions/download-artifact@v5** — MIT License
- **actions/setup-dotnet@v5** — MIT License
- **actions/cache@v5** — MIT License
- **softprops/action-gh-release@v2** — MIT License
- **signpath/github-action-submit-signing-request@v1** — Apache License 2.0

## Icon and Assets

- **Redball Icon** — Custom GDI+ rendered 3D sphere, created by the Redball project
- **No third-party icon assets are used**
