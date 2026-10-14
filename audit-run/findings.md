# Audit Findings

<!-- markdownlint-disable MD031 -->

*All issues use the `ISS-NNN` format. Each finding was triaged using the decision matrix in the run prompt.*

## ISS-001: Duplicate `AllowUnsafeBlocks` property in WPF csproj

- **Scanner:** S6 UI Uniformity / S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Redball.UI.WPF.csproj:27-28`
- **Evidence:**

  ```xml
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  ```

- **Issue:** The same MSBuild property is set twice on consecutive lines, which is redundant and can cause confusion during maintenance.
- **Proposed fix:** Remove the second `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` line.
- **Verification:** `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj` (on Windows/.NET SDK)
- **Action:** auto-fix

## ISS-002: Hardcoded default admin credentials in update-server

- **Scanner:** S10 Security
- **Severity:** critical
- **Risk:** high
- **Location:** `/root/Redball/update-server/lib/auth.js:48-63`
- **Evidence:**

  ```javascript
  function getDefaultUser() {
      const hash = process.env.REDADMIN_INITIAL_HASH;
      if (!hash) {
          throw new Error('REDADMIN_INITIAL_HASH environment variable is required for first-run admin setup');
      }
      return {
          username: 'admin',
          passwordHash: hash,
          role: 'admin',
          mfaEnabled: false,
          mfaSecret: null,
          createdAt: new Date().toISOString()
      };
  }
  ```

- **Issue:** A default admin account with a known, hardcoded password hash was shipped in source. If the admin did not change it on first run, an attacker could authenticate with `admin:admin`.
- **Proposed fix:** (applied) Remove the hardcoded `DEFAULT_USER` object and replace it with `getDefaultUser()`, which reads the initial password hash from `REDADMIN_INITIAL_HASH` and is only called on first-run auth creation.
- **Verification:** `npm test` and `node -c`
- **Action:** fixed

## ISS-003: Hardcoded default admin credentials in web-admin

- **Scanner:** S10 Security
- **Severity:** critical
- **Risk:** high
- **Location:** `/root/Redball/web-admin/server.js:117-135`
- **Evidence:**

  ```javascript
  function getAdminUser() {
    const initialHash = process.env.REDADMIN_INITIAL_HASH;
    if (!initialHash) {
      throw new Error('REDADMIN_INITIAL_HASH environment variable is required for first-run admin setup');
    }
    // ...
    const defaultUser = {
      username: 'admin',
      passwordHash: initialHash
    };
  }
  ```

- **Issue:** The web admin shipped with a hardcoded, documented default password (`redball2026`) and no enforcement of a mandatory change.
- **Proposed fix:** (applied) Replaced the hardcoded hash with `process.env.REDADMIN_INITIAL_HASH` and throw if the env var is not set, requiring an explicit initial hash on first run.
- **Verification:** `npm test` and `node -c`
- **Action:** fixed
- **Action:** queue-patch

## ISS-004: No rate limiting on web-admin login endpoint

- **Scanner:** S10 Security
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/web-admin/server.js:221-236`
- **Evidence:**

  ```javascript
  const loginLimiter = rateLimit({
    windowMs: 15 * 60 * 1000,
    max: 5,
    standardHeaders: true,
    message: { error: 'Too many login attempts, please try again later.' }
  });

  app.post('/api/auth/login', loginLimiter, async (req, res) => {
  ```

- **Issue:** The `/api/auth/login` endpoint accepts unlimited attempts, enabling online brute-force attacks against the single admin account.
- **Proposed fix:** (applied) Installed `express-rate-limit` and added `loginLimiter` to the login route (5 attempts per 15 minutes per IP).
- **Verification:** `npm test` and manual curl attempts
- **Action:** fixed

## ISS-005: Error messages leaked to clients in web-admin

