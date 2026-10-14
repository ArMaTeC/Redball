# Redball Companion (browser extension)

**Status: EXPERIMENTAL / non-functional**

This extension expects the desktop app to expose a control API on
`http://localhost:5000` (`background.js` → `REDBALL_API_URL`). No such service
exists in the desktop app today — every status check fails and the popup shows
the disconnected state.

Two paths forward (see `audit-run/opportunities.md` → OPP-001):

1. Implement the opt-in `LocalApiService` in the WPF app, or
2. Remove this extension from distribution until that API ships.

The extension code itself (popup UI, background polling, content script) is
otherwise complete and will work unchanged once the API exists.
