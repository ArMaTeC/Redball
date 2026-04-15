# Redball — Master Implementation Roadmap
**Generated:** 2026-04-15  
**Analysis Scope:** Full recursive static analysis — all sub-projects, modules, utility libraries  
**Analyst Role:** Senior Full-Stack Architect, Security Auditor, QA Lead  
**Phases Completed:** Structural Integrity · Security · UI/UX · Performance · Code Quality

---

## Analysis Summary

The codebase is architecturally sophisticated and largely well-structured. The C# WPF application has extensive service coverage, proper IDisposable patterns, and thorough logging. The Node.js servers (update-server, web-admin) are security-conscious. However, a number of concrete issues were identified across all five dimensions.

**Key cross-cutting themes found:**
1. **Unauthenticated write endpoints** on the update-server expose the release database to unauthenticated mutation.
2. **Duplicated functions** (`compareVersions`, JWT secret loader, download counter, auth, build state) exist identically across both Node.js servers.
3. **Swallowed errors** (`catch (e) { }`) in the update-server suppress persistence failures silently.
4. **Dead/orphaned UI logic** in the WPF settings layer (`UpdateAutoUpdateIntervalText`, `MainAutoUpdateIntervalText`).
5. **Hardcoded default password hash** is present verbatim in two server files.
6. **Admin WebSocket** in `web-admin/server.js` performs no token authentication on connection, unlike the update-server.
7. A **disabled feature module** (`WindowsHello.cs.disabled`) is left in source with no roadmap comment.
8. **Magic numbers** for Windows API constants are partially duplicated between `NativeMethods.cs` and `MainWindow.TypeThing.cs`.
9. **Deployment-history chart** in `AdminDashboard.tsx` is hard-coded static data, not wired to the API.
10. **`/api/config` POST** on update-server has no authentication, allowing unauthenticated config mutation.

---

## Roadmap — Prioritised Task List

### MODULE: update-server

| # | Task Name | File / Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|-----------------|-------------------|--------------|----------|----------|
| 1 | Protect release mutation endpoints | `update-server/server.js:587,619,668,686,698` | `POST /api/releases`, `POST /api/releases/:version/upload`, `DELETE /api/releases/:version`, `PATCH /api/releases/:version`, and `POST /api/publish` all lack `authenticateToken` middleware — any anonymous caller can create, upload, delete, or modify releases | Add `authenticateToken` as second argument to each of these `app.post/delete/patch` calls | Security | **[CRITICAL]** |
| 2 | Protect `/api/config` POST endpoint | `update-server/server.js:956` | `POST /api/config` has no auth guard — unauthenticated clients can overwrite the default client configuration pushed to all Redball installations | Add `authenticateToken` middleware to this route | Security | **[CRITICAL]** |
| 3 | Protect `/api/admin/generate-patches` | `update-server/server.js:1035` | `POST /api/admin/generate-patches` has no `authenticateToken` guard despite being an admin operation that runs background computation | Add `authenticateToken` middleware | Security | **[HIGH]** |
| 4 | Add brute-force protection to login | `update-server/server.js:1315` | `/api/auth/login` is not covered by `apiLimiter` (the limiter is applied to `/api/` prefix, but login should have a stricter dedicated limiter) and no account lockout exists | Apply a dedicated `authLimiter` (e.g. 5 attempts per 15 min per IP) to the login route, separate from the general `apiLimiter` | Security | **[HIGH]** |
| 5 | Remove duplicated `compareVersions` function | `update-server/server.js:161` and `:1137` | `compareVersions` is defined twice in the same file (lines 161 and 1137) with slightly different semantics — the second definition at line 1137 silently shadows the first | Delete the duplicate at line 1137 and verify all callers use the same single definition | Logic | **[HIGH]** |
| 6 | Remove duplicated `DEFAULT_CLIENT_CONFIG` | `update-server/server.js:880`, `web-admin/server.js:404` | Identical `DEFAULT_CLIENT_CONFIG` object is copy-pasted into both server files; divergence risk over time | Extract to a shared `config/defaults.js` module imported by both servers | Cleanup | **[MEDIUM]** |
| 7 | Eliminate silently swallowed `catch` blocks | `update-server/server.js:193,255,285,292,299,306` | Six `catch (e) { }` blocks silently discard: JWT secret load failure, directory size calculation errors, build state load/save errors, and download count load/save errors — these are persistence-critical | Add at minimum `console.error('[TAG] Failed:', e.message)` inside each empty catch; use structured logging where possible | Logic | **[HIGH]** |
| 8 | Validate `username`/`password` inputs on login | `update-server/server.js:1316`, `web-admin/server.js:193` | Neither login handler validates that the provided `username` and `password` fields are non-empty strings before calling `bcrypt.compare`, risking a `TypeError` if the body is malformed | Add `if (!username || !password) return res.status(400).json({ error: '...' })` guard (already done in web-admin but missing in update-server) | Security | **[MEDIUM]** |
| 9 | `Logger.Warning` called as if it's a global | `update-server/server.js:1015,1025` | Lines 1015 and 1025 call `Logger.Warning(...)` — `Logger` is a C# class, not a Node.js global. These are dead calls that will throw `ReferenceError: Logger is not defined` at runtime | Replace with `console.warn(...)` | Logic | **[CRITICAL]** |
| 10 | Patch download route lacks version sanitisation | `update-server/server.js:1011` | `GET /downloads/:version/patches/:filename` sanitises the filename but does not sanitise `req.params.version` before constructing `allowedDir`. A crafted version string containing `..` could escape the releases directory | Apply the same `version.replace(/[^a-zA-Z0-9._-]/g, '')` sanitisation used in the multer `destination` callback to the `version` param here | Security | **[HIGH]** |

