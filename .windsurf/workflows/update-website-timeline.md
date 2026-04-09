---
description: Update website timeline from git history
---

# Update Website Timeline from Git History

This workflow detects major milestones from git commits and updates the public website timeline.

## When to run

Run this workflow after:

- Major feature implementations
- Architecture changes (new services, drivers, UI frameworks)
- Distribution method additions (new package managers)
- Significant refactoring or rewrites

## Steps

1. **Analyze git history for major commits**

   ```bash
   bash scripts/update-timeline-from-git.sh
   ```

2. **Review detected milestones**
   - Look for commits with keywords: `feat:`, `feature:`, `add:`, `implement:`, `WPF`, `KMDF`, `driver`, `delta`, `analytics`, `winget`, etc.
   - Exclude: version bumps, build releases, chore commits

3. **Update the memory database**
   Add significant milestones to the knowledge graph:

   ```text
   Entity: Redball_Major_Milestones
   Observation: "[Month Year]: [Brief description] - [Commit hash]"
   ```

4. **Update the website timeline**
   Edit `update-server/public/index.html`:
   - Locate the `.timeline` section
   - Add/modify `.timeline-item` entries with real dates from git
   - Format: `<div class="timeline-year">Mon YYYY — Label</div>`

5. **Verify the changes**
   // turbo

   ```bash
   # Check the timeline section renders correctly
   head -n 50 update-server/public/index.html | grep -A 20 "timeline-year"
   ```

## Key milestones to track

| Category      | Keywords                                      |
| ------------- | --------------------------------------------- |
| Driver/HID    | KMDF, driver, interception, HID               |
| UI/Themes     | WPF, theme, MVVM, ModernUI, Hacker            |
| Core Features | TypeThing, clipboard, typing, SendInput       |
| Services      | Service mode, analytics, dashboard, telemetry |
| Updates       | Delta, patch, update-server                   |
| Distribution  | Winget, Scoop, Chocolatey, NSIS, installer    |

## Example timeline entry

```html
<div class="timeline-item">
    <div class="timeline-year">Apr 2026 — Package Managers</div>
    <div class="timeline-title">Automated Distribution</div>
    <div class="timeline-desc">Winget, Scoop, and Chocolatey publishing with automated CI/CD releases.</div>
</div>
```

## Related files

- `scripts/update-timeline-from-git.sh` — Detection script
- `update-server/public/index.html` — Website timeline (lines ~2400)
- Memory entity: `Redball_Major_Milestones`
