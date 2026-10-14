# Queued Patches

Run 2 (v7.0.0-redball). All entries below are `queue-patch`; items marked **[REQUIRES APPROVAL]** touch security/auth/release contracts and must not be applied without maintainer sign-off. Patch proposals live in `audit-run/patches/ISS-NNN.patch`; multi-file reconciliations are documented as patch plans inline.

---

## ISS-037: Unit test project does not compile — ~161 errors from API drift and triple-framework references **[REQUIRES APPROVAL]**

- **Severity:** high | **Risk:** high | **Area:** tests
- **Location:** `/root/Redball/tests/` — `SecurityAuditServiceTests.cs`, `SecurityUtilitiesTests.cs`, `ServiceOrchestrationTests.cs`, `ConvertersTests.cs`, `Chaos/ChaosEngineeringTests.cs`, `UpdateSystemE2ETests.cs`, `ViewModels/CommandPaletteViewModelTests.cs`; also `tests-ui-headless/` (8 errors — same drift)
- **Summary:** CS0104 `Assert` ambiguous (csproj references MSTest + NUnit + xUnit + Playwright.NUnit + FlaUI simultaneously); ~160 CS1061/missing-type errors from API drift (`SecurityAuditService.Log*`/`QueryEvents`, `SecurityAuditEvent`, `SliderValueToWidthConverter`, `MemoryPoolService`, `CommandPaletteViewModel` members, `MainViewModel.TypeThingDriverSelection`).
- **Why queued:** deciding the canonical framework and reconciling drifted tests against current APIs is a maintainer decision; a mechanical patch would fabricate test intent.
- **Patch plan:**
  1. Pick xUnit as the sole framework (matches WIKI.md); strip MSTest/NUnit/Playwright.NUnit/FlaUI refs from `tests/Redball.Tests.csproj` (move browser/UI automation deps to `tests-ui-automation` where they belong).
  2. Convert MSTest-style asserts (`Assert.AreSame` → `Assert.Same`, `Assert.IsTrue` → `Assert.True`, `Assert.AreEqual` stays).
  3. Rewrite drifted test files against real APIs or delete stale ones.
  4. Disambiguate duplicate `SecurityAuditService` (Core vs UI.WPF) with explicit usings.
  5. Then add `tests-e2e` + `tests/Redball.Benchmarks` to the sln (completes ISS-035).
- **Verification:** `dotnet test tests/Redball.Tests.csproj` compiles and runs green.

## ISS-038: update-server mutating routes missing `requireAdmin` **[REQUIRES APPROVAL]**

- **Severity:** high | **Risk:** medium | **Area:** update-server
- **Location:** `/root/Redball/update-server/routes/api/releases.js` + `/root/Redball/update-server/server.js` route mounts
- **Summary:** Publish/delete/trigger-build endpoints are behind `authenticateToken` but not role-checked — any authenticated *viewer* account can publish or delete releases and fire the build pipeline.
- **Patch:** `patches/ISS-038.patch` — inserts a `requireAdmin` middleware and applies it to mutating routes. Approval needed because role enforcement changes the auth contract for existing non-admin users.
- **Verification:** `npm test`; manual check that a non-admin JWT gets 403 on `POST /api/releases`.

## ISS-039: site/server.js exposes unauthenticated admin endpoints that trigger the release pipeline **[REQUIRES APPROVAL]**

- **Severity:** critical | **Risk:** medium | **Area:** site
- **Location:** `/root/Redball/site/server.js` — `POST /api/admin/build`, `POST /api/admin/release`, `GET /api/admin/logs`, `GET /api/admin/config` (all spawn processes or expose config with no auth)
- **Summary:** If this file is ever served, anyone who can reach the port can run `scripts/build.sh` and publish releases.
- **Patch:** `patches/ISS-039.patch` — proposal adds a shared-secret token check (`ADMIN_TOKEN` env) on the `/api/admin/*` mount; alternative recommendation is deleting the file if `web-admin/` already supersedes it (see ISS-045 pattern).
- **Verification:** unauthenticated `POST /api/admin/build` returns 401; build still works with the token.

## ISS-040: site/server.js default port collides with update-server

- **Severity:** low | **Risk:** low | **Area:** site
- **Location:** `/root/Redball/site/server.js` — `PORT` default shares update-server's port space
- **Patch:** `patches/ISS-040.patch` — changes the default listen port; paired with ISS-039.

## ISS-041: web-admin exposes build status and full build log without authentication

- **Severity:** medium | **Risk:** low | **Area:** web-admin
- **Location:** `/root/Redball/web-admin/server.js` — `GET /api/build/status`, `GET /api/build/log` are public while `start/stop` require auth
- **Summary:** Build output can leak paths, versions, tokens echoed by the pipeline, and machine layout.
- **Patch:** `patches/ISS-041.patch` — mounts `authenticateToken` on the two read endpoints.
- **Verification:** `node --check`; curl without token → 401.

