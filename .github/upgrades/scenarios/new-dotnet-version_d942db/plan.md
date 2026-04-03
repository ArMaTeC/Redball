# .NET 10.0 Upgrade Plan

Table of contents
- 1 Executive Summary
- 2 Migration Strategy
- 3 Dependency Analysis
- 4 Project-by-Project Plans
  - 4.1 `src\Redball.UI.WPF\Redball.UI.WPF.csproj`
  - 4.2 `src\Redball.Core\Redball.Core.csproj`
  - 4.3 `Redball.Tests` (inferred)
- 5 Package Update Reference
- 6 Breaking Changes Catalog
- 7 Testing & Validation Strategy
- 8 Risk Management & Mitigation
- 9 Source Control Strategy
- 10 Success Criteria
- 11 Appendices & Next Steps

---

## 1 Executive Summary

Selected Strategy
**All-At-Once Strategy** — All projects upgraded simultaneously in a single coordinated operation.

Rationale
- Solution size: small (3 projects discovered by assessment).
- Current targets: `net8.0` / `net8.0-windows`.
- Assessment identified 10 NuGet packages requiring updates and no critical incompatibilities that mandate staged migration.
- Team can tolerate a single atomic upgrade window.

Scope
- Projects affected (discovered in assessment):
  - `src\Redball.UI.WPF\Redball.UI.WPF.csproj` (WPF app) — current `TargetFramework`: `net8.0-windows`
  - `src\Redball.Core\Redball.Core.csproj` (class library) — current `TargetFramework`: `net8.0` (inferred)
  - `Redball.Tests` (test project, referenced by InternalsVisibleTo) — current `TargetFramework`: `net8.0` (inferred)

Target state
- All projects target `.NET 10.0` monikers:
  - For WPF app: `net10.0-windows`
  - For libraries and tests: `net10.0`
- All NuGet packages listed in the assessment updated to versions compatible with .NET 10.0

Critical findings from assessment (summary)
- 10 packages across the solution require version updates (listed in §5).
- No blocking circular dependencies were reported in assessment.
- WPF-specific items may need review (Program/Startup, ApplicationDefinition, ResourceDictionary behavior).

---

## 2 Migration Strategy

Approach
- Apply the **All-At-Once** approach: update all project TargetFrameworks and package versions in a single atomic change.
- Perform a single coordinated restore/build/fix pass over the whole solution.

Why All-At-Once
- Small solution (3 projects): atomic upgrade lowers overall complexity and avoids multi-targeting.
- Simpler dependency resolution and single verification pass.

Phases (informational grouping — not sequential project-by-project work)
- Phase 0: Prerequisites & environment checks
- Phase 1: Atomic Upgrade (project files + packages)
- Phase 2: Build & Compilation Fixes (resolve breaking changes)
- Phase 3: Test Execution & Fixes
- Phase 4: Final Verification and Merge

Key principles to follow during execution
- Update all TargetFramework properties and MSBuild imports at once (including Directory.Build.props/targets if present).
- Update all PackageReferences to assessment-suggested versions (see §5).
- Restore dependencies and build the entire solution; fix compilation errors discovered.
- Run tests; fix failures.
- Use single-commit approach for the upgrade branch (see §9).

---

## 3 Dependency Analysis

Summary
- Assessment discovered a shallow dependency graph suitable for an All-At-Once upgrade.
- Leaf projects (no project references): likely `Redball.Core` is a dependency of `Redball.UI.WPF`; tests depend on both.

Project dependency relationships (textual)
- `Redball.Core` → library, depended on by `Redball.UI.WPF` and `Redball.Tests`.
- `Redball.UI.WPF` → WPF application, depends on `Redball.Core`.
- `Redball.Tests` → test project, depends on `Redball.Core` (and possibly `Redball.UI.WPF` test assets).

Critical path
- `Redball.Core` must compile under .NET 10.0 before application builds can succeed. In All-At-Once this is handled within the single pass but keep attention on library API compatibility.

Circular dependencies
- None reported by assessment. If any are discovered during execution, treat the entire cycle as a single unit and resolve simultaneously.

---

## 4 Project-by-Project Plans

Notes: The steps below describe what must be done for each project during the atomic upgrade. These are instructions for the executor; do not execute them now.

### 4.1 `src\Redball.UI.WPF\Redball.UI.WPF.csproj`