---

### MODULE: web-admin

| # | Task Name | File / Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|-----------------|-------------------|--------------|----------|----------|
| 11 | Authenticate WebSocket connections | `web-admin/server.js:514` | The WebSocket server in `web-admin` does not validate a JWT token on connection — any client can connect and send `start-build` or `stop-build` messages, triggering arbitrary process execution | Mirror the update-server pattern: extract token from `req.url` query params, call `jwt.verify`, and `ws.close(1008)` on failure | Security | **[CRITICAL]** |
| 12 | Hardcoded default password hash | `web-admin/server.js:90`, `update-server/server.js:207` | The bcrypt hash of the default password `redball2026` is embedded verbatim in both server files. A developer accidentally committing this to a public repo leaks the default credential | Replace the hardcoded hash with a runtime-generated random password on first boot, write it to `logs/first-run-credentials.txt` (mode 0600), and force a password change on first login | Security | **[HIGH]** |
| 13 | Silence empty `catch` block for JWT secret | `web-admin/server.js:73` | `catch (e) { }` on JWT secret file read silently swallows I/O errors | Add `console.error('[AUTH] Failed to read JWT secret file:', e.message)` | Logic | **[MEDIUM]** |
| 14 | `/api/stats` endpoint is unauthenticated | `web-admin/server.js:253` | The stats endpoint (total downloads, release list) is public. The update-server intentionally exposes `/api/stats` publicly, but `web-admin` may not need to. Confirm intent; if admin-only, add `authenticateToken` | Audit intended exposure level; add auth guard if admin-only | Security | **[MEDIUM]** |
| 15 | `getDirSize` is defined locally in `web-admin` | `web-admin/server.js:329-343` | The recursive `getDirSize` function is duplicated between both server files with slight differences | Consolidate into a shared utility module | Cleanup | **[MEDIUM]** |

---

### MODULE: WPF Application — Services

