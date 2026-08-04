# Queued Patches

## ISS-002: Hardcoded default admin credentials in update-server

- **Severity:** critical
- **Risk:** high
- **Location:** `/root/Redball/update-server/lib/auth.js:48-54`
- **Why queued:** Removes a shipped hardcoded password, which is a breaking change to first-run behavior.
- **Patch file:** `audit-run/patches/ISS-002.patch`
- **Verification:** `npm test` + manual login test
- **Approval needed:** yes

## ISS-003: Hardcoded default admin credentials in web-admin

- **Severity:** critical
- **Risk:** high
- **Location:** `/root/Redball/web-admin/server.js:117-121`
- **Why queued:** Changes default authentication setup and first-run flow.
- **Patch file:** `audit-run/patches/ISS-003.patch`
- **Verification:** `npm test` + manual login test
- **Approval needed:** yes

## ISS-004: No rate limiting on web-admin login endpoint

- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/web-admin/server.js:221-250`
- **Why queued:** Adds a new npm dependency (`express-rate-limit`) and runtime behavior.
- **Patch file:** `audit-run/patches/ISS-004.patch`
- **Verification:** `npm test` + manual brute-force simulation
- **Approval needed:** yes

## ISS-005: Error messages leaked to clients in web-admin

- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/web-admin/server.js:317` (and related)
- **Why queued:** Changes API error response shape and could break clients relying on the messages.
- **Patch file:** `audit-run/patches/ISS-005.patch`
- **Verification:** `npm test` + curl tests
- **Approval needed:** yes

## ISS-006: update-server exposes build endpoint via pty.spawn

- **Severity:** critical
- **Risk:** high
- **Location:** `/root/Redball/update-server/server.js:1439-1529`
- **Why queued:** Requires architectural hardening; changing PTY/process behavior could break the build pipeline.
- **Patch file:** `audit-run/patches/ISS-006.patch`
- **Verification:** Manual build-flow test on a staging server
- **Approval needed:** yes

## ISS-007: Unsanitized `version` path parameter used in filesystem operations

- **Severity:** high
- **Risk:** medium
- **Location:** `/root/Redball/update-server/server.js:996-1019` and `696-699`
- **Why queued:** Touches filesystem deletion and directory listing; must be tested carefully.
- **Patch file:** `audit-run/patches/ISS-007.patch`
- **Verification:** `npm test` + targeted path-traversal curl tests
- **Approval needed:** yes

## ISS-008: update-server serves downloads without security headers

- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/update-server/server.js:1573-1575`
- **Why queued:** Adds new dependency (`helmet`) and may alter response headers for downloads/SPA.
- **Patch file:** `audit-run/patches/ISS-008.patch`
- **Verification:** `npm test` + header inspection
- **Approval needed:** yes

## ISS-009: Browser extension content script matches all URLs

- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/browser-extension/manifest.json:18-23`
- **Why queued:** Changes extension permissions and may break intended page-integration behavior.
- **Patch file:** `audit-run/patches/ISS-009.patch`
- **Verification:** Load extension in a browser and confirm scope
- **Approval needed:** yes

## ISS-010: Generic `throw new Exception` in update installer

- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/UpdateService.cs:849-1027`
- **Why queued:** Changes exception contract of the updater; other catch sites may need updates.
- **Patch file:** `audit-run/patches/ISS-010.patch`
- **Verification:** `dotnet build src/Redball.UI.WPF/Redball.UI.WPF.csproj`
- **Approval needed:** yes

## ISS-011: Empty catch block in `GamingModeService`

- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/GamingModeService.cs:105-110`
- **Why queued:** Changes exception handling; need to confirm allowed exception types.
- **Patch file:** `audit-run/patches/ISS-011.patch`
- **Verification:** `dotnet build ...` and runtime test
- **Approval needed:** yes

## ISS-012: `throw new Exception` in audit hash chain verification

- **Severity:** low
- **Risk:** low
- **Location:** `/root/Redball/src/Redball.Core/Security/SecurityAuditService.cs:73`
- **Why queued:** Changing exception type could affect callers/tests.
- **Patch file:** `audit-run/patches/ISS-012.patch`
- **Verification:** `dotnet build src/Redball.Core/Redball.Core.csproj`
- **Approval needed:** yes

## ISS-013: Non-atomic analytics data flush risks file corruption

- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/AnalyticsService.cs:772`
- **Why queued:** File I/O pattern change; needs build and unit tests.
- **Patch file:** `audit-run/patches/ISS-013.patch`
- **Verification:** `dotnet build ...` and unit test
- **Approval needed:** yes

## ISS-014: Non-atomic session stats save

- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/src/Redball.UI.WPF/Services/SessionStatsService.cs:179`
- **Why queued:** Same as ISS-013.
- **Patch file:** `audit-run/patches/ISS-014.patch`
- **Verification:** `dotnet build ...` and unit test
- **Approval needed:** yes

## ISS-018: web-admin package.json declares fake built-in dependencies

- **Severity:** medium
- **Risk:** medium
- **Location:** `/root/Redball/web-admin/package.json:14-23`
- **Why queued:** Removes dependencies and requires `package-lock.json` regeneration.
- **Patch file:** `audit-run/patches/ISS-018.patch`
- **Verification:** `npm install` + `node -c server.js` + `npm test`
- **Approval needed:** yes

## ISS-020: NSIS installer version does not match build props

- **Severity:** low
- **Risk:** medium
- **Location:** `/root/Redball/installer/Redball.nsi:25-26`
- **Why queued:** Changes installer product version; must match the actual release being built.
- **Patch file:** `audit-run/patches/ISS-020.patch`
- **Verification:** `grep PRODUCT_VERSION installer/Redball.nsi` and `grep Version Directory.Build.props`
- **Approval needed:** yes