## ISS-042: web-admin `/api/config` POST merges unvalidated client JSON into the served default config

- **Severity:** medium | **Risk:** low | **Area:** web-admin
- **Location:** `/root/Redball/web-admin/server.js` — config POST handler spreads the request body into the config it serves to clients
- **Summary:** Type/shape isn't validated; arbitrary keys (and strings later rendered into `value=` attributes — now escaped by ISS-048) flow into the canonical config file the desktop app downloads.
- **Patch:** `patches/ISS-042.patch` — allow-lists writable keys and enforces primitive type checks per key.

## ISS-043: web-admin `startBuild` ignores `buildType` and always runs `auto-release` **[REQUIRES APPROVAL]**

- **Severity:** medium | **Risk:** medium | **Area:** web-admin
- **Location:** `/root/Redball/web-admin/server.js` — build-spawn handler
- **Summary:** The UI/API implies selectable build types but the spawn always passes `auto-release` — a "windows-only build" request still bumps the version, commits, pushes, and publishes a GitHub release.
- **Patch:** `patches/ISS-043.patch` — validates `buildType` against the same allow-list used by update-server and passes it through.

## ISS-044: update-server treats invalid/missing build type as `auto-release`

- **Severity:** medium | **Risk:** low | **Area:** update-server
- **Location:** `/root/Redball/update-server/server.js` — build-type resolution (~line where the allow-list check defaults)
- **Summary:** Invalid input silently escalates to the most destructive build mode instead of being rejected.
- **Patch:** `patches/ISS-044.patch` — returns 400 on unknown build types; only an explicit `auto-release` runs the release pipeline.

## ISS-045: Tracked dead/dangerous files — legacy unauthenticated server, cred-guessing script, core dumps, temp projects

- **Severity:** high | **Risk:** medium | **Area:** repo hygiene / security
- **Location:** `/root/Redball/web-admin/server-old.js` (unauthenticated `pty.spawn` release trigger + WS build control), `/root/Redball/trigger-build.js` (hardcoded `admin`/`admin` credential guessing), `/root/Redball/core.508609`, `/root/Redball/core.530235` (**committed core dumps** — may contain secrets), `/root/Redball/build.pid`, 5× `src/Redball.UI.WPF/Redball.UI.WPF_*_wpftmp.csproj`, `/root/Redball/src/Redball.UI.WPF/Views/SettingsSectionView.xaml(.cs)` (dead view)
- **Patch:** `patches/ISS-045.patch` — exact `git rm`/`git filter-repo` command list + `.gitignore` additions. Core dumps should be purged from history, not just deleted, if secrets exposure is possible.
- **Verification:** `git status` clean of these paths; `dotnet build` still passes without `SettingsSectionView`.

## ISS-046: eslint `complexity` violations in first-party code need real refactors

- **Severity:** low | **Risk:** medium | **Area:** update-server | web-admin | extension
- **Location:** `update-server/lib/analytics.js` `trackEvent`(15)/`getAnalytics`(18), `update-server/lib/delta-patches.js` `generatePatches`(17), `update-server/public/js/site.js` `syncData`(26), `update-server/server.js` arrow fns ~120(11)/~750(13)/~844(13)/~1020(12)/~1214(17)/~1490(14), `web-admin/public/admin.js` `renderConfig`(28)/`handleMessage`(21)/`syncState`(12)/`appendLog`(12)/`updateAnalyticsUI`(11)/~690(11), `web-admin/server.js` ~60(11)
- **Summary:** 16 functions exceed the repo's own `complexity ≤ 10` rule. Mechanical extraction isn't safe — several of these are auth/publishing paths.
- **Patch plan:** extract cohesive helpers per function, one PR per file, `npm test` + eslint after each.

## ISS-047: Browser extension targets `http://localhost:5000`, but nothing in the app listens there

- **Severity:** medium | **Risk:** low | **Area:** extension
- **Location:** `/root/Redball/browser-extension/background.js:3` (`REDBALL_API_URL`), `manifest.json` host_permissions
- **Summary:** The shipped extension can never connect — it's a dead feature with a working-looking popup UI. Product decision required: implement the localhost API (registered as OPP-001) or mark the extension experimental/remove it from distribution.

---

### Noted but not registered as defects

- `web-admin/server.js` download path check uses `startsWith(RELEASES_DIR)` — path boundary should use `path.resolve` + separator check; folded into ISS-041's patch scope.
- `MainWindow.xaml.cs` `catch (InvalidOperationException) { }` around `DragMove()` — intentional (drag throws when the button isn't down); left as-is.
- 29 remaining `no-console` **warnings** in shipped browser JS (`web-admin/public/admin.js`, `update-server/public/js/site.js`) — deliberate warnings, suggest triaging to `console.debug`-gated logging as part of ISS-046 work.
- `installer/Redball.nsi` shows `2.1.720` vs `Directory.Build.props` `2.1.724` — same class as historical ISS-020; the version-bump pipeline regenerates it, so no action taken (noted in report).