- **Scanner:** S8 API / S10 Security
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/web-admin/server.js:288,317,329,380,405` (all 500 handlers)
- **Evidence:**

  ```javascript
  } catch (err) {
    console.error('[API] Internal error:', err);
    res.status(500).json({ error: 'Internal server error' });
  }
  ```

- **Issue:** Internal `Error` messages are forwarded to API clients, potentially exposing file paths, stack frames, or internal state.
- **Proposed fix:** (applied) Return a generic error object and log the original error server-side.
- **Verification:** `npm test` and manual requests
- **Action:** fixed

## ISS-006: update-server exposes build endpoint via pty.spawn

- **Scanner:** S10 Security
- **Severity:** critical
- **Risk:** high
- **Location:** `/root/Redball/update-server/server.js:1379-1543`
- **Evidence:**

  ```javascript
  const allowedBuildTypes = ['windows', 'linux', 'macos', 'test'];
  const buildType = allowedBuildTypes.includes(body?.type) ? body.type : 'windows';

  const ptyProcess = pty.spawn('bash', [scriptPath, buildType], {
      name: 'xterm-color',
      // ...
      cwd: PROJECT_ROOT,
      env: { ...process.env, FORCE_COLOR: '1', BUILD_BY_SERVICE: '1' }
  });
  ```

- **Issue:** An authenticated admin could trigger a PTY-backed `bash` build with the hardcoded `auto-release` argument, and the function later executed `pm2 restart redball-update-server` via `child_process.exec`.
- **Proposed fix:** (applied) Validate `body.type` against an allow-list, default to `windows`, remove the `pm2` self-restart, and require a manual restart after a successful build.
- **Verification:** `node -c` and `npm test`
- **Action:** fixed

## ISS-007: Unsanitized `version` path parameter used in filesystem operations

- **Scanner:** S10 Security
- **Severity:** high
- **Risk:** medium
- **Location:** `/root/Redball/update-server/server.js:25-35`
- **Evidence:**

  ```javascript
  function isValidVersion(version) {
    return /^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9._-]+)?$/.test(version);
  }

  app.param('version', (req, res, next, version) => {
    if (!isValidVersion(version)) {
      return res.status(400).json({ error: 'Invalid version format' });
    }
    next();
  });
  ```

- **Issue:** The `version` route parameter was placed directly in a path. Malicious values like `..` could delete or list files outside `RELEASES_DIR`.
- **Proposed fix:** (applied) Added `app.param('version', ...)` middleware to validate all `:version` route parameters against a strict semver regex.
- **Verification:** `npm test` and targeted curl tests
- **Action:** fixed

## ISS-008: update-server serves downloads without security headers

- **Scanner:** S8 API / S10 Security
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/update-server/server.js:23-24`
- **Evidence:**

  ```javascript
  const helmet = require('helmet');
  // ...
  const app = express();
  app.use(helmet());
  ```

- **Issue:** Static files (including release downloads) are served without `helmet`, `X-Content-Type-Options`, `X-Frame-Options`, or CSP.
- **Proposed fix:** (applied) Added `helmet` middleware to the Express pipeline.
- **Verification:** `npm test` and manual header inspection
- **Action:** fixed

## ISS-009: Browser extension content script matches all URLs

- **Scanner:** S10 Security
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/browser-extension/manifest.json:18-23` and `40-44`
- **Evidence:**

  ```json
  "content_scripts": [{
      "matches": ["http://localhost:5000/*"],
      "js": ["content.js"],
      "run_at": "document_idle"
  }]
  ```

- **Issue:** The content script was injected on every page, which is unnecessarily broad for a keep-awake utility and raises privacy/review concerns.
- **Proposed fix:** (applied) Restrict `matches` to the local Redball API origin; also narrowed `web_accessible_resources`.
- **Verification:** `node -e` JSON.parse on `manifest.json`
- **Action:** fixed

## ISS-010: Generic `throw new Exception` in update installer

- **Scanner:** S1 Codebase Health
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/UpdateService.cs:849-1002` (multiple)
- **Evidence:**

  ```csharp
  if (!await DownloadFileAsync(updateInfo.DownloadUrl, zipPath, progress, cancellationToken))
      throw new HttpRequestException("Failed to download ZIP for differential update");
  // ...
  throw new InvalidOperationException("Failed to extract ZIP for differential update");
  throw new HttpRequestException($"Failed to download {fileShortName}");
  throw new InvalidDataException($"Integrity check failed for {fileShortName}");
  ```

- **Issue:** `throw new Exception` throws the base exception type with no inner exception, making error classification and upstream handling harder.
- **Proposed fix:** (applied) Replaced network failures with `HttpRequestException`, extraction failures with `InvalidOperationException`, and integrity failures with `InvalidDataException`.
- **Verification:** `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj` (must be run on Windows/.NET 10 host)
- **Action:** fixed

## ISS-011: Empty catch block in `GamingModeService`

- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/GamingModeService.cs:105-120`
- **Evidence:**

  ```csharp
  catch (ArgumentException)
  {
      // Process exited between detection and lookup
      processName = "Unknown";
  }
  catch (Exception ex)
  {
      Logger.Debug("GamingModeService", $"Unexpected process lookup error for PID {processId}: {ex.Message}");
      processName = "Unknown";
  }
  ```

- **Issue:** The inner `catch` swallows all exceptions silently, including unexpected ones.
- **Proposed fix:** (applied) Catch `ArgumentException` (process already exited) and `Exception` (log Debug and set `processName = "Unknown"`).
- **Verification:** `dotnet build ...` and runtime test (must be run on Windows/.NET 10 host)
- **Action:** fixed

## ISS-012: `throw new Exception` in audit hash chain verification

- **Scanner:** S1 Codebase Health
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.Core/Security/SecurityAuditService.cs:73`
- **Evidence:**

  ```csharp
  if (entry == null) throw new InvalidDataException("Invalid JSON formatting in audit log");
  ```

