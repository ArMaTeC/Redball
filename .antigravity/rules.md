# Antigravity Rules for Redball

These rules are specifically for Antigravity (me) to follow when assisting with the Redball project.

## Core Principles

1. **Maintain WPF Native Feel**: Avoid bringing in external dependencies unless absolutely necessary.
2. **Follow the Service Architecture**: Use the established singleton services in `src/Redball.UI.WPF/Services/`.
3. **Ensure UI Consistency**: Always use `DynamicResource` and verify theme compatibility for all 14 themes.
4. **Security & Privacy**: No hardcoded secrets. Follow the privacy-conscious design of Redball.
5. **Robust Error Handling**: Handle exceptions gracefully and log them using the `Logger` service.
6. **Test-Driven Development**: Always include tests for new functionality.

## Project Structure

- `src/Redball.UI.WPF/`: Main WPF application.
- `scripts/`: PowerShell build and utility scripts.
- `installer/`: WiX v4 installer configurations.
- `tests/`: Pester (PowerShell) and C# test suites.

## Coding Style

- Follow the `.cursorrules` at the project root for detailed naming and pattern guidelines.
- Add XML documentation to all public members.

## MCP Servers

- Utilize MCP servers defined in `mcp-servers.json` for enhanced project mastery.
- GitHub and Fetch servers are configured for repo management and doc retrieval.
- Memory: Use the Memory MCP server for persistent project knowledge.

## Auto-run Permissions

To reduce friction and enhance autonomy, Antigravity is authorized to use `SafeToAutoRun: true` for the following "Safe Commands":

### Safe Commands (Auto-run Allowed)

- **Build & Test**: `dotnet build`, `dotnet test`, `dotnet restore`, `Invoke-Pester`.
- **Analysis**: `Invoke-ScriptAnalyzer`.
- **Scripts**: `.\scripts\build.ps1 -SkipVersionBump -SkipMSI -SkipRestore` (Read-only validation).
- **Discovery**: `ls`, `dir`, `grep`, `findstr`, `cat`, etc.

### Restricted Commands (Approval Required)

- `git push`, `git commit`.
- `.\scripts\build.ps1` (with version bumping enabled).
- `Stop-Process`, `Kill`, `Remove-Item` (on non-build-artifacts).
- Any command that modifies system state outside the project directory.

> [!IMPORTANT]
> Even with auto-run permissions, Antigravity must prioritize safety and never execute potentially destructive commands without explicit confirmation.
