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
- **Location:** `/root/Redball/update-server/lib/auth.js:48-54`
- **Evidence:**

  ```javascript
  const DEFAULT_USER = {
      username: 'admin',
      passwordHash: '$2b$10$vbyR65jpIlk9tN99d.p33u4A7mdwXcdarb.5iR72VlJScEF9CJaQi', // 'admin'
      role: 'admin',
      mfaEnabled: false,
      mfaSecret: null,
      createdAt: new Date().toISOString()
  };
  ```

- **Issue:** A default admin account with a known, hardcoded password hash is shipped in source. If the admin does not change it on first run, an attacker can authenticate with `admin:admin`.
- **Proposed fix:** Remove the hardcoded `DEFAULT_USER` password; generate/force a setup password on first run or accept an initial hash via environment variable. Implement a `mustChangePassword` flag.
- **Verification:** Manual review of `/login` behavior
- **Action:** queue-patch

## ISS-003: Hardcoded default admin credentials in web-admin

- **Scanner:** S10 Security
- **Severity:** critical
- **Risk:** high
- **Location:** `/root/Redball/web-admin/server.js:117-121`
- **Evidence:**

  ```javascript
  const defaultUser = {
    username: 'admin',
    // Default password: 'redball2026' - change after first login
    passwordHash: '$2b$10$NH.q4MEoGZUuC4kHmv8B2uLh4OXdPVwa2Q/CKp/ORwSMG2XvhGQ8e'
  };
  ```

- **Issue:** The web admin ships with a hardcoded, documented default password (`redball2026`) and no enforcement of a mandatory change.
- **Proposed fix:** Remove the hardcoded hash and require a setup-time password (env var or interactive). Track a `mustChangePassword` flag and force change on first login.
- **Verification:** Manual review of `/api/auth/login` and `/api/auth/change-password`
- **Action:** queue-patch

## ISS-004: No rate limiting on web-admin login endpoint