- **Issue:** Throws the base `Exception` type instead of a domain-specific or `InvalidDataException`, making exception filtering by callers harder.
- **Proposed fix:** (applied) Replaced with `InvalidDataException`.
- **Verification:** `dotnet build src/Redball.Core/Redball.Core.csproj` (must be run on Windows/.NET 10 host)
- **Action:** fixed

## ISS-013: Non-atomic analytics data flush risks file corruption

- **Scanner:** S1 Codebase Health
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/AnalyticsService.cs:772-774`
- **Evidence:**

  ```csharp
  var json = JsonSerializer.Serialize(_data, options);
  var tempFile = AnalyticsFile + ".tmp";
  File.WriteAllText(tempFile, json);
  File.Move(tempFile, AnalyticsFile, overwrite: true);
  ```

- **Issue:** Writing directly to the analytics file can corrupt it if the process is terminated mid-write. `ConfigService` already uses an atomic temp-file + `File.Move` pattern.
- **Proposed fix:** (applied) Adopted the atomic write pattern: write to `AnalyticsFile + ".tmp"`, then `File.Move(..., overwrite: true)`.
- **Verification:** `dotnet build ...` and unit test (must be run on Windows/.NET 10 host)
- **Action:** fixed

## ISS-014: Non-atomic session stats save

- **Scanner:** S1 Codebase Health
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/SessionStatsService.cs:179-181`
- **Evidence:**

  ```csharp
  var json = JsonSerializer.Serialize(_data, SerializerOptions);
  var tempFile = _statsFile + ".tmp";
  File.WriteAllText(tempFile, json);
  File.Move(tempFile, _statsFile, overwrite: true);
  ```

- **Issue:** Same as ISS-013 — non-atomic write risks corruption on crash.
- **Proposed fix:** (applied) Use temp-file + `File.Move(..., overwrite: true)`.
- **Verification:** `dotnet build ...` and unit test (must be run on Windows/.NET 10 host)
- **Action:** fixed

## ISS-015: AGENT.md lists removed project directories

- **Scanner:** S2 Documentation
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/AGENT.md:55-57`
- **Evidence:**
  ```text
  │   ├── Redball.Service/          # Windows Service components
  │   └── Redball.SessionHelper/    # Session helper executable
  ```

- **Issue:** `src/` only contains `Redball.Core` and `Redball.UI.WPF`; the listed directories no longer exist, misleading new agents/developers.
- **Proposed fix:** Delete the two lines referencing `Redball.Service` and `Redball.SessionHelper`.
- **Verification:** `ls /root/Redball/src`
- **Action:** auto-fix

## ISS-016: WIKI.md incorrectly names the test framework

- **Scanner:** S2 Documentation
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/WIKI.md:103`
- **Evidence:**
  ```text
  - **Unit Tests**: MSTest suites for core services.
  ```

- **Issue:** The project uses xUnit, not MSTest.
- **Proposed fix:** Change `MSTest` to `xUnit`.
- **Verification:** Read-back of `WIKI.md`
- **Action:** auto-fix

## ISS-017: WIKI.md section numbering out of order

- **Scanner:** S2 Documentation
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/WIKI.md:115`
- **Evidence:**
  ```text
  ## 5. Troubleshooting Reference
  ```

- **Issue:** Section 5 follows section 6, breaking the document's numbering sequence.
- **Proposed fix:** Renumber to `## 7. Troubleshooting Reference`.
- **Verification:** Read-back of `WIKI.md`
- **Action:** auto-fix

## ISS-018: web-admin package.json declares fake built-in dependencies

- **Scanner:** S10 Security
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/web-admin/package.json:14-19`
- **Evidence:**

  ```json
  "dependencies": {
    "child_process": "^1.0.2",
    "fs": "^0.0.1-security",
    "path": "^0.12.7"
  }
  ```

- **Issue:** `fs`, `path`, and `child_process` are Node.js built-ins. The npm packages with the same names are placeholders and introduce supply-chain risk.
- **Proposed fix:** (applied) Removed the three entries from `dependencies` and ran `npm install` to regenerate `package-lock.json`.
- **Verification:** `npm install` followed by `node -c server.js` and `npm test`
- **Action:** fixed

## ISS-019: web-admin has no usable test command

- **Scanner:** S4 UX / S2 Documentation
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/web-admin/package.json:7`
- **Evidence:**

  ```json
  "test": "echo \"Error: no test specified\" && exit 1"
  ```

- **Issue:** The test script is a hard failure, breaking CI pipelines and making it impossible to run the test target.
- **Proposed fix:** Replace with `"test": "echo \"No tests configured\" && exit 0"` and create at least one minimal test, or install a test framework.
- **Verification:** `npm test`
- **Action:** auto-fix

## ISS-020: NSIS installer version does not match build props