Current state
- `TargetFramework`: `net8.0-windows`
- Notable PackageReferences (current versions from project file):
  - `CommunityToolkit.Mvvm` `8.2.2`
  - `Hardcodet.NotifyIcon.Wpf` `1.1.0`
  - `LottieSharp` `2.4.3`
  - `Microsoft.Extensions.DependencyInjection` `8.0.1`
  - `Microsoft.Xaml.Behaviors.Wpf` `1.1.135`
  - `System.Management` `8.0.0`
  - `System.Speech` `8.0.0`
  - `InputInterceptor` `2.2.1`
  - `System.ServiceProcess.ServiceController` `8.0.1`
  - `System.Text.Json` `8.0.5`
- ProjectReference: `..\Redball.Core\Redball.Core.csproj`

Target state
- `TargetFramework`: `net10.0-windows`
- All listed packages updated to .NET 10-compatible versions (see §5).

Migration steps (to be performed in the atomic upgrade task)
1. Update `TargetFramework` to `net10.0-windows` in the project file.
2. Update PackageReference versions to the assessment-specified target versions (see §5).
3. Check `EnableDefaultApplicationDefinition` / `ApplicationDefinition` usage: ensure `App.xaml` is still recognized under .NET 10 and that `StartupObject` approach is compatible.
4. Verify any P/Invoke or platform-specific API (SendInput replacement for SendKeys) remains valid on .NET 10.
5. Restore and build solution; address compilation errors related to framework or package API changes.
6. Run UI-related automated tests (if any) and validate behavior.

Expected breaking-change areas
- WPF startup/hosting model changes (if any are introduced in .NET 10)
- API changes in `System.Management`, `System.Speech`, and `System.ServiceProcess` packages
- Changes in CommunityToolkit.Mvvm major/minor versions (verify ViewModel attributes and generated code)

Validation checklist
- [ ] `net10.0-windows` set in `Redball.UI.WPF.csproj`
- [ ] All package versions updated as per §5
- [ ] Project builds without errors
- [ ] UI smoke checks (automated tests) succeed

### 4.2 `src\Redball.Core\Redball.Core.csproj`

Current state (inferred)
- `TargetFramework`: `net8.0`
- Library used by application and tests

Target state
- `TargetFramework`: `net10.0`

Migration steps
1. Update `TargetFramework` to `net10.0`.
2. Update package references (if any were flagged in assessment) to versions compatible with .NET 10.
3. Restore and build; fix compilation errors arising from API changes.
4. Validate public APIs used by `Redball.UI.WPF` do not break expected contracts.

Expected breaking-change areas
- Obsolete APIs removed between .NET 8 and .NET 10
- Default JSON serializer behavior changes if `System.Text.Json` is updated

Validation checklist
- [ ] `net10.0` in project file
- [ ] Builds without errors
- [ ] Library behavior validated by unit tests

### 4.3 `Redball.Tests` (test project — inferred)

Current state (inferred)
- Tests target `net8.0` and reference production projects

Target state
- `TargetFramework`: `net10.0`

Migration steps
1. Update `TargetFramework` to `net10.0`.
2. Update test framework packages if flagged in the assessment.
3. Restore and run all tests; fix failures.

Validation checklist
- [ ] All tests run and pass under .NET 10
- [ ] No test framework incompatibilities remain

---

## 5 Package Update Reference

Summary: Assessment identified 10 packages requiring updates across the solution. The table below lists the current versions (from `Redball.UI.WPF.csproj`) and a placeholder for the target version. The executor should use assessment-specified target versions or call package resolution tools to pick exact versions compatible with `.NET 10.0`.

Common Package Updates (current → target placeholder)
- `CommunityToolkit.Mvvm` 8.2.2 → (update to .NET 10 compatible version)
- `Hardcodet.NotifyIcon.Wpf` 1.1.0 → (update if available)
- `LottieSharp` 2.4.3 → (update if available)
- `Microsoft.Extensions.DependencyInjection` 8.0.1 → (update to 10.x matching runtime)
- `Microsoft.Xaml.Behaviors.Wpf` 1.1.135 → (update if available)
- `System.Management` 8.0.0 → (update to 10.x)
- `System.Speech` 8.0.0 → (update to 10.x)
- `InputInterceptor` 2.2.1 → (update if available)
- `System.ServiceProcess.ServiceController` 8.0.1 → (update to 10.x)
- `System.Text.Json` 8.0.5 → (update to 10.x)

Project-specific package notes
- `Redball.UI.WPF` — all packages listed above are referenced here and must be updated as part of the atomic upgrade.
- `Redball.Core` — include any package updates discovered in the assessment.
- `Redball.Tests` — update test-related packages if flagged.

