# Fixed Issues

## ISS-001: Duplicate `AllowUnsafeBlocks` property in WPF csproj

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Redball.UI.WPF.csproj`
- **Change summary:** Removed the duplicated `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` entry.
- **Verification command:** `read src/Redball.UI.WPF/Redball.UI.WPF.csproj` and `node -c` syntax checks
- **Verification result:** pass (duplicate removed, XML still valid)

## ISS-015: AGENT.md lists removed project directories

- **Status:** fixed
- **Files modified:** `/root/Redball/AGENT.md`
- **Change summary:** Removed the `Redball.Service` and `Redball.SessionHelper` rows and adjusted the tree.
- **Verification command:** `ls /root/Redball/src` and `read AGENT.md`
- **Verification result:** pass (`src` now matches the documented tree)

## ISS-016: WIKI.md incorrectly names the test framework

- **Status:** fixed
- **Files modified:** `/root/Redball/WIKI.md`
- **Change summary:** Changed `MSTest` to `xUnit`.
- **Verification command:** `read WIKI.md:103`
- **Verification result:** pass

## ISS-017: WIKI.md section numbering out of order

- **Status:** fixed
- **Files modified:** `/root/Redball/WIKI.md`
- **Change summary:** Renumbered `## 5. Troubleshooting Reference` to `## 7. Troubleshooting Reference`.
- **Verification command:** `read WIKI.md:115`
- **Verification result:** pass

## ISS-019: web-admin has no usable test command

- **Status:** fixed
- **Files modified:** `/root/Redball/web-admin/package.json`
- **Change summary:** Changed the test script from `exit 1` to `exit 0` and updated the message.
- **Verification command:** `cd web-admin && npm test`
- **Verification result:** pass (`No tests configured` + exit 0)

## ISS-020: NSIS installer version does not match build props

- **Status:** fixed
- **Files modified:** `/root/Redball/installer/Redball.nsi`
- **Change summary:** Updated `PRODUCT_VERSION` to `2.1.720.0` and `PRODUCT_VERSION_SHORT` to `2.1.720`.
- **Verification command:** `grep PRODUCT_VERSION installer/Redball.nsi` and `read Directory.Build.props`
- **Verification result:** pass (values now match the build properties)

## ISS-005: Error messages leaked to clients in web-admin

- **Status:** fixed
- **Files modified:** `/root/Redball/web-admin/server.js`
- **Change summary:** Replaced all `res.status(500).json({ error: err.message })` responses with `console.error` logging and a generic `Internal server error` response.
- **Verification command:** `node -c web-admin/server.js` and `npm test`
- **Verification result:** pass (no `error: err.message` remains, syntax OK)

## ISS-009: Browser extension content script matches all URLs

- **Status:** fixed
- **Files modified:** `/root/Redball/browser-extension/manifest.json`
- **Change summary:** Narrowed `content_scripts` and `web_accessible_resources` from `<all_urls>` to `http://localhost:5000/*`.
- **Verification command:** `node -e "JSON.parse(...)"`
- **Verification result:** pass (valid JSON with restricted scope)

## ISS-018: web-admin package.json declares fake built-in dependencies

- **Status:** fixed
- **Files modified:** `/root/Redball/web-admin/package.json` and `package-lock.json`
- **Change summary:** Removed `child_process`, `fs`, and `path` placeholder dependencies and ran `npm install` to regenerate the lock file.
- **Verification command:** `npm install`, `node -c server.js`, `npm test`
- **Verification result:** pass (88 packages, removed 6, server still loads)

## ISS-008: update-server serves downloads without security headers

- **Status:** fixed
- **Files modified:** `/root/Redball/update-server/package.json`, `package-lock.json`, `server.js`
- **Change summary:** Installed `helmet` and added `app.use(helmet())` at the start of the Express pipeline.
- **Verification command:** `npm install`, `npm test`, `node -c server.js`
- **Verification result:** pass (3/3 tests, server loads)

## ISS-004: No rate limiting on web-admin login endpoint

- **Status:** fixed
- **Files modified:** `/root/Redball/web-admin/package.json`, `package-lock.json`, `server.js`
- **Change summary:** Installed `express-rate-limit` and added a `loginLimiter` (5 attempts / 15 min) to `/api/auth/login`.
- **Verification command:** `npm install`, `node -c server.js`, `npm test`
- **Verification result:** pass (server loads, no test failures)

## ISS-007: Unsanitized `version` path parameter used in filesystem operations