- **Scanner:** S2 Documentation / S6 Uniformity
- **Severity:** low
- **Risk:** medium
- **Location:** `/root/Redball/installer/Redball.nsi:25-26`
- **Evidence:**
  ```nsis
  !define PRODUCT_VERSION "2.1.720.0"
  !define PRODUCT_VERSION_SHORT "2.1.720"
  ```

- **Issue:** The installer still advertises version `2.1.443` while `Directory.Build.props` and the project are at `2.1.720`. This causes installer/published artifact mismatch if the build script does not override these defines.
- **Proposed fix:** Update the defines to `2.1.720.0` and `2.1.720`, or ensure the build script reliably replaces them.
- **Verification:** `grep PRODUCT_VERSION installer/Redball.nsi` matches `Directory.Build.props`
- **Action:** fixed

<!-- Second audit run (prompt v7.0.0-redball). IDs resume at ISS-021. -->

## ISS-021: Remaining generic `throw new Exception` in UpdateService

- **Area:** services
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/UpdateService.cs:987,1027`
- **Evidence:**

  ```csharp
  throw new Exception($"No download source available for {fileShortName}");
  if (scriptPath == null) throw new Exception("Failed to create update script");
  ```

- **Issue:** ISS-010 replaced generic `Exception` elsewhere in this file but these two sites remain; callers cannot distinguish failure modes.
- **Proposed fix:** `InvalidOperationException` for both (state/operation failures), matching the ISS-010 precedent.
- **Verification:** `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj`
- **Action:** auto-fix

## ISS-022: Non-atomic file-hash cache write in UpdateService

- **Area:** services
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/UpdateService.cs:77`
- **Evidence:** `File.WriteAllText(_cacheFilePath, json);` inside `SaveCache()`.
- **Issue:** A crash mid-write corrupts the hash cache — same class as ISS-013/014.
- **Proposed fix:** Write to `tempFile` then `File.Move(temp, target, overwrite: true)`.
- **Verification:** `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj`
- **Action:** auto-fix

## ISS-023: Non-atomic session-state save

- **Area:** services
- **Scanner:** S1 Codebase Health
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/SessionStateService.cs:50`
- **Evidence:** `File.WriteAllText(statePath, json);`
- **Issue:** This file feeds crash recovery; a torn write loses the resume state.
- **Proposed fix:** temp-file + `File.Move` (atomic), keep `Logger` call.
- **Verification:** `dotnet build` + `dotnet test --filter SessionStateService`
- **Action:** auto-fix

## ISS-024: Non-atomic TypeThing templates flush

- **Area:** services
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/TemplateService.cs:93`
- **Evidence:** `File.WriteAllText(_templatesFile, json);` in `Flush()`.
- **Issue:** User templates can be lost to a partial write.
- **Proposed fix:** temp-file + `File.Move`.
- **Verification:** `dotnet build`
- **Action:** auto-fix

## ISS-025: Non-atomic profile save

- **Area:** services
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/ProfileService.cs:64`
- **Evidence:** `File.WriteAllText(path, json);` in `SaveProfile()`.
- **Issue:** User profiles can be corrupted by a partial write.
- **Proposed fix:** temp-file + `File.Move`.
- **Verification:** `dotnet build`
- **Action:** auto-fix

## ISS-026: Non-atomic security-policy cache write

- **Area:** services
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/SecurityPolicyService.cs:487`
- **Evidence:** `File.WriteAllText(_policyCachePath, json);`
- **Issue:** Corrupted policy cache on crash; load path then falls back silently.
- **Proposed fix:** temp-file + `File.Move`.
- **Verification:** `dotnet build`
- **Action:** auto-fix

## ISS-027: Non-atomic crash-telemetry writes (consent, crash store, upload queue)

- **Area:** core
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.Core/Telemetry/CrashTelemetryService.cs:105,168,176`
- **Evidence:** Three `File.WriteAllText` calls for `_consentFilePath`, `crashFile`, `queueFile`.
- **Issue:** Consent state and queued crash reports can corrupt on a mid-write crash.
- **Proposed fix:** temp-file + `File.Move` for all three.
- **Verification:** `dotnet build src/Redball.Core/Redball.Core.csproj`
- **Action:** auto-fix

## ISS-028: Non-atomic consent write in CrossPlatformAnalyticsSync

- **Area:** core
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.Core/Sync/CrossPlatformAnalyticsSync.cs:105`
- **Evidence:** `File.WriteAllText(_consentFilePath, json);`
- **Issue:** Consent file corrupts on partial write.
- **Proposed fix:** temp-file + `File.Move`.
- **Verification:** `dotnet build src/Redball.Core/Redball.Core.csproj`
- **Action:** auto-fix

## ISS-029: HighContrast theme exists but is unreachable from every theme picker

