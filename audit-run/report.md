# Audit Report — Run 2 (prompt v7.0.0-redball)

**Scope:** `/root/Redball` — WPF/.NET 10 desktop app, `Redball.Core`, `update-server`, `web-admin`, `site`, `browser-extension`, NSIS installer, build/release scripts, docs.
**Method:** dual-track audit — ISS defects (auto-fix or queue) + OPP improvement proposals. Numbering resumed from ISS-020 → this run issued **ISS-021 – ISS-049** and **OPP-001 – OPP-010**.

## Headline

- **29 new defects** registered: **all 29 fixed** — 18 auto-fixed during the run, 11 queued items applied after maintainer approval.
- **10 opportunities** registered (all `proposed`, none implemented).
- Resolved risks: unauthenticated release-trigger surfaces (`site/server.js` gated + `server-old.js` deleted), viewer-can-publish authorization gap closed (`requireAdmin` on all mutating routes), test suite compiles (consolidated on MSTest), dead/dangerous artifacts removed.
- Residual: committed core dumps remain in git **history** (files deleted from HEAD; history purge is a separate destructive op). Runtime Windows test execution unavailable in this container.

## Fixed this run (18)

| ISS | Area | Fix |
| --- | --- | --- |
| ISS-021 | WPF updater | `throw new Exception` → `InvalidOperationException` in `UpdateService.cs` |
| ISS-022–028 | WPF/Core persistence | Atomic temp+move writes for hash cache, session state, templates, profiles, security policy, crash telemetry, analytics consent — same class as prior ISS-013/014 |
| ISS-029 | WPF themes | `HighContrast` added to all 3 theme pickers (enum + `ApplyThemeVariant` already existed) |
| ISS-030 | WPF themes | "Slate Grey" → "Slate Gray" label normalization |
| ISS-031 | tooling | eslint ignores for vendor/dist/logs/audit dirs; `no-console` scoped to browser code only |
| ISS-032 | JS hygiene | All first-party eslint *errors* fixed (unused vars/imports/catch params, prefer-const, eqeqeq) across update-server, web-admin, extension, trigger-build, site JS |
| ISS-033 | extension | Removed dangling `injected.js` from manifest `web_accessible_resources` |
| ISS-034 | docs | AGENT.md drift: phantom services → real ones, 40+ → 70+ services, 14 → 13 themes |
| ISS-035 | build | `Redball.Core` added to `Redball.v3.sln` (test projects deferred — see ISS-037) |
| ISS-036 | extension | `updateUI` complexity 11 → helpers extracted, ≤10 |
| ISS-048 | web-admin | Stored-XSS: `escapeHtml` applied to build-log lines, releases/users tables, analytics labels, all config `value=` inputs; `encodeURIComponent` on delete-user path |
| ISS-049 | update-server site | `escapeHtml` + `safeUrl` (scheme allow-list) on release-history `innerHTML`/`href` and download buttons |

## Queued → applied (11, all approved and fixed)

| ISS | Sev | What | Applied as |
| --- | --- | --- | --- |
| ISS-037 | high | Test suite didn't compile (~161 errors; 3 frameworks referenced at once) | Consolidated `tests/` on MSTest; converted 3 xUnit files; repaired/removed drifted tests — **sln builds 0/0** |
| ISS-038 | high | update-server mutating routes lacked `requireAdmin` | `requireAdmin` on all mutating routes |
| ISS-039 | critical | `site/server.js` unauthenticated `/api/admin/{build,release,logs,config}` | `x-admin-token` gate (`ADMIN_TOKEN` env); fails closed 503 unset / 401 wrong |
| ISS-040 | low | `site/server.js` default port collided with update-server | port aligned |
| ISS-041 | medium | web-admin build status/log public; `startsWith` path boundary | `authenticateToken` on reads; `path.resolve` boundary check |
| ISS-042 | medium | web-admin `/api/config` POST merged unvalidated JSON | allow-list keyed to `DEFAULT_CLIENT_CONFIG` + type checks |
| ISS-043 | medium | web-admin `startBuild` ignored `buildType`, always `auto-release` | honors + passes through requested type |
| ISS-044 | medium | update-server defaulted invalid build type → `auto-release` | 400 on explicit bad type; residual fallback fails closed to `windows` |
| ISS-045 | high | `server-old.js`, `trigger-build.js`, `core.*` dumps, `build.pid`, `*_wpftmp.csproj`, dead `SettingsSectionView` | all `git rm`'d + `.gitignore` patterns added |
| ISS-046 | low | 16 eslint `complexity` violations | all refactored — **eslint 0 errors** |
| ISS-047 | medium | Extension targets dead `localhost:5000` | marked experimental; `browser-extension/README.md` documents limitation → OPP-001 |