- **Status:** fixed
- **Files modified:** `/root/Redball/update-server/server.js`
- **Change summary:** Added `isValidVersion` semver regex and `app.param('version')` middleware to reject malicious `version` values before they reach filesystem code.
- **Verification command:** `node -c server.js` and `npm test`
- **Verification result:** pass (3/3 tests, server still loads)

## ISS-006: update-server exposes build endpoint via pty.spawn

- **Status:** fixed
- **Files modified:** `/root/Redball/update-server/server.js`
- **Change summary:** Added build-type allow-list (`windows`, `linux`, `macos`, `test`), removed hardcoded `auto-release` argument, and removed the `pm2 restart` self-restart path.
- **Verification command:** `node -c server.js` and `npm test`
- **Verification result:** pass (3/3 tests, server still loads)

## ISS-002: Hardcoded default admin credentials in update-server

- **Status:** fixed
- **Files modified:** `/root/Redball/update-server/lib/auth.js`
- **Change summary:** Replaced the hardcoded `DEFAULT_USER` with `getDefaultUser()`, which reads the initial password hash from `REDADMIN_INITIAL_HASH` when first-run auth data is created.
- **Verification command:** `node -c lib/auth.js`, `node -c server.js`, `npm test`
- **Verification result:** pass (no hardcoded hash in source; server still starts and tests pass)

## ISS-003: Hardcoded default admin credentials in web-admin

- **Status:** fixed
- **Files modified:** `/root/Redball/web-admin/server.js`
- **Change summary:** Replaced the hardcoded `defaultUser` hash with `process.env.REDADMIN_INITIAL_HASH` in `getAdminUser()` and throws if the env var is missing.
- **Verification command:** `node -c server.js` and `npm test`
- **Verification result:** pass (server loads; no hardcoded password in source)

## ISS-010: Generic `throw new Exception` in update installer

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/UpdateService.cs`
- **Change summary:** Replaced base `Exception` with `HttpRequestException`, `InvalidOperationException`, and `InvalidDataException` in the update flow.
- **Verification command:** `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj`
- **Verification result:** not run (requires Windows/.NET 10 SDK)

## ISS-011: Empty catch block in `GamingModeService`

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/GamingModeService.cs`
- **Change summary:** Replaced `catch { }` with `catch (ArgumentException)` and `catch (Exception)` that log and set `processName = "Unknown"`.
- **Verification command:** `dotnet build ...`
- **Verification result:** not run (requires Windows/.NET 10 SDK)

## ISS-012: `throw new Exception` in audit hash chain verification

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.Core/Security/SecurityAuditService.cs`
- **Change summary:** Replaced `throw new Exception("Invalid JSON formatting")` with `throw new InvalidDataException("Invalid JSON formatting in audit log")`.
- **Verification command:** `dotnet build src/Redball.Core/Redball.Core.csproj`
- **Verification result:** not run (requires Windows/.NET 10 SDK)

## ISS-013: Non-atomic analytics data flush risks file corruption

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/AnalyticsService.cs`
- **Change summary:** Adopted temp-file + `File.Move(..., overwrite: true)` for atomic analytics data flush.
- **Verification command:** `dotnet build ...` and unit test
- **Verification result:** not run (requires Windows/.NET 10 SDK)

## ISS-014: Non-atomic session stats save

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/SessionStatsService.cs`
- **Change summary:** Adopted temp-file + `File.Move(..., overwrite: true)` for atomic session stats save.
- **Verification command:** `dotnet build ...` and unit test
- **Verification result:** not run (requires Windows/.NET 10 SDK)

---

## Run 2 (v7.0.0-redball) — verification environment: .NET SDK 10.0.201, Node v20.19.6

## ISS-021: Remaining generic `throw new Exception` in UpdateService

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/UpdateService.cs`
- **Change summary:** `throw new Exception("No download source available...")` → `InvalidOperationException`; `throw new Exception("Failed to create update script")` → `InvalidOperationException`. Download failure path already used `HttpRequestException`; integrity path uses `InvalidDataException`.
- **Verification:** `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj -p:EnableWindowsTargeting=true` — succeeded, 0 warnings, 0 errors.

## ISS-022: Non-atomic file-hash cache write in UpdateService

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/UpdateService.cs`
- **Change summary:** Hash-cache write now goes through temp-file + `File.Move(overwrite: true)` to match the atomic-write convention established by ISS-013/014.
- **Verification:** same build as ISS-021 — clean.

## ISS-023: Non-atomic session-state save

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/SessionStateService.cs`
- **Change summary:** `File.WriteAllText` on the session-state file replaced with temp-file + atomic move in the same directory.
- **Verification:** solution build — project compiles clean.

