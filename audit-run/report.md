# Audit Report — Run 2 (prompt v7.0.0-redball)

**Scope:** `/root/Redball` — WPF/.NET 10 desktop app, `Redball.Core`, `update-server`, `web-admin`, `site`, `browser-extension`, NSIS installer, build/release scripts, docs.
**Method:** dual-track audit — ISS defects (auto-fix or queue) + OPP improvement proposals. Numbering resumed from ISS-020 → this run issued **ISS-021 – ISS-049** and **OPP-001 – OPP-010**.

## Headline

- **29 new defects** registered: 18 auto-fixed and verified, 11 queued (5 marked **[REQUIRES APPROVAL]**).
- **10 opportunities** registered (all `proposed`, none implemented).
- Worst open risks: unauthenticated release-trigger surfaces (`site/server.js`, legacy `web-admin/server-old.js`), a viewer-can-publish authorization gap on update-server, a broken primary test suite (zero effective coverage), and committed core dumps in git history.

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

## Queued (11) — patches in `audit-run/patches/`

| ISS | Sev | What | Why queued |
| --- | --- | --- | --- |
| ISS-037 | high | Test suite doesn't compile (~161 errors; 3 test frameworks referenced at once) | framework decision + test-intent rewrite — **APPROVAL** |
| ISS-038 | high | update-server mutating routes lack `requireAdmin` (viewer can publish/delete/build) | auth contract change — **APPROVAL** |
| ISS-039 | critical | `site/server.js` unauthenticated `/api/admin/{build,release,logs,config}` spawns pipeline | **APPROVAL** — may be superseded by web-admin |
| ISS-040 | low | `site/server.js` default port 3500 collides with update-server | paired w/ ISS-039 |
| ISS-041 | medium | web-admin build status/log public; download `startsWith` boundary | auth surface change |
| ISS-042 | medium | web-admin `/api/config` POST merges unvalidated JSON into served config | key allow-list patch ready |
| ISS-043 | medium | web-admin `startBuild` ignores `buildType`, always `auto-release` (bump+push+publish) | release pipeline — **APPROVAL** |
| ISS-044 | medium | update-server defaults invalid build type → `auto-release` | fail-closed patch ready |
| ISS-045 | high | `server-old.js` (unauth release trigger), `trigger-build.js` (admin/admin creds), committed `core.*` dumps, `build.pid`, `*_wpftmp.csproj`, dead `SettingsSectionView` | `git rm` + history purge for dumps |
| ISS-046 | low | 16 eslint `complexity` violations (auth/publish paths — real refactors) | per-function extraction plan |
| ISS-047 | medium | Extension targets `localhost:5000` — nothing listens; dead feature | product decision → OPP-001 |

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
| `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj -p:EnableWindowsTargeting=true` | **succeeded, 0 warnings, 0 errors** |
| `dotnet build Redball.v3.sln -p:EnableWindowsTargeting=true` | fails — **only** in pre-existing `tests/` (322 err) + `tests-ui-headless/` (8 err), tracked as ISS-037; `Redball.Core` + WPF + other projects compile |
| `cd update-server && npm test` | **3/3 pass** |
| `cd web-admin && npm test` | pass (placeholder by design) |
| `cd site && npx tsc -b && npx vite build && npm run lint` | **all clean** |
| `npx eslint .` | 373 → **48 problems**: 19 errors = 16 queued complexity + 2 in queued-for-deletion `server-old.js` + 1 `appendLog`; 29 `no-console` warnings (deliberate, browser files) |
| `node --check` on 12 modified JS files | all parse |
| `python3 -m json.tool browser-extension/manifest.json` | valid |

## Improvement roadmap

1. **This week (security):** apply ISS-039/041 (unauth surfaces), ISS-038 (`requireAdmin`), ISS-045 (delete `server-old.js`, purge core dumps, remove `trigger-build.js`), ISS-043/044 (auto-release footguns).
2. **Next:** ISS-037 test-suite rehab (OPP-009 program) — nothing else is safely verifiable until tests compile; then finish ISS-035 by adding tests-e2e/Benchmarks to the sln.
3. **Then hardening:** OPP-008 headers, OPP-005 signature verification, ISS-042 config allow-list, ISS-046 complexity passes.
4. **Strategic:** OPP-003 dashboard consolidation, OPP-001 extension API (or retire it), OPP-002 `AtomicFileWriter`, OPP-006 theme tokens.

## Not fixed / honest caveats

- `Redball.nsi` version (2.1.720) lags build props (2.1.724) — release pipeline regenerates it; left alone.
- Browser `console.*` warnings retained deliberately (29) — triage under ISS-046.
- WPF runtime behavior not exercised — no Windows host; all C# verification is compile-level.
- `update-server/data/releases.json` had pre-existing local modifications — untouched.