Security vulnerabilities
- Assessment flagged package updates; any packages with security vulnerabilities must be prioritized. The exact CVE list and vulnerable package versions are in the assessment document and must be reprovisioned in the execution phase.

---

## 6 Breaking Changes Catalog

This section lists categories of breaking changes to inspect during the compilation and fix pass. The exact items will be discovered during the build step.

Common categories to expect
- API removals or signature changes in BCL packages (`System.*`) between .NET 8 and .NET 10
- Behavior changes in `System.Text.Json` serialization defaults
- CommunityToolkit.Mvvm source-generator or attribute changes
- WPF hosting/startup or resource resolution changes
- Native interop/PInvoke differences affecting `SendInput` usage and `InputInterceptor`

Action guidance
- Map each compilation or test failure to a breaking change category and document the required code change in the execution log.
- Prefer minimal invasive changes (polyfills, compatibility shims) only if they do not introduce long-term maintenance debt.

---

## 7 Testing & Validation Strategy

Levels of testing
- Unit tests: run all unit tests in `Redball.Tests` and any other test projects.
- Integration tests: run any integration tests available.
- Build verification: solution must build without errors and without relevant warnings.

Execution order
- After atomic upgrade and package updates: restore → build solution → run tests.
- Fix compilation errors and test failures in the same atomic upgrade pass.

Validation checklist for completion
- [ ] Solution builds successfully with `dotnet build` under `.NET 10.0` SDK
- [ ] All tests pass (unit and integration)
- [ ] No security-vulnerable package versions remain
- [ ] No unresolved dependency conflicts

Automated checks recommended
- CI job using .NET 10 SDK for restore/build/test
- Static analysis (optional) to detect API or security issues

---

## 8 Risk Management & Mitigation

Risk summary
- Medium risk: All-At-Once increases blast radius but is acceptable for a small solution.
- High-risk areas: WPF-specific runtime changes, native interop (InputInterceptor/SendInput), and packages with major version bumps.

Mitigations
- Keep an isolated upgrade branch and single commit for the atomic upgrade.
- Preserve a reproducible environment via `global.json` if necessary (validate SDK in §11).
- If a single package causes numerous breaking changes, consider a focused follow-up patch after the atomic upgrade to reduce scope.

Rollbacks
- If the atomic upgrade introduces unrecoverable regressions, revert the upgrade commit on the upgrade branch and open focused issues for individual fixes.

---

## 9 Source Control Strategy

Branching
- Use upgrade branch created during assessment: `upgrade/dotnet-version_d942db`.

Commit strategy
- Prefer a single comprehensive commit representing the atomic upgrade (project file changes + package version bumps).
- Include a detailed commit message referencing the assessment and plan (link to this `plan.md` path).

Pull request and review
- Create a PR from `upgrade/dotnet-version_d942db` to `main` once CI verifies build and tests.
- PR should include a checklist referencing the Validation checklist in §7.

---

## 10 Success Criteria

The upgrade is complete when all the following are met:
- All projects target the proposed framework (`net10.0` / `net10.0-windows`).
- All package updates from the assessment are applied (or documented exceptions).
- The solution builds without errors under .NET 10.0 SDK.
- All tests pass under .NET 10.0.
- No package dependency conflicts or known security vulnerabilities remain.

---

## 11 Appendices & Next Steps

Immediate next steps for the Execution stage (executor agent or developer):
- TASK-000: Prerequisites
  - Validate .NET 10 SDK installed on CI and local dev machines. Update `global.json` if present.
  - Ensure pending changes have been handled (commit/stash/undo as selected during assessment).

- TASK-001: Atomic framework and package upgrade (All projects simultaneously)
  - (1) Update `TargetFramework` for all projects to `net10.0` / `net10.0-windows`.
  - (2) Update all PackageReference versions to the assessment-specified targets (see §5).
  - (3) Restore dependencies and build solution; fix all compilation errors found.
  - (4) Verify solution builds with 0 errors.

- TASK-002: Test execution and fixes
  - (1) Run all tests and address failures.
  - (2) Verify no security vulnerabilities remain.

Notes & assumptions
- Where the assessment did not include exact target package versions, the executor must resolve package versions compatible with .NET 10 and document chosen versions in the execution log.
- If new issues are discovered during build/test, document them and escalate if they block the atomic upgrade.

Contact & references
- Assessment file: `.github/upgrades/scenarios/new-dotnet-version_d942db/assessment.md`

---

*Plan generated by planning agent. This is a planning-only document; no code changes have been performed.*