- **Area:** wpf
- **Scanner:** S11 Consistency
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Views/MainWindow.xaml:367-381`, `OnboardingWindow.xaml:125-137`, `SettingsWindow.xaml:60-72`; mappings in `MainWindow.Settings.cs:140-155,288-303`, `OnboardingWindow.xaml.cs:56-71,77-92`, `SettingsWindow.xaml.cs:~90-105,~280-295,~365-380`
- **Evidence:** `enum Theme` includes `HighContrast` (ThemeManager.cs:447) with a full `ApplyThemeVariant` case (line 319) and `ThemeFromString` mapping (line 399), but no ComboBox offers it; selecting index 13 is impossible and config `"HighContrast"` displays as Dark (`_ => 1`).
- **Issue:** A complete, implemented theme is dead UI surface.
- **Proposed fix:** Add `<ComboBoxItem Content="High Contrast"/>` at index 13 in the three live pickers and extend all index↔name/enum switches with `13`/ `"HighContrast"`.
- **Verification:** `dotnet build` — plus manual picker check on Windows.
- **Action:** auto-fix

## ISS-030: "Slate Grey" vs "Slate Gray" label inconsistency across theme pickers

- **Area:** wpf
- **Scanner:** S11 Consistency
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Views/OnboardingWindow.xaml:133` and `SettingsSectionView.xaml:30` ("Slate Grey") vs `MainWindow.xaml:376` and `SettingsWindow.xaml:68` ("Slate Gray")
- **Issue:** Same theme spelled two ways; harmless (index-mapped) but inconsistent UX.
- **Proposed fix:** Normalize to "Slate Gray" (matches `Theme.SlateGray`).
- **Verification:** `dotnet build`
- **Action:** auto-fix

## ISS-031: eslint config doesn't ignore vendored/minified assets — hundreds of false errors

