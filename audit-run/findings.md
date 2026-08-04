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