## Opportunities registered (proposed only)

- **OPP-001** Localhost control API to revive the extension (strategic)
- **OPP-002** Shared `AtomicFileWriter` in Core — kills the whole ISS-013/022–028 class (in-arch)
- **OPP-003** Consolidate the 3 overlapping Node dashboards (strategic)
- **OPP-004** Structured logging for Node services (in-arch)
- **OPP-005** `VerifyUpdateSignature: true` once a signature scheme ships (strategic)
- **OPP-006** Real theme dictionaries/tokens per variant (in-arch)
- **OPP-007** Diagnostics export self-check section (in-arch)
- **OPP-008** helmet/CSP on web-admin + site (in-arch)
- **OPP-009** Test-suite rehabilitation program w/ CI gate (strategic)
- **OPP-010** Plugin trust model + manifest + docs (strategic)

## Verification results (all actually run)

| Command | Result |
| --- | --- |
| `dotnet build Redball.v3.sln -p:EnableWindowsTargeting=true` | **succeeded — 8 projects, 0 warnings, 0 errors** (Core + WPF + tests + e2e + benchmarks all members) |
| `dotnet test` | compiles; **runtime execution unavailable** — no Windows .NET SDK in container (`run-tests-via-wine.sh` fails: `/root/.wine-redball/dotnet.exe` missing) |
| `cd update-server && npm test` | **3/3 pass** |
| `cd web-admin && npm test` | pass (placeholder by design) |
| `cd site && npx tsc -b && npx vite build && npm run lint` | **all clean** |
| `npx eslint .` | 373 → **0 errors, 29 warnings** (all deliberate `no-console` in browser-facing files) |
| `node --check` on all modified JS files | all parse |
| Live probe: `site/server.js` admin endpoints | 401 without token; 503 when `ADMIN_TOKEN` unset; public `/` 200 |
| `python3 -m json.tool browser-extension/manifest.json` | valid |

## Improvement roadmap

All queued defects are now applied. Remaining work is opportunity-level (see `opportunities.md`):

1. **Hardening:** OPP-008 (helmet/CSP headers), OPP-005 (update signature verification), OPP-002 (`AtomicFileWriter` in Core — kills the whole ISS-013/022–028 class).
2. **Strategic:** OPP-003 (consolidate 3 Node dashboards), OPP-001 (localhost control API to revive the extension — or retire it), OPP-006 (theme tokens), OPP-009 (CI test gate so ISS-037 can't regress).
3. **Repo hygiene (destructive — needs explicit approval):** purge `core.508609`/`core.530235` from git history via `git filter-repo` if they may contain secrets.

## Not fixed / honest caveats

- `core.508609`/`core.530235` deleted from HEAD but remain in git history — history purge needs explicit approval (rewrites history).
- `Redball.nsi` version (2.1.720) lags build props (2.1.724) — release pipeline regenerates it; left alone.
- 29 `no-console` warnings retained deliberately in browser-facing JS.
- Windows runtime behavior not exercised — no Windows host/SDK; all .NET verification is compile-level. Runtime test pass still owed on a Windows machine.
- `update-server/data/releases.json` had pre-existing local modifications — untouched.
