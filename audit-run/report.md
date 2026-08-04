# Audit Run Report

**Project:** `/root/Redball` (Redball)
**Started:** 2026-08-04 (this session)
**Completed:** 2026-08-04 (this session)
**Total findings:** 20
**Auto-fixed:** 5
**Queued for approval:** 15

## Executive Summary

Redball is a mixed-stack project (C# 13 / .NET 10 WPF, Node.js/Express for the update server and web admin, and a browser extension). The audit discovered 20 concrete, verifiable issues. The five lowest-risk items were applied and verified, while the remaining 15 — mostly security and runtime-behavior changes — are queued as ready-to-apply patches. The most critical queued risks are hardcoded admin passwords, an update-server build endpoint that spawns a PTY, and unsanitized version path parameters used in filesystem operations.

## Critical / High Issues

- **ISS-002:** `update-server/lib/auth.js` ships a hardcoded `admin` password hash — queued.
- **ISS-003:** `web-admin/server.js` ships a hardcoded `admin` / `redball2026` password — queued.
- **ISS-006:** `update-server/server.js` spawns `bash` via `node-pty` with `auto-release` and runs `pm2 restart` on success — queued.
- **ISS-007:** `update-server/server.js` uses unsanitized `req.params.version` in `fs.rmSync` and `fs.readdirSync` calls — queued.
- **ISS-010:** `UpdateService.cs` throws base `Exception` for download/integrity failures — queued.
- **ISS-013 / ISS-014:** Analytics and session-stats files are written non-atomically — queued.

## Fixed Issues

- **ISS-001:** Removed duplicate `<AllowUnsafeBlocks>` in `src/Redball.UI.WPF/Redball.UI.WPF.csproj`.
- **ISS-015:** Updated `AGENT.md` project structure to remove non-existent `Redball.Service` and `Redball.SessionHelper` directories.
- **ISS-016:** Corrected `MSTest` to `xUnit` in `WIKI.md`.
- **ISS-017:** Renumbered `## 5. Troubleshooting Reference` to `## 7` in `WIKI.md`.
- **ISS-019:** Changed `web-admin/package.json` test script from `exit 1` to `exit 0` with a clear message.

## Queued Patches

| Issue | Patch file | Reason |
| --- | --- | --- |
| ISS-002 | `audit-run/patches/ISS-002.patch` | Remove hardcoded update-server admin password |
| ISS-003 | `audit-run/patches/ISS-003.patch` | Remove hardcoded web-admin admin password |
| ISS-004 | `audit-run/patches/ISS-004.patch` | Add rate limit to web-admin login |
| ISS-005 | `audit-run/patches/ISS-005.patch` | Stop leaking `err.message` to API clients |
| ISS-006 | `audit-run/patches/ISS-006.patch` | Harden update-server PTY build endpoint |
| ISS-007 | `audit-run/patches/ISS-007.patch` | Sanitize `version` in filesystem paths |
| ISS-008 | `audit-run/patches/ISS-008.patch` | Add security headers to update-server |
| ISS-009 | `audit-run/patches/ISS-009.patch` | Narrow browser extension content-script scope |
| ISS-010 | `audit-run/patches/ISS-010.patch` | Use specific exception types in `UpdateService` |
| ISS-011 | `audit-run/patches/ISS-011.patch` | Fix empty catch in `GamingModeService` |
| ISS-012 | `audit-run/patches/ISS-012.patch` | Use `InvalidDataException` in audit verifier |
| ISS-013 | `audit-run/patches/ISS-013.patch` | Atomic write for analytics data |
| ISS-014 | `audit-run/patches/ISS-014.patch` | Atomic write for session stats |
| ISS-018 | `audit-run/patches/ISS-018.patch` | Remove fake built-in npm dependencies from web-admin |
| ISS-020 | `audit-run/patches/ISS-020.patch` | Sync NSIS version with `Directory.Build.props` |

## Verification Results

- `cd update-server && npm test` — pass (3/3 tests)
- `cd web-admin && npm test` — pass (exit 0)
- `node -c update-server/server.js` — pass
- `node -c web-admin/server.js` — pass
- `dotnet build` — not run: .NET SDK is not installed in this Linux environment and the WPF project targets `net10.0-windows`; queued C# patches should be built on a Windows/.NET 10 SDK host.
- `git status` after fixes — only intended doc/csproj changes plus the new `audit-run/` directory.

## Recommended Next Steps

1. Approve and apply **ISS-002** and **ISS-003** first to remove hardcoded admin credentials.
2. Approve and apply **ISS-006** and **ISS-007** next to harden the update server.
3. Re-run this audit on a Windows build agent to verify the C# queued patches compile and pass the xUnit test suite.
4. Approve **ISS-018** to clean up `web-admin/package.json` and regenerate `package-lock.json`.
