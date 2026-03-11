# Third-Party Notices

This file contains the licenses and notices for third-party software used by Redball.

## Runtime Dependencies

### Windows APIs (SetThreadExecutionState, SendInput, RegisterHotKey)

- **Source:** Microsoft Windows SDK
- **License:** Microsoft Software License Terms (included with Windows)
- **Usage:** Keep-awake power state management, simulated keyboard input, global hotkey registration

### .NET Framework / .NET Runtime

- **Source:** Microsoft
- **License:** MIT License
- **URL:** <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>
- **Usage:** System.Windows.Forms, System.Drawing (GDI+), System.Runtime.InteropServices

### Windows Runtime (WinRT)

- **Source:** Microsoft
- **License:** Microsoft Software License Terms
- **Usage:** Toast notifications on Windows 10/11

## Build Dependencies

### WiX Toolset v4

- **Source:** WiX Toolset Team
- **License:** Microsoft Reciprocal License (MS-RL)
- **URL:** <https://wixtoolset.org/>
- **Usage:** MSI installer generation

### ps2exe

- **Source:** Markus Scholtes
- **License:** MIT License
- **URL:** <https://github.com/MScholtes/PS2EXE>
- **Usage:** Optional EXE compilation from PowerShell script

### SignPath

- **Source:** SignPath GmbH
- **License:** Commercial (free for OSS)
- **URL:** <https://signpath.io/>
- **Usage:** Code signing of MSI installers in CI/CD

## Test Dependencies

### Pester

- **Source:** Pester Team
- **License:** Apache License 2.0
- **URL:** <https://github.com/pester/Pester>
- **Usage:** PowerShell unit testing framework

### PSScriptAnalyzer

- **Source:** Microsoft
- **License:** MIT License
- **URL:** <https://github.com/PowerShell/PSScriptAnalyzer>
- **Usage:** PowerShell static analysis and linting

## CI/CD Dependencies

### GitHub Actions

- **actions/checkout@v4** — MIT License
- **actions/upload-artifact@v4** — MIT License
- **actions/download-artifact@v4** — MIT License
- **actions/setup-dotnet@v4** — MIT License
- **softprops/action-gh-release@v2** — MIT License
- **signpath/github-action-submit-signing-request@v1** — Apache License 2.0

## Icon and Assets

- **Redball Icon** — Custom GDI+ rendered 3D sphere, created by the Redball project
- **No third-party icon assets are used**