| # | Task Name | File / Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|-----------------|-------------------|--------------|----------|----------|
| 16 | Dead `UpdateAutoUpdateIntervalText` method body | `src/Redball.UI.WPF/Views/MainWindow.Settings.cs:521-531` | Entire method body is commented out with `// TODO: Re-add MainAutoUpdateIntervalText control`. The method is still called but produces no effect, meaning the UI never updates the interval display text | Either re-add the `MainAutoUpdateIntervalText` control to the XAML and un-comment the logic, or remove the dead method and all call sites | Logic | **[HIGH]** |
| 17 | `WindowsHello.cs.disabled` — orphaned feature file | `src/Redball.UI.WPF/Services/WindowsHelloService.cs.disabled` | A 9.7 KB Windows Hello authentication service is left disabled with no roadmap comment, no `#if` guard, and no issue reference. Build tooling may silently skip it but its state is ambiguous | Either re-enable with a feature flag, or add a `// Scheduled for removal: <date>` header and track in the roadmap | Logic | **[MEDIUM]** |
| 18 | `CrashDumpService` magic dump type constant | `src/Redball.UI.WPF/Services/CrashDumpService.cs:38` | `uint dumpType = 0x00001040` is a bitwise OR of two `MINIDUMP_TYPE` enum values with only a comment. No named constant | Define a named `const uint MiniDumpType = MiniDumpWithIndirectlyReferencedMemory \| MiniDumpWithThreadInfo` using actual enum values | Cleanup | **[POLISH]** |
| 19 | Magic numbers duplicated in `NativeMethods` and `MainWindow.TypeThing` | `src/Redball.UI.WPF/Interop/NativeMethods.cs:60-63`, `src/Redball.UI.WPF/Views/MainWindow.TypeThing.cs:868-874` | `WM_KEYDOWN`, `WM_KEYUP`, `WM_CHAR`, `KEYEVENTF_KEYUP`, `KEYEVENTF_UNICODE`, `KEYEVENTF_SCANCODE`, `INPUT_KEYBOARD` are defined in both files | Remove the duplicates from `MainWindow.TypeThing.cs` and reference the values from `NativeMethods.cs` | Cleanup | **[MEDIUM]** |
| 20 | Singleton services with no thread-safe lazy init | `src/Redball.UI.WPF/Services/SingletonService.cs` (and ~46 other service files with `static.*instance` pattern) | Multiple services use a simple `static Instance { get; } = new()` pattern. While C# static field initialisation is thread-safe, services with complex constructor dependencies risk initialisation-order issues if `ServiceLocator` ordering ever changes | Audit `ServiceLocator.cs` init order; add `[Conditional]` assertions or startup-time dependency graph validation | Logic | **[MEDIUM]** |
| 21 | `TemperatureMonitorService` has no null guard on WMI `CurrentTemperature` | `src/Redball.UI.WPF/Services/TemperatureMonitorService.cs:279` | `Convert.ToDouble(obj["CurrentTemperature"])` is called without a null check — if WMI returns null the call throws `InvalidCastException` | Add `if (obj["CurrentTemperature"] == null) continue;` before the Convert call | Logic | **[HIGH]** |

---

### MODULE: WPF Application — Views / XAML

| # | Task Name | File / Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|-----------------|-------------------|--------------|----------|----------|
| 22 | `VirtualizedSettingsPanel` value cast — no null safety for non-double types | `src/Redball.UI.WPF/Controls/VirtualizedSettingsPanel.cs:448` | `Convert.ToDouble(item.Value ?? 0)` will throw `InvalidCastException` if `item.Value` is a string (e.g. `"50"`) or an unsupported type at runtime — no format-exception handling | Wrap in a `try { ... } catch (FormatException) { Value = item.Min ?? 0; }` or use `double.TryParse` | Logic | **[HIGH]** |
| 23 | `MainWindow.xaml.cs` empty `InvalidOperationException` catch on `DragMove` | `src/Redball.UI.WPF/Views/MainWindow.xaml.cs:256` | `catch (InvalidOperationException) { }` silently swallows exceptions from `DragMove()`. While this is a common WPF pattern, it hides unexpected exceptions with the same type | Add `if (!ex.Message.Contains("DragMove"))` re-throw guard to preserve unexpected exceptions | Logic | **[MEDIUM]** |
| 24 | `AutomationProperties.AutomationId` mismatches in XAML | `src/Redball.UI.WPF/Views/MainWindow.xaml:455,464` | `MainHeartbeatInputModeCombo` has `AutomationId="UseHeartbeatCheckBox"` (wrong — it's a ComboBox), and `MainDurationSlider` has `AutomationId="HeartbeatIntervalTextBox"` (wrong — it's a Slider). These break UI automation tests | Correct `AutomationId` values to match the actual control names/types | UI/UX | **[HIGH]** |
| 25 | Missing `aria-label` on form inputs in `AdminDashboard.tsx` | `site/src/AdminDashboard.tsx:460,467` | Login form `<input type="text">` (username) and `<input type="password">` only have `placeholder` attributes, which do not satisfy WCAG 2.1 SC 1.3.1 (labels for form controls) | Add `<label htmlFor="...">` elements or `aria-label` attributes | UI/UX (a11y) | **[HIGH]** |
| 26 | Search bar in `AdminDashboard.tsx` is orphaned UI | `site/src/AdminDashboard.tsx:192-203` | The header search bar `<input type="text" placeholder="Search resources...">` is rendered but has no `onChange` handler and no connected state — it does nothing | Either connect to a filtering function for the active tab's data, or remove it until the feature is implemented | UI/UX | **[MEDIUM]** |
| 27 | Deployment history chart uses hard-coded static data | `site/src/AdminDashboard.tsx:232-234` | The "Deployment History" chart renders a static array `[40, 60, 45, 90, 65, 80, 50, 70]`. It is not wired to any API response | Fetch actual deployment event counts from `/api/stats` (releases array with dates) and render real data; add a `/* Chart Placeholder */` comment removal as acceptance criterion | UI/UX | **[MEDIUM]** |
| 28 | Hard-coded "Update Server: v3.1.2" version string | `site/src/AdminDashboard.tsx:182` | The sidebar footer displays `Update Server: v3.1.2` as a literal string — this will be out of date on every release | Fetch the version from `/api/health` or `/api/stats` and render dynamically | UI/UX | **[MEDIUM]** |
| 29 | No loading skeleton / error state in `AdminDashboard.tsx` overview tab | `site/src/AdminDashboard.tsx:120-132` | `fetchStats` sets `statsLoading` to `false` in `finally`, but there is no rendered empty/error state when the fetch fails — the dashboard silently shows nothing | Add error state variable; render a visible error banner when fetch fails | UI/UX | **[MEDIUM]** |

