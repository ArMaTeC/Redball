# Project Snapshot

**Project:** `/root/Redball` (Redball)
**Detected root:** `/root/Redball`
**Primary stack:** C# 13 / .NET 10 WPF desktop application
**Secondary stacks:** Node.js/Express update server, Node.js/Express web admin, browser extension, PowerShell/NSIS installer

## Top-level layout

```text
/root/Redball
├── src/
│   ├── Redball.Core/           .NET 10 class library (SQLite, telemetry, sync, security)
│   ├── Redball.UI.WPF/         .NET 10 WPF desktop app (WinExe, net10.0-windows)
│   ├── Redball.Service/        Windows Service components
│   └── Redball.SessionHelper/  Session helper executable
├── tests/                      xUnit + BenchmarkDotNet
├── tests-e2e/
├── tests-integration/
├── tests-ui-automation/
├── tests-ui-headless/
├── web-admin/                  Node.js/Express admin dashboard
├── update-server/              Node.js/Express release/update server
├── browser-extension/          Chrome/Firefox extension (manifest v2 style)
├── installer/                  NSIS installer scripts
├── scripts/                    PowerShell build/version scripts
├── docs/                       Markdown documentation
├── wiki/                       GitHub wiki content
└── AGENT.md                    Local agent guidelines
```

## Build / test commands

### .NET

- Build: `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj`
- Full solution: `dotnet build Redball.v3.sln`
- Run tests: `dotnet test tests/Redball.Tests.csproj`
- Full pipeline: `powershell -File scripts/build.ps1`

### Node.js

- Update server:
  - `cd /root/Redball/update-server && npm install && npm test` (test script present)
  - Start: `node server.js`
- Web admin:
  - `cd /root/Redball/web-admin && npm install && npm test` (test script present)
  - Start: `node server.js` or via `ecosystem.config.js`

### PowerShell

- Version bump: `scripts/windows/Bump-Version.ps1`
- Code coverage: `scripts/windows/Get-CodeCoverage.ps1`

## Key files

| File | Purpose |
| --- | --- |
| `/root/Redball/Directory.Build.props` | Global MSBuild version (`2.1.720`) and `EnableWindowsTargeting` |
| `/root/Redball/src/Redball.UI.WPF/Redball.UI.WPF.csproj` | Main WPF project file |
| `/root/Redball/src/Redball.Core/Redball.Core.csproj` | Core library |
| `/root/Redball/src/Redball.UI.WPF/Services/KeepAwakeService.cs` | Core keep-awake engine |
| `/root/Redball/src/Redball.UI.WPF/Models/RedballConfig.cs` | Configuration schema |
| `/root/Redball/src/Redball.UI.WPF/Interop/NativeMethods.cs` | Win32 P/Invoke declarations |
| `/root/Redball/update-server/package.json` | Update server dependencies and scripts |
| `/root/Redball/web-admin/package.json` | Web admin dependencies and scripts |

## Environment notes

- This audit was executed on a Linux container (`Linux 7.0.14-8-pve`).
- The .NET 10 SDK is **not installed** in this environment, so WPF/Core build/test cannot be executed here.
- Node.js appears available for the `web-admin` and `update-server` stacks; verification will be attempted where safe.
- WPF requires Windows; static analysis and patch generation are the only actions possible for the C# stack in this session.