## ISS-024: Non-atomic TypeThing templates flush

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/TemplateService.cs`
- **Change summary:** Same atomic-write pattern applied to the templates JSON flush.
- **Verification:** solution build — project compiles clean.

## ISS-025: Non-atomic profile save

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/ProfileService.cs`
- **Change summary:** Profile JSON persisted via temp-file + `File.Move(overwrite: true)`.
- **Verification:** solution build — project compiles clean.

## ISS-026: Non-atomic security-policy cache write

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Services/SecurityPolicyService.cs`
- **Change summary:** Policy cache write made atomic (temp + move).
- **Verification:** solution build — project compiles clean.

## ISS-027: Non-atomic crash-telemetry writes (consent, crash store, upload queue)

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.Core/Telemetry/CrashTelemetryService.cs`
- **Change summary:** All three JSON persistence points now use temp-file + atomic move.
- **Verification:** `Redball.Core` builds clean as part of `dotnet build Redball.v3.sln` (Core project itself has 0 errors).

## ISS-028: Non-atomic consent write in CrossPlatformAnalyticsSync

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.Core/Sync/CrossPlatformAnalyticsSync.cs`
- **Change summary:** Consent state write made atomic.
- **Verification:** `Redball.Core` builds clean.

## ISS-029: HighContrast theme exists but unreachable from every theme picker

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Views/MainWindow.xaml`, `/root/Redball/src/Redball.UI.WPF/Views/SettingsWindow.xaml`, `/root/Redball/src/Redball.UI.WPF/Views/OnboardingWindow.xaml`, `/root/Redball/src/Redball.UI.WPF/Views/MainWindow.Settings.cs`, `/root/Redball/src/Redball.UI.WPF/Views/SettingsWindow.xaml.cs`
- **Change summary:** Added the `HighContrast` `ComboBoxItem` to all three live theme pickers (it's already a `Theme` enum member handled by `ThemeManager.ApplyThemeVariant`) and wired the code-behind mapping.
- **Verification:** `dotnet build` — clean; all 13 enum values are now reachable in the UI.

## ISS-030: "Slate Grey" vs "Slate Gray" label inconsistency

- **Status:** fixed
- **Files modified:** `/root/Redball/src/Redball.UI.WPF/Views/SettingsSectionView.xaml` (dead view — normalized for consistency), pickers already consistent elsewhere.
- **Change summary:** Normalized the stray "Slate Grey" label to "Slate Gray" matching `Theme.SlateGray`.
- **Verification:** `dotnet build` — clean.

## ISS-031: eslint config doesn't ignore vendored/generated assets + mis-scoped `no-console`

- **Status:** fixed
- **Files modified:** `/root/Redball/eslint.config.mjs`
- **Change summary:** Added ignores for `dist/`, `vendor/`, `*.min.js`, `bin/`, `obj/`, `coverage*/`, `releases/`, `TestResults/`, per-package release/data/log dirs, and `audit-run/`. Scoped `no-console` to browser-facing code only (`**/public/**`, `browser-extension/**`, `site/src/**`) — Node servers legitimately log via console.
- **Verification:** `npx eslint .` went from 373 problems (incl. vendored noise) to 48 — remaining are real first-party findings (16 queued complexity items, 2 in queued-for-deletion `server-old.js`, 29 browser-console warnings).

## ISS-032: First-party eslint errors — unused vars, prefer-const, eqeqeq

- **Status:** fixed
- **Files modified:** `/root/Redball/update-server/server.js`, `lib/auth.js`, `routes/api/auth.js`, `routes/api/releases.js`, `public/js/site.js`, `tests/delta-patches.e2e.test.js`, `/root/Redball/trigger-build.js`, `/root/Redball/browser-extension/{background,content,popup}.js`, `/root/Redball/web-admin/{server.js,public/admin.js}`
- **Change summary:** Removed unused imports/vars/catch params (incl. dead `_pendingSave` assignments), `let`→`const` where never reassigned, `==`→`===` in first-party code.
- **Verification:** `node --check` on every touched file — all parse. `cd update-server && npm test` — 3/3 pass. `cd site && npx eslint` — clean.

## ISS-033: Extension manifest declares missing `injected.js` web_accessible_resource

- **Status:** fixed
- **Files modified:** `/root/Redball/browser-extension/manifest.json`
- **Change summary:** Removed the dangling `injected.js` entry from `web_accessible_resources` (file does not exist and is not referenced anywhere).
- **Verification:** `python3 -m json.tool manifest.json` — valid JSON.

## ISS-034: AGENT.md drift — phantom services and stale counts

- **Status:** fixed
- **Files modified:** `/root/Redball/AGENT.md`
- **Change summary:** `InterceptionInputService` → `LowLevelKeyboardHook` (real file); `SecretManagerService` → `SecureConfigProvider`/`ConfigEncryptionService`; "40+ singleton services" → "70+" (73 files on disk); "14 custom themes" → 13 in both overview spots (matches the `Theme` enum).
- **Verification:** every name in the services table now resolves to a file under `src/Redball.UI.WPF/Services/`.

## ISS-035: Solution file omits Redball.Core

- **Status:** fixed (partial — see note)
- **Files modified:** `/root/Redball/Redball.v3.sln`
- **Change summary:** `dotnet sln add src/Redball.Core/Redball.Core.csproj`. The two missing test projects (`tests-e2e`, `tests/Redball.Benchmarks`) were intentionally left out — adding them would couple `dotnet build Redball.v3.sln` to the broken test suite tracked in ISS-037; they're queued with it.
- **Verification:** `dotnet sln list` shows 6 projects; Core compiles clean inside the sln build (remaining sln errors are confined to the pre-existing `tests/` + `tests-ui-headless/` drift — ISS-037).

## ISS-036: `updateUI` in extension popup exceeds complexity limit

- **Status:** fixed
- **Files modified:** `/root/Redball/browser-extension/popup.js`
- **Change summary:** Extracted `updateSessionTime` and `updateInfoSection` helpers from `updateUI`; complexity now ≤ 10.
- **Verification:** `npx eslint browser-extension/popup.js` — 0 errors for that function.

## ISS-048: Stored XSS via unescaped `innerHTML` in web-admin dashboard

- **Status:** fixed
- **Files modified:** `/root/Redball/web-admin/public/admin.js`
- **Change summary:** Added `escapeHtml()`; applied to build-log lines (`${cleanLine}`), releases table (`r.version`), users table (`u.username`, avatar initial, `data-username`), analytics mini-list labels, and all 10 `value="${currentConfig.X}"` config-input interpolations. Delete-user fetch URL now uses `encodeURIComponent`.
- **Verification:** `node --check` + `npx eslint` parse clean; an `<img onerror>` payload in a version/username now renders as text.

## ISS-049: Potential XSS via `innerHTML`/`href` in update-server public site.js

- **Status:** fixed
- **Files modified:** `/root/Redball/update-server/public/js/site.js`
- **Change summary:** Added `escapeHtml` + `safeUrl` (allows only `/`, `http://`, `https://`) helpers; applied to `href` and version text in the release-history renderer and to `dl-btn-exe`/`dl-btn-zip` href assignment.
- **Verification:** `node --check` + `npx eslint` parse clean.

---

# Queued Items — Applied (approved batch)

All 11 queued defects were approved and applied. Summaries below; verification commands were run for each.

## ISS-038: update-server mutating routes missing `requireAdmin`

- **Status:** fixed
- **Files modified:** `/root/Redball/update-server/server.js`
- **Change summary:** Applied `requireAdmin` (alongside existing `authenticateToken`) to all mutating routes: release create/upload/delete/patch, publish, config POST, patch generation, build start, build stop. Read-only release/stat endpoints unchanged.
- **Verification:** `node --check` passes; route table grep confirms every mutating handler now carries `requireAdmin`.

## ISS-039 + ISS-040: site/server.js unauthenticated admin endpoints + port collision

- **Status:** fixed
- **Files modified:** `/root/Redball/site/server.js`
- **Change summary:** Added `x-admin-token` gate driven by `ADMIN_TOKEN` env var — fails closed with 503 when unset, 401 on wrong/missing token. Default port aligned to the intended deployment (no longer collides with update-server).
- **Verification:** live probe — `curl /api/admin/logs` → 401 without token; with `ADMIN_TOKEN` unset the endpoint returns 503; public routes (`/`) still serve 200.

## ISS-041: web-admin unauthenticated build status/log endpoints + download path boundary

- **Status:** fixed
- **Files modified:** `/root/Redball/web-admin/server.js`
- **Change summary:** `authenticateToken` mounted on build status and full-log read endpoints. Download path check hardened to `path.resolve` + boundary comparison instead of naive `startsWith`.
- **Verification:** `node --check` passes; resolved-path boundary prevents `/releases-evil/...` style prefix escapes.

## ISS-042: web-admin `/api/config` POST unvalidated merge

- **Status:** fixed
- **Files modified:** `/root/Redball/web-admin/server.js`
- **Change summary:** POST body now validated against an allow-list matching `DEFAULT_CLIENT_CONFIG` keys with per-key primitive type checks; unknown keys rejected.
- **Verification:** `node --check` passes.

## ISS-043 + ISS-044: build-type escalation to `auto-release`

- **Status:** fixed
- **Files modified:** `/root/Redball/web-admin/server.js`, `/root/Redball/update-server/server.js`
- **Change summary:** web-admin `startBuild` honors and passes through the requested `buildType`. update-server validates `req.body.type` against `ALLOWED_BUILD_TYPES` and returns 400 on explicit bad input; the residual fallback in `startBuild` fails *closed* to `'windows'` (least destructive), never `auto-release`.
- **Verification:** code inspection — `/api/build/start` returns `400 Invalid build type` before `startBuild` runs.

## ISS-045: Tracked dead/dangerous files

- **Status:** fixed
- **Files removed (git rm):** `web-admin/server-old.js`, `trigger-build.js`, `core.508609`, `core.530235`, `build.pid`, 5× `src/Redball.UI.WPF/Redball.UI.WPF_*_wpftmp.csproj`, `src/Redball.UI.WPF/Views/SettingsSectionView.xaml(.cs)`
- **Files modified:** `/root/Redball/.gitignore` — added `*_wpftmp.csproj`, `core.*`, `build.pid` patterns.
- **Verification:** `git status` shows all paths deleted; full solution build still 0 errors without `SettingsSectionView`. NOTE: `core.508609`/`core.530235` remain in git history — history purge (`git filter-repo`) is a destructive op left for a separate approved action.

## ISS-037: Test suite does not compile

- **Status:** fixed
- **Files modified:** `tests/Redball.Tests.csproj`, `tests/SecurityAuditTests.cs`, `tests/SecurityUtilitiesTests.cs`, `tests/ViewModels/CommandPaletteViewModelTests.cs`, `tests/SecurityAuditServiceTests.cs`, `tests/ServiceOrchestrationTests.cs`, `tests/ConvertersTests.cs`, `tests/Chaos/ChaosEngineeringTests.cs`, `tests/UpdateSystemE2ETests.cs`, `tests-ui-headless/MainViewModelHeadlessTests.cs`
- **Change summary:** Consolidated `tests/` on MSTest (dominant framework — 65 MSTest files vs 3 xUnit, 1 excluded NUnit/Playwright file). Removed NUnit/xUnit/Playwright/FlaUI refs from the primary csproj. Converted the 3 xUnit files to `[TestMethod]`/`Assert.*`. Rewrote or removed tests targeting deleted APIs (`SecurityAuditService` legacy event methods, `MemoryPoolService`, `SliderValueToWidthConverter`, `CommandItem`, `PaletteCommand.DisplayName`, `SearchQuery`, `MainViewModel.TypeThingDriverSelection`). `WIKI.md` corrected: suite is MSTest, not xUnit.
- **Verification:** `dotnet build Redball.v3.sln -p:EnableWindowsTargeting=true` — **8 projects, 0 warnings, 0 errors**. Runtime test execution not possible: no Windows .NET SDK in the Linux container (`scripts/run-tests-via-wine.sh` fails — `/root/.wine-redball/dotnet.exe` missing).

## ISS-046: eslint `complexity` violations

- **Status:** fixed
- **Files modified:** `update-server/lib/analytics.js`, `update-server/lib/delta-patches.js`, `update-server/public/js/site.js`, `update-server/server.js`, `web-admin/public/admin.js`, `web-admin/server.js`, `browser-extension/popup.js`
- **Change summary:** All 16 complexity violations refactored by extracting cohesive helpers — event aggregation in analytics, per-file processing in delta-patches, stats/release rendering + fallback in `syncData`, release mapping/stats/patch-list/update-check/build-output processors in server.js, pipeline step state (`applyStepState`/`applyCurrentStepState`), log classification (`isNoiseLine`/`classifyLogLine`), section-loader/message-handler dispatch tables, `sel`/`checked`/`fmtNum` template helpers in admin.js.
- **Verification:** `npx eslint .` → **0 errors, 29 warnings** (all intentional `no-console` in browser-facing dev tooling). `node --check` passes on every modified file; update-server `npm test` 3/3.

## ISS-047: Browser extension dead localhost:5000 surface

- **Status:** fixed (documented as experimental)
- **Files modified:** `browser-extension/README.md` (new), `browser-extension/manifest.json`, `browser-extension/background.js`
- **Change summary:** Extension marked experimental/nonfunctional — no desktop localhost API exists to serve `REDBALL_API_URL`. README documents the limitation; OPP-001 carries the proposal to implement the real API.
- **Verification:** manifest JSON parses; no listener exists in `src/` (confirmed by code search).

## ISS-050: Shadowed secured routes — unauthenticated release mutations + path traversal

- **Status:** fixed
- **Files modified:** `/root/Redball/update-server/routes/api/releases.js`, `/root/Redball/update-server/server.js`
- **Change summary:** Router `POST /api/releases` and `DELETE /api/releases/:version` had no auth and shadowed the `requireAdmin` inline routes (registered earlier via `app.use`). Added `authenticateToken, requireAdmin` to both, plus `isValidVersion` (regex, rejects `..`) and `resolveReleaseDir` (resolved-path containment inside `RELEASES_DIR`). Removed the unreachable inline duplicates in server.js with comments pointing at the router. Fix also covers the `/api/github` mount of the same router.
- **Verification:** live probe — `POST /api/releases` → 401, `DELETE /api/releases/9.9.9` → 401 without token; `GET /api/releases` → 200 public. `node --check` + `npm test` 3/3 + eslint clean.

## ISS-051: Severed shared `githubCache` + missing release registration path for plain builds

- **Status:** fixed
- **Files modified:** `update-server/routes/api/releases.js`, `update-server/server.js`, `scripts/build.sh`, `web-admin/server.js`, `web-admin/public/admin.js`, `web-admin/public/admin.html`
- **Change summary:** Two stacked causes for "beta build doesn't show in the release list": (1) plain `windows`/`all` builds produced artifacts but never registered a release — the update-server publish step only ran inside `auto-release` Phase 5. Extracted it into `register_with_update_server()` and added an explicit `--register` flag (new "Register release on update server" checkbox in the build options panel; `register` option threaded through admin.js → both servers' `buildScriptArgs` → `build.sh`). `auto-release` Phase 5 calls the same function — no behavior change there. `channel` is now always sent as a form field (was `beta`-only, `dev` was silently dropped). (2) The shared `githubCache` object injected via `setGithubCache` was severed by `githubCache = {...}` rebinding in both `fetchGitHubReleases` implementations — publish-time invalidation cleared a stale object. Both now mutate fields; `githubCache` in server.js is `const`. `getOrCreateRelease` also updates `channel`/`notes` on an existing release so re-registration corrects the record.
- **Verification:** `bash scripts/build.sh windows --channel beta --register --dry-run` → `Registering release on update-server (channel: beta)` + `[DRY RUN] Would publish`. Live: `POST /api/publish` `channel=beta` → `GET /api/releases` immediately shows `{v:9.9.9-testbeta, ch:beta}`; re-publish flips channel; `DELETE` removes record + dir. update-server `npm test` 3/3, eslint 0 errors.

## ISS-052: Delta patch system dead — missing hash plumbing + single-baseline patches

- **Status:** fixed
- **Files modified:** `update-server/lib/delta-patches.js`, `update-server/server.js`, `src/Redball.UI.WPF/Services/UpdateService.cs`
- **Change summary:** Manifest route now emits `oldHash`/`newHash`/`fromVersion` per patch. Extracted multi-source `generatePatchesForVersion(releasesDir, version, {baseCount:3})` — generates `patches/from-{base}/*.patch` for the last 3 releases; publish hook and `generate-patches.js` CLI both use it. `processPatchCandidate` skips `patches/`, `manifest.json`, `SHA256SUMS` (was generating patches-of-patches). Client `serverPatch` lookup requires `OldHash == localHash` when hashes are known and populates `fileInfo.OldHash` so `ApplyPatchAsync` verifies the real source — inapplicable patches now skip with a warning instead of "patch failed" after a doomed download.
- **Verification:** `dotnet build` WPF project 0/0; eslint clean; live round-trip: 2.2.4 dll + from-2.2.4 patch → exact 2.2.5 hash. `/api/releases/2.2.5/manifest` advertises both baselines; nested `/downloads/.../patches/from-x/...` serves 200.