- **Area:** scripts
- **Scanner:** S10 Tests (lint gate)
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/eslint.config.mjs:3`
- **Evidence:** `npx eslint .` → 373 problems; the bulk are inside `web-admin/public/vendor/chart.umd.min.js` and other generated/vendored files the lint config never excludes.
- **Issue:** The lint gate is unusable — real first-party errors drown in vendored noise.
- **Proposed fix:** Extend `ignores` with `**/vendor/**`, `**/*.min.js`, `**/dist/**`, `**/node_modules/**`, `**/coverage*/**`, `**/releases/**`, `site/dist/**`, `update-server/releases/**`.
- **Verification:** `npx eslint .` error count drops to first-party only.
- **Action:** auto-fix

## ISS-032: First-party eslint errors — unused vars, prefer-const, eqeqeq

- **Area:** update-server | web-admin | extension | scripts
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `update-server/server.js` (unused imports `getAdminUser`/`getJwtSecret`/`generateToken`/`verifyPassword`, `authLimiter`, `AUTH_FILE`, `BUILD_STATE_DEBOUNCE_MS`, `CLEANUP_INTERVAL`, `_pendingSave`, `ALLOWED_MIME_TYPES`, `getSystemStats`, stray `next`, `buildState`→const, `line`→const, unused `e`/`err` catch params), `update-server/lib/auth.js` (unused catch params ×5), `update-server/routes/api/auth.js:139`, `routes/api/releases.js:200` (unused `release`), `update-server/public/js/site.js` (`animId`, `start`, `handleDemoSubmit`), `web-admin/server.js` (`BUILD_LOG_FILE`, catch params, `totalDownloads`, `buildType`), `browser-extension/{background,content,popup}.js` (`CHECK_INTERVAL_MS`, `redballService`, `error` params), `trigger-build.js` (`fs`, `path`)
- **Issue:** Real lint errors (not vendored noise) — dead imports/vars, `var`/`let` misuse, `==` vs `===`.
- **Proposed fix:** Remove dead bindings, convert to `const`, `===` — mechanical, no behavior change. Complexity violations are tracked separately (ISS-046).
- **Verification:** `npx eslint .` on touched files + `node -c` each edited file + `npm test` (update-server).
- **Action:** auto-fix

## ISS-033: Extension manifest declares missing `injected.js` web_accessible_resource

- **Area:** extension
- **Scanner:** S11 Consistency
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/browser-extension/manifest.json:41-46`
- **Evidence:** `"resources": ["injected.js"]` but no such file exists in `browser-extension/`; nothing references it.
- **Proposed fix:** Remove the dead `web_accessible_resources` block.
- **Verification:** `python3 -m json.tool browser-extension/manifest.json`
- **Action:** auto-fix

## ISS-034: AGENT.md drift — phantom services and stale service count

- **Area:** docs
- **Scanner:** S2 Documentation / S11 Consistency
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/AGENT.md`
- **Evidence:** Key-services table lists `InterceptionInputService` and `SecretManagerService` — neither file exists under `src/Redball.UI.WPF/Services/` (nor anywhere in `src/`). "40+ singleton services" undercounts the actual ~73 files.
- **Proposed fix:** Update the table to real services (e.g. `InterceptionInputService` → actual input services present) and the count wording.
- **Verification:** names in doc resolve to files on disk.
- **Action:** auto-fix

## ISS-035: Solution file omits Redball.Core, tests-e2e, and Benchmarks projects

- **Area:** tests
- **Scanner:** S11 Consistency
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/Redball.v3.sln`
- **Evidence:** `dotnet sln list` includes UI.WPF + 4 test projects but not `src/Redball.Core/Redball.Core.csproj`, `tests-e2e/Redball.E2E.Tests.csproj`, or `tests/Redball.Benchmarks/Redball.Benchmarks.csproj`.
- **Issue:** `dotnet build Redball.v3.sln` silently skips a shipping library and a test project.
- **Proposed fix:** `dotnet sln add` the three projects.
- **Verification:** `dotnet sln list` shows 8 projects; `dotnet build Redball.v3.sln` compiles Core directly.
- **Action:** auto-fix

## ISS-036: `updateUI` in extension popup exceeds complexity limit

- **Area:** extension
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/browser-extension/popup.js:57`
- **Evidence:** eslint `complexity` 11 > 10.
- **Proposed fix:** Extract the disconnected/connected branch rendering into a small helper.
- **Verification:** `npx eslint browser-extension/popup.js`
- **Action:** auto-fix

## ISS-037: Unit test project does not compile — ~161 errors from API drift and triple-framework references

- **Area:** tests
- **Scanner:** S10 Tests
- **Severity:** high
- **Risk:** high
- **Location:** `/root/Redball/tests/` — `SecurityAuditServiceTests.cs`, `SecurityUtilitiesTests.cs`, `ServiceOrchestrationTests.cs`, `ConvertersTests.cs`, `Chaos/ChaosEngineeringTests.cs`, `UpdateSystemE2ETests.cs`, `ViewModels/CommandPaletteViewModelTests.cs`
- **Evidence:** `dotnet test tests/Redball.Tests.csproj` → CS0104 `Assert` ambiguous (MSTest + xUnit both referenced in csproj lines 17-22: MSTest.TestAdapter, MSTest.TestFramework, NUnit, NUnit3TestAdapter, xunit, xunit.runner.visualstudio, Microsoft.Playwright.NUnit, FlaUI); CS1061 missing `LogAuthentication`/`LogTamperDetection`/`LogCryptoOperation`/`LogUpdateEvent`/`QueryEvents`/`Flush` on `Redball.UI.Services.SecurityAuditService`; missing types `SecurityAuditEvent`, `SliderValueToWidthConverter`, `MemoryPoolService`, `CommandItem`, `PaletteCommand.DisplayName`, `SearchQuery`; `UpdateSystemE2ETests.cs:564` signature mismatch.
- **Issue:** The primary test suite is uncompilable — no unit coverage is actually exercised, and the csproj pulls in three mutually-conflicting test frameworks plus Playwright/FlaUI.
- **Proposed fix:** Decide one framework (xUnit — the repo standard per WIKI.md), remove MSTest/NUnit refs, convert MSTest-style asserts (`Assert.AreSame` → `Assert.Same` etc.), reconcile or rewrite the drifted test files against current APIs (`SecurityAuditService.LogEvent`, `CommandPaletteViewModel`), drop or relocate Playwright/FlaUI deps. Also disambiguate the duplicate `SecurityAuditService` classes (Core vs UI.WPF).
- **Verification:** `dotnet test` compiles and runs.
- **Action:** queue-patch [REQUIRES APPROVAL]

## ISS-038: update-server mutating routes missing `requireAdmin` — any viewer can publish/delete releases and trigger builds

- **Area:** update-server
- **Scanner:** S7 Security
- **Severity:** high
- **Risk:** medium
- **Location:** `/root/Redball/update-server/server.js` — `POST /api/releases` (628), `POST /api/releases/:version/upload` (663), `DELETE /api/releases/:version` (712), `PATCH /api/releases/:version` (730), `POST /api/publish` (742), `POST /api/config` (1008), `POST /api/admin/generate-patches` (1093), `POST /api/admin/build|/api/build/start` (1391), `POST /api/build/stop` (1400); `requireAdmin` only used inside `routes/api/auth.js` user management.
- **Evidence:** `lib/auth.js` `addUser` defaults `role='viewer'`; `middleware/auth.js` exports `requireAdmin` but server.js never applies it — every `authenticateToken`-only POST/DELETE above executes for a viewer token.
- **Issue:** Authenticated-but-unprivileged users can publish, overwrite, and delete release artifacts and kick off the build pipeline.
- **Proposed fix:** Add `requireAdmin` to all state-changing routes; keep `authenticateToken`-only on read-only/status endpoints.
- **Verification:** `cd update-server && npm test`; manual check that a viewer token receives 403 on `POST /api/releases`.
- **Action:** queue-patch [REQUIRES APPROVAL]

## ISS-039: site/server.js exposes unauthenticated admin endpoints that trigger the release pipeline

- **Area:** site
- **Scanner:** S7 Security
- **Severity:** critical
- **Risk:** medium
- **Location:** `/root/Redball/site/server.js:23-63`
- **Evidence:**

  ```js
  app.post('/api/admin/build',   (req,res) => spawn('npm',['run','build'],   {cwd: repoRoot}))
  app.post('/api/admin/release', (req,res) => spawn('npm',['run','release'], {cwd: repoRoot}))
  app.get('/api/admin/logs',   ...) // reads build.log
  app.get('/api/admin/config', ...) // reads and serves repo-root Redball.json
  ```

  `npm run build` at repo root executes `bash ./scripts/build.sh` **with no args = auto-release** (version bump → git commit → git push → publish to update-server → GitHub release). No auth middleware anywhere; the listener binds `0.0.0.0`.
- **Issue:** Any network peer can trigger a full release (with real git push and GitHub publish side effects), read build logs, and dump the committed `Redball.json`.
- **Proposed fix:** Remove the `/api/admin/*` block (the React `AdminDashboard` calls `/api/admin/status`, which this server doesn't even implement — it expects the authenticated update-server API) or gate it behind a shared-secret/token check; at minimum refuse to spawn the release script.
- **Verification:** `node -c site/server.js`; `curl -X POST localhost:PORT/api/admin/build` → 401/404.
- **Action:** queue-patch [REQUIRES APPROVAL]

## ISS-040: site/server.js default port collides with update-server

- **Area:** site
- **Scanner:** S6 API & Contracts / S1
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/site/server.js:11` — `const PORT = process.env.PORT || 3500;`
- **Evidence:** update-server also owns `:3500` (`scripts/build.sh` health-checks `localhost:3500/api/health`). Whichever process starts second dies with EADDRINUSE.
- **Proposed fix:** Change the default to an unused port (e.g. 3600) and document it; `PORT` env override already works.
- **Verification:** `node site/server.js` alongside update-server does not crash.
- **Action:** queue-patch

## ISS-041: web-admin exposes build status and full build log without authentication

- **Area:** web-admin
- **Scanner:** S7 Security
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/web-admin/server.js:305-311`
- **Evidence:** `GET /api/build/status` and `GET /api/build/log` have no `authenticateToken`; `buildState.log` contains the raw auto-release console output (paths, versions, command echoes).
- **Proposed fix:** Add `authenticateToken` to both GETs.
- **Verification:** `curl localhost:PORT/api/build/log` → 401.
- **Action:** queue-patch

## ISS-042: web-admin `/api/config` POST merges unvalidated client JSON into public default config

- **Area:** web-admin
- **Scanner:** S7 Security
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/web-admin/server.js:513-522`
- **Evidence:** `const newConfig = { ...currentConfig, ...req.body };` — any authenticated user can inject arbitrary keys/values which are then served to all clients via the unauthenticated `GET /api/defaults`.
- **Proposed fix:** Whitelist/validate against the `DEFAULT_CLIENT_CONFIG` key set (and coerce types) before persisting.
- **Verification:** POST with a bogus key → 400 or key stripped; `npm test`.
- **Action:** queue-patch

## ISS-043: web-admin `startBuild` ignores `buildType` and always runs `auto-release`

- **Area:** web-admin
- **Scanner:** S7 Security / S1
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/web-admin/server.js:317,706-733`
- **Evidence:** `startBuild((req.body && req.body.type) || 'windows')` accepts a type, but `startBuild(buildType)` never uses the parameter (eslint: `'buildType' is assigned a value but never used`) and unconditionally `pty.spawn('bash', [build.sh, 'auto-release'])`.
- **Issue:** The UI implies a build-only action; every invocation actually performs the full commit/push/publish pipeline.
- **Proposed fix:** Mirror the update-server allow-list approach (`scripts/build.sh` subcommands) or fix the arg to a build-only command; reject unknown types.
- **Verification:** build button performs `build.sh windows` not auto-release.
- **Action:** queue-patch [REQUIRES APPROVAL]

## ISS-044: update-server treats invalid/missing build type as `auto-release`

- **Area:** update-server
- **Scanner:** S7 Security
- **Severity:** low
- **Risk:** medium
- **Location:** `/root/Redball/update-server/server.js:1475-1476`
- **Evidence:** `const buildType = allowedBuildTypes.includes(body?.type) ? body.type : 'auto-release';` — an empty or bogus type silently escalates to the most destructive option (commit+push+publish) instead of being rejected or defaulting to a build-only subcommand.
- **Proposed fix:** Default to `windows` (or reject with 400 when `body.type` is provided but not allowed).
- **Verification:** POST `/api/build/start` with `{}` starts a build-only job.
- **Action:** queue-patch

## ISS-045: Tracked dead/dangerous files — unauthenticated legacy server, cred-guessing script, build artifacts

- **Area:** web-admin | scripts | wpf | repo hygiene
- **Scanner:** S7 Security / S1
- **Severity:** high
- **Risk:** low
- **Location:**
  - `/root/Redball/web-admin/server-old.js` — 287-line legacy server: unauthenticated `POST /api/build/start` → `pty.spawn('bash',[build.sh,'auto-release'])`, unauthenticated WS `start-build`/`stop-build`, `err.message` leaks. Not referenced by `package.json`/`ecosystem.config.js`.
  - `/root/Redball/trigger-build.js` — dev script attempting login with hardcoded `admin`/`admin` ("Trying common default password"), unused `fs`/`path` imports.
  - `/root/Redball/src/Redball.UI.WPF/Views/SettingsSectionView.xaml(.cs)` — full settings UserControl referenced nowhere (only its own generated `.g.cs` in `obj/`).
  - `/root/Redball/core.508609`, `/root/Redball/core.530235` — **committed core dumps** (may contain process memory incl. secrets).
  - `/root/Redball/build.pid`, 5× `src/Redball.UI.WPF/Redball.UI.WPF_*_wpftmp.csproj` — tracked build artifacts.
- **Issue:** A single `node server-old.js` exposes unauthenticated release-trigger; core dumps leak memory; temp files bloat the repo.
- **Proposed fix:** `git rm` the files above; add `*_wpftmp.csproj`, `core.*`, `build.pid` to `.gitignore`; delete `SettingsSectionView.xaml` + code-behind.
- **Verification:** `git status` clean; `dotnet build` still passes after view removal.
- **Action:** queue-patch

## ISS-046: eslint `complexity` violations in first-party code need real refactors

- **Area:** update-server | web-admin | extension
- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** medium
- **Location:** `update-server/lib/analytics.js` `trackEvent`(15)/`getAnalytics`(18), `update-server/lib/delta-patches.js` `generatePatches`(17), `update-server/public/js/site.js` `syncData`(26), `update-server/server.js` arrow fns at ~120(11), ~750(13), ~844(13), ~1020(12), ~1214(17), ~1490(14), `browser-extension/popup.js` handled by ISS-036.
- **Issue:** Functions exceed the repo's own `complexity ≤ 10` rule; mechanical fixes aren't safe (these paths touch auth/publishing).
- **Proposed fix:** Extract cohesive helpers per function; verify with `npm test` + eslint.
- **Verification:** `npx eslint .` complexity errors = 0.
- **Action:** queue-patch

## ISS-047: Browser extension targets `http://localhost:5000`, but nothing in the app listens there

- **Area:** extension
- **Scanner:** S6 API & Contracts
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/browser-extension/background.js:3` (`REDBALL_API_URL = 'http://localhost:5000'`), `manifest.json` host_permissions
- **Evidence:** `grep -rn "5000|HttpListener|TcpListener|Kestrel" src/` — no HTTP server exists in the desktop app; every extension call fails. The shipped extension is a dead feature.
- **Issue:** Feature is advertised (manifest, popup UI for start/stop/timed keep-awake) but can never connect.
- **Proposed fix:** Product decision — either implement the localhost control API (OPP-001) or remove/mark the extension experimental.
- **Verification:** extension status check succeeds, or docs mark it non-functional.
- **Action:** queue-patch

## ISS-048: Stored XSS via unescaped `innerHTML` interpolation in web-admin dashboard

- **Area:** web-admin
- **Scanner:** S7 Security
- **Severity:** high
- **Risk:** low
- **Location:** `/root/Redball/web-admin/public/admin.js` — build-log lines (`${cleanLine}` at ~741), releases table (`${r.version}` ~514-521), users table (`${u.username}`/`${u.username[0]}` ~791-805), analytics labels (~1059)
- **Evidence:** Server/API data and raw build-log output are interpolated into `innerHTML` template literals with no escaping helper — a `<img onerror>` in a username, release version, or build log line executes in every admin's session.
- **Proposed fix:** Add an `escapeHtml` helper and apply it at each interpolation; keep intentional markup (SVG icons) static.
- **Verification:** `node -c` equivalent (eslint parse) + manual injection of `<script>` in a version string renders as text.
- **Action:** auto-fix

## ISS-049: Potential XSS via unescaped `innerHTML`/`href` interpolation in update-server public site.js

- **Area:** update-server
- **Scanner:** S7 Security
- **Severity:** medium
- **Risk:** low
- **Location:** `/root/Redball/update-server/public/js/site.js:281-310`
- **Evidence:** `versionList.innerHTML = recentReleases.map(release => ...)` interpolates `release.version` and builds `href="${downloadUrl}"` from `setupFile.url`/file name — a crafted stored release name/URL could inject markup or a `javascript:` URL on the public downloads page.
- **Proposed fix:** Escape interpolated fields and validate `downloadUrl` scheme (http/https or leading `/`) before embedding.
- **Verification:** eslint parse + payload renders as text.
- **Action:** auto-fix