---

### MODULE: Site / Landing Page

| # | Task Name | File / Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|-----------------|-------------------|--------------|----------|----------|
| 30 | Missing `alt` text on hero image | `site/src/LandingPage.tsx:376` | `<img src="/hero-sphere.png" alt="Redball UI">` — the alt text is present but purely decorative; the image contains UI screenshots that should be described meaningfully for screen readers | Update `alt` to a descriptive string, e.g. `"Screenshot of the Redball desktop application showing the keep-awake control panel"` | UI/UX (a11y) | **[MEDIUM]** |

---

### MODULE: Build / Infrastructure

| # | Task Name | File / Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|-----------------|-------------------|--------------|----------|----------|
| 31 | `server-old.js` left in `web-admin` | `web-admin/server-old.js` | An old 7.4 KB server file is committed alongside the active `server.js`. It is not referenced anywhere but adds confusion and surface area | Delete `server-old.js` from the repository | Cleanup | **[POLISH]** |
| 32 | Stale `wpftmp.csproj` files in source tree | `src/Redball.UI.WPF/Redball.UI.WPF_*_wpftmp.csproj` (5 files) | Five temporary `.csproj` files generated during WPF cross-compilation are committed to source control. They contain 33–49 KB of generated content that will drift from the real project file | Add `*_wpftmp.csproj` to `.gitignore` and delete committed copies | Cleanup | **[MEDIUM]** |
| 33 | `build.pid` file committed to repository root | `/root/Redball/build.pid` | A process-ID file from a previous build is committed to the repo root. This is ephemeral build state | Add `build.pid` to `.gitignore` and delete the file from the index | Cleanup | **[POLISH]** |
| 34 | Duplicate `DEFAULT_CLIENT_CONFIG` across two servers | `web-admin/server.js:404`, `update-server/server.js:880` | The default client configuration object (20+ keys) is copy-pasted verbatim in both server files — any change must be manually mirrored | Extract to `config/client-defaults.js` and `require()` from both servers | Cleanup | **[MEDIUM]** |
| 35 | Duplicate auth+JWT logic across both servers | `web-admin/server.js:67-130`, `update-server/server.js:187-239` | `loadOrCreateJwtSecret`, `getAdminUser`, `saveAdminUser`, `authenticateToken` are near-identical in both server files | Extract to a shared `lib/auth.js` module | Cleanup | **[MEDIUM]** |

---

### MODULE: Performance

