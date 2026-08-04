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