- **Scanner:** S10 Security
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/web-admin/server.js:221-250`
- **Evidence:**

  ```javascript
  app.post('/api/auth/login', async (req, res) => {
    const { username, password } = req.body;
    // ...
    const validPassword = await bcrypt.compare(password, admin.passwordHash);
  ```

- **Issue:** The `/api/auth/login` endpoint accepts unlimited attempts, enabling online brute-force attacks against the single admin account.
- **Proposed fix:** Add `express-rate-limit` middleware specifically for `/api/auth/login` (e.g., 5 attempts per 15 minutes per IP) and consider account lockout.
- **Verification:** `npm test` and manual curl attempts
- **Action:** queue-patch

## ISS-005: Error messages leaked to clients in web-admin

- **Scanner:** S8 API / S10 Security
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/web-admin/server.js:317` (and similar patterns)
- **Evidence:**

  ```javascript
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
  ```

- **Issue:** Internal `Error` messages are forwarded to API clients, potentially exposing file paths, stack frames, or internal state.
- **Proposed fix:** Return a generic error object (e.g., `{ error: 'Internal server error' }`) and log the original error server-side.
- **Verification:** `npm test` and manual requests
- **Action:** queue-patch

## ISS-006: update-server exposes build endpoint via pty.spawn

- **Scanner:** S10 Security
- **Severity:** critical
- **Risk:** high
- **Location:** `/root/Redball/update-server/server.js:1439-1529`
- **Evidence:**

  ```javascript
  const ptyProcess = pty.spawn('bash', [scriptPath, 'auto-release'], {
      name: 'xterm-color',
      // ...
      cwd: PROJECT_ROOT,
      env: { ...process.env, FORCE_COLOR: '1', BUILD_BY_SERVICE: '1' }
  });
  ```

- **Issue:** An authenticated admin can trigger a PTY-backed `bash` build with `auto-release`, and the function later runs `pm2 restart`. Any compromise of the admin token or a command-injection in the build pipeline grants arbitrary shell access on the server.
- **Proposed fix:** Validate and sanitize all inputs; run builds in an isolated, resource-limited sandbox; avoid `auto-release` without explicit approval; restrict `pm2` and PTY usage; require multi-step approval for release builds.
- **Verification:** Manual code review and runtime permission tests
- **Action:** queue-patch

## ISS-007: Unsanitized `version` path parameter used in filesystem operations

- **Scanner:** S10 Security
- **Severity:** high
- **Risk:** medium
- **Location:** `/root/Redball/update-server/server.js:996-1019` and `696-699`
- **Evidence:**

  ```javascript
  const patchesDir = path.join(RELEASES_DIR, req.params.version, 'patches');
  // ...
  const releaseDir = path.join(RELEASES_DIR, req.params.version);
  if (fs.existsSync(releaseDir)) {
    fs.rmSync(releaseDir, { recursive: true, force: true });
  }
  ```

- **Issue:** The `version` route parameter is placed directly in a path. Malicious values like `..` could delete or list files outside `RELEASES_DIR`.
- **Proposed fix:** Apply the same sanitization pattern used in `/downloads/:version/:filename` (regex or `path.basename` + resolved-path check) to all `version` parameters.
- **Verification:** `npm test` and targeted curl tests
- **Action:** queue-patch

## ISS-008: update-server serves downloads without security headers

- **Scanner:** S8 API / S10 Security
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/update-server/server.js:1573-1575`
- **Evidence:**

  ```javascript
  app.use('/admin', express.static(ADMIN_PUBLIC, { index: 'admin.html' }));
  app.use('/downloads', express.static(RELEASES_DIR));
  app.use(express.static(SITE_PUBLIC, { index: 'index.html' }));
  ```

- **Issue:** Static files (including release downloads) are served without `helmet`, `X-Content-Type-Options`, `X-Frame-Options`, or CSP.
- **Proposed fix:** Add `helmet` middleware to the Express pipeline and configure CSP / CORS explicitly.
- **Verification:** `npm test` and manual header inspection
- **Action:** queue-patch

## ISS-009: Browser extension content script matches all URLs

- **Scanner:** S10 Security
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/browser-extension/manifest.json:18-23`
- **Evidence:**

  ```json
  "content_scripts": [{
      "matches": ["<all_urls>"],
      "js": ["content.js"],
      "run_at": "document_idle"
  }]
  ```

- **Issue:** The content script is injected on every page, which is unnecessarily broad for a keep-awake utility and raises privacy/review concerns.
- **Proposed fix:** Restrict `matches` to specific origins, or replace with the `activeTab` permission and inject only when the user opens the popup.
- **Verification:** Load extension in a browser and confirm script scope
- **Action:** queue-patch

## ISS-010: Generic `throw new Exception` in update installer

- **Scanner:** S1 Codebase Health
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/UpdateService.cs:849-1027` (multiple)
- **Evidence:**

  ```csharp
  if (!await DownloadFileAsync(updateInfo.DownloadUrl, zipPath, progress, cancellationToken))
      throw new Exception("Failed to download ZIP for differential update");
  // ...
  throw new Exception("Failed to extract ZIP for differential update");
  throw new Exception($"Failed to download {fileShortName}");
  ```

- **Issue:** `throw new Exception` throws the base exception type with no inner exception, making error classification and upstream handling harder.
- **Proposed fix:** Replace with specific types (`InvalidOperationException`, `HttpRequestException`, or a custom `UpdateException`) and include the original exception where applicable.
- **Verification:** `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj`
- **Action:** queue-patch

## ISS-011: Empty catch block in `GamingModeService`

- **Scanner:** S1 Codebase Health
- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/GamingModeService.cs:105-110`
- **Evidence:**

  ```csharp
  try
  {
      using var proc = System.Diagnostics.Process.GetProcessById((int)processId);
      processName = proc.ProcessName;
  }
  catch { }
  ```

- **Issue:** The inner `catch` swallows all exceptions silently, including unexpected ones.
- **Proposed fix:** Catch `ArgumentException` (process no longer exists) and log at Debug level; let other exceptions propagate or be handled by the outer catch.
- **Verification:** `dotnet build ...` and runtime test
- **Action:** queue-patch

## ISS-012: `throw new Exception` in audit hash chain verification

- **Scanner:** S1 Codebase Health
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.Core/Security/SecurityAuditService.cs:73`
- **Evidence:**

  ```csharp
  if (entry == null) throw new Exception("Invalid JSON formatting");
  ```

- **Issue:** Throws the base `Exception` type instead of a domain-specific or `InvalidDataException`, making exception filtering by callers harder.
- **Proposed fix:** Replace with `InvalidDataException` or a custom `AuditIntegrityException`.
- **Verification:** `dotnet build src/Redball.Core/Redball.Core.csproj`
- **Action:** queue-patch

## ISS-013: Non-atomic analytics data flush risks file corruption

- **Scanner:** S1 Codebase Health
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/AnalyticsService.cs:772`
- **Evidence:**

  ```csharp
  var json = JsonSerializer.Serialize(_data, options);
  File.WriteAllText(AnalyticsFile, json);
  ```

- **Issue:** Writing directly to the analytics file can corrupt it if the process is terminated mid-write. `ConfigService` already uses an atomic temp-file + `File.Move` pattern.
- **Proposed fix:** Adopt the same atomic write pattern used by `ConfigService` (write to `*.tmp`, then `File.Move(..., overwrite: true)`).
- **Verification:** `dotnet build ...` and unit test
- **Action:** queue-patch

## ISS-014: Non-atomic session stats save

- **Scanner:** S1 Codebase Health
- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/SessionStatsService.cs:179`
- **Evidence:**

  ```csharp
  var json = JsonSerializer.Serialize(_data, SerializerOptions);
  File.WriteAllText(_statsFile, json);
  ```

- **Issue:** Same as ISS-013 — non-atomic write risks corruption on crash.
- **Proposed fix:** Use temp-file + `File.Move(..., overwrite: true)`.
- **Verification:** `dotnet build ...` and unit test
- **Action:** queue-patch

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
- **Location:** `/root/Redball/web-admin/package.json:8-13`
- **Evidence:**

  ```json
  "dependencies": {
    "child_process": "^1.0.2",
    "fs": "^0.0.1-security",
    "path": "^0.12.7"
  }
  ```

- **Issue:** `fs`, `path`, and `child_process` are Node.js built-ins. The npm packages with the same names are placeholders and introduce supply-chain risk.
- **Proposed fix:** Remove these three entries from `dependencies` and regenerate `package-lock.json`.
- **Verification:** `npm install` followed by `node -c server.js` and `npm test`
- **Action:** queue-patch

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
  !define PRODUCT_VERSION "2.1.443.0"
  !define PRODUCT_VERSION_SHORT "2.1.443"
  ```

- **Issue:** The installer still advertises version `2.1.443` while `Directory.Build.props` and the project are at `2.1.720`. This causes installer/published artifact mismatch if the build script does not override these defines.
- **Proposed fix:** Update the defines to `2.1.720.0` and `2.1.720`, or ensure the build script reliably replaces them.
- **Verification:** `grep PRODUCT_VERSION installer/Redball.nsi` matches `Directory.Build.props`
- **Action:** queue-patch