| # | Task Name | File / Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|-----------------|-------------------|--------------|----------|----------|
| 36 | `getDirSize` is synchronous recursive I/O on request thread | `web-admin/server.js:329`, `update-server/server.js:241` | `getDirSize` performs deep-recursive synchronous `fs.readdirSync` + `fs.statSync` calls inside an HTTP request handler (`/api/system/config`). On large release directories this blocks the Node.js event loop | Run `getDirSize` on startup / on a timer, cache the result, and serve the cached value from the endpoint | Performance | **[HIGH]** |
| 37 | `getDownloadStats` reloads the full releases directory on every `/api/stats` call | `web-admin/server.js:541-631` | `getDownloadStats` calls `loadDownloadCounts()`, `fs.readdirSync`, `fs.statSync` per-file, and re-reads `releases.json` on every request. Under moderate traffic this is a significant I/O bottleneck | Cache the result with a TTL (e.g. 30 seconds) and invalidate on upload/delete events | Performance | **[HIGH]** |
| 38 | `TemperatureMonitorService` retries 7+ WMI query classes sequentially | `src/Redball.UI.WPF/Services/TemperatureMonitorService.cs:28-33` | The service tries up to 7 different WMI queries in sequence each poll cycle. All `ManagementObjectSearcher.Get()` calls are synchronous and block the calling thread | Cache the first successful WMI class name after startup; use it exclusively on subsequent polls rather than probing all classes every time | Performance | **[MEDIUM]** |
| 39 | `AdminDashboard.tsx` WebSocket does not reconnect on drop | `site/src/AdminDashboard.tsx:72-111` | The WebSocket is created once in a `useEffect` with no reconnect logic. If the server restarts during a build, the UI silently loses real-time output | Implement exponential-backoff reconnection with a max-retry cap inside `ws.onclose` | Performance | **[MEDIUM]** |

---

### MODULE: Code Quality / Uniformity

| # | Task Name | File / Location | Issue Description | Proposed Fix | Category | Severity |
|---|-----------|-----------------|-------------------|--------------|----------|----------|
| 40 | `HotkeyConflictDetectionService` mixed naming — `duplicate` vs `DRY` references | `src/Redball.UI.WPF/Services/HotkeyConflictDetectionService.cs` | Internal variable naming inconsistency (3 instances flagged by static scan) — mix of `duplicate`/`conflict`/`clash` for the same concept | Standardise on `conflict` throughout the file to match the class name | Cleanup | **[POLISH]** |
| 41 | `Logger.Warning` used in Node.js (C# class name bleed) | `update-server/server.js:1015,1025` | Two calls use `Logger.Warning(...)` — a C# pattern — instead of `console.warn(...)`. This is a ReferenceError at runtime on those code paths | Replace with `console.warn('[UpdateServer]', ...)` | Logic | **[CRITICAL]** (duplicate of #9) |
| 42 | `VirtualizedSettingsPanel` has an unnamed magic number for slider width | `src/Redball.UI.WPF/Controls/VirtualizedSettingsPanel.cs:451` | `Width = 150` is a hard-coded pixel value with no named constant and no comment | Define `private const double SliderWidth = 150;` | Cleanup | **[POLISH]** |
| 43 | Inconsistent error-handling verbosity in `updateServer` — some `catch` blocks log, others don't | `update-server/server.js:152,193,255,285,292,299,306` | Six of the nine `catch` blocks in the server are silent (`catch (e) { }`), while others log via `console.error`. The silent ones hide persistence failures | Apply a consistent policy: all `catch` blocks must log at minimum the error message to `console.error` | Cleanup | **[MEDIUM]** |

---

## Severity Legend

| Badge | Meaning |
|-------|---------|
| **[CRITICAL]** | Application crashes, security breach, or data loss risk |
| **[HIGH]** | Feature broken or missing; significant security gap |
| **[MEDIUM]** | Degraded performance, UX gap, or moderate code risk |
| **[POLISH]** | Code style, minor UI, maintainability |

---

## Recommended Fix Order

1. **Items 1, 2, 9/41, 11** — Unauthenticated mutation endpoints + `Logger.Warning` ReferenceError + unauthenticated WebSocket. These are live security and runtime crash risks.
2. **Items 3, 4, 10, 12** — Remaining auth gaps + brute force + version sanitisation + hardcoded credentials.
3. **Items 5, 7, 8, 13** — Logic correctness: duplicated function, swallowed errors, missing login validation.
4. **Items 16, 21, 22, 23, 24** — WPF dead code, null dereferences, XAML automation mismatches.
5. **Items 25–30** — UI/UX and accessibility fixes.
6. **Items 36–39** — Performance: sync I/O on request thread, no WS reconnect.
7. **Items 31–35, 40–43** — Cleanup and code quality.
