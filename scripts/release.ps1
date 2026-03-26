#Requires -Version 5.1

<#
.SYNOPSIS
    Create GitHub release with changelog and artifacts.

.DESCRIPTION
    This script creates a GitHub release with auto-generated changelog from git commits,
    SHA256 checksums, and uploads MSI/Bundle artifacts.
    Automatically handles: missing tags, dirty working trees, unpushed commits,
    missing artifacts (triggers build), existing releases, and branch validation.

.PARAMETER Version
    Version for the release (e.g., "2.1.80"). Extracted from csproj if not provided.

.PARAMETER Tag
    Git tag for the release (e.g., "v2.1.80"). Auto-generated from version if not provided.

.PARAMETER ReleaseNotes
    Custom release notes. If not provided, generates from git commits.

.PARAMETER SkipRelease
    Skip creating GitHub release (validate only).

.PARAMETER DistDir
    Directory containing build artifacts. Defaults to ../dist.

.PARAMETER DryRun
    Show what would happen without making any changes.

.PARAMETER Force
    Skip all confirmation prompts.

.PARAMETER AllowDirty
    Allow release from a dirty working tree (uncommitted changes).

.PARAMETER SkipAutoBuild
    Skip automatically running the build script when artifacts are missing.

.EXAMPLE
    .\release.ps1

.EXAMPLE
    .\release.ps1 -Version "2.1.81" -ReleaseNotes "Custom release notes"

.EXAMPLE
    .\release.ps1 -SkipRelease

.EXAMPLE
    .\release.ps1 -DryRun
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Version = "",
    [string]$Tag = "",
    [string]$ReleaseNotes = "",
    [switch]$SkipRelease,
    [string]$DistDir = "",
    [switch]$DryRun,
    [switch]$Force,
    [switch]$AllowDirty,
    [switch]$SkipAutoBuild
)

$ErrorActionPreference = "Stop"
$script:stashedChanges = $false

# Resolve project root safely
$currentScriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path $MyInvocation.MyCommand.Path -Parent }
if (-not $currentScriptRoot) { $currentScriptRoot = (Get-Item .).FullName }
$parentDir = Split-Path $currentScriptRoot -Parent
$script:ProjectRoot = Resolve-Path $parentDir

if (-not $DistDir) {
    $script:DistDir = Join-Path $script:ProjectRoot "dist"
}
else {
    $script:DistDir = Resolve-Path $DistDir
}
$script:PublishDir = Join-Path $script:DistDir "wpf-publish"

# Helper Functions

function Write-HostSafe {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'Release script requires console output for user feedback')]
    param(
        [Parameter(Mandatory, Position = 0)]
        [object]$Object,
        [System.ConsoleColor]$ForegroundColor
    )
    if ($ForegroundColor) {
        Write-Host $Object -ForegroundColor $ForegroundColor
    }
    else {
        Write-Host $Object
    }
}

function Write-Step {
    param([string]$Message)
    Write-HostSafe "`n=== $Message ===" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-HostSafe "✓ $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-HostSafe "⚠ $Message" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-HostSafe "✗ $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-HostSafe "ℹ $Message" -ForegroundColor DarkGray
}

function Test-GitHubCli {
    $gh = Get-Command "gh" -ErrorAction SilentlyContinue
    if ($gh) {
        $ver = (& gh --version 2>$null | Select-Object -First 1)
        Write-Info "GitHub CLI found: $ver"
        return $true
    }
    return $false
}

function Test-GitRepo {
    $result = git rev-parse --is-inside-work-tree 2>$null
    return ($result -eq "true")
}

function Get-CurrentBranch {
    return (git rev-parse --abbrev-ref HEAD 2>$null)
}

function Test-DirtyWorkingTree {
    $status = git status --porcelain 2>$null
    return [bool]$status
}

function Test-UnpushedCommit {
    $upstream = git rev-parse --abbrev-ref '@{upstream}' 2>$null
    if (-not $upstream) { return $false }
    $ahead = git rev-list --count "$upstream..HEAD" 2>$null
    return ([int]$ahead -gt 0)
}

function Get-UnpushedCount {
    $upstream = git rev-parse --abbrev-ref '@{upstream}' 2>$null
    if (-not $upstream) { return 0 }
    return [int](git rev-list --count "$upstream..HEAD" 2>$null)
}

function Test-TagExistsLocally {
    param([string]$TagName)
    $result = git tag -l $TagName 2>$null
    return [bool]$result
}

function Test-TagExistsRemotely {
    param([string]$TagName, [string]$Remote = "origin")
    $result = git ls-remote --tags $Remote "refs/tags/$TagName" 2>$null
    return [bool]$result
}

function Get-ProjectVersion {
    if ($script:Version) {
        return $script:Version
    }

    $srcDir = Join-Path $ProjectRoot "src"
    $wpfDir = Join-Path $srcDir "Redball.UI.WPF"
    $CsprojPath = Join-Path $wpfDir "Redball.UI.WPF.csproj"
    if (-not (Test-Path $CsprojPath)) {
        throw "Project file not found: $CsprojPath"
    }

    [xml]$csproj = Get-Content $CsprojPath
    $versionNode = $csproj.Project.PropertyGroup.Version

    if ($versionNode) {
        return $versionNode
    }

    throw "Could not extract version from $CsprojPath. Specify -Version manually."
}

function Get-ChangeLog {
    param([string]$CurrentTag)

    # Find previous tag (excluding the current one)
    $allTags = git tag --sort=-v:refname 2>$null
    $previousTag = $null
    if ($allTags) {
        foreach ($t in $allTags) {
            if ($t -ne $CurrentTag) {
                $previousTag = $t
                break
            }
        }
    }

    # Always get the commit list — try tag range first, then full log
    if ($previousTag) {
        Write-Info "Generating changelog from $previousTag to HEAD..."
        $commits = git log "$previousTag..HEAD" --pretty=format:"- %s (%h)" --no-merges 2>$null
        $rangeLabel = "Changes since $previousTag"
    }
    else {
        Write-Info "No previous tag found, listing all commits..."
        $commits = git log --pretty=format:"- %s (%h)" --no-merges 2>$null
        $rangeLabel = "All Changes"
    }

    # If we still got nothing (e.g. single-commit repo), try without --no-merges
    if (-not $commits) {
        Write-Info "No non-merge commits found, including merge commits..."
        if ($previousTag) {
            $commits = git log "$previousTag..HEAD" --pretty=format:"- %s (%h)" 2>$null
        }
        else {
            $commits = git log --pretty=format:"- %s (%h)" 2>$null
        }
    }

    $changelog = "## $rangeLabel`n`n"

    if ($commits) {
        $changelog += ($commits -join "`n")
    }
    else {
        $changelog += "- No commits found in range`n"
    }

    $changelog += "`n`n## Installation`n`n"
    $changelog += "Download and run ``Redball.msi`` to install.`n`n"
    $changelog += "## SHA256 Checksums`n`n"

    # Calculate checksums for all artifacts in dist
    $artifacts = Get-ChildItem $DistDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.msi', '.exe' }
    foreach ($artifact in $artifacts) {
        $hash = (Get-FileHash $artifact.FullName -Algorithm SHA256).Hash
        $changelog += "- ``$($artifact.Name)``: ``$hash```n"
    }

    return $changelog
}

function Invoke-BuildIfNeeded {
    param([string]$VersionStr)

    $buildScript = Join-Path $PSScriptRoot "build.ps1"
    if (-not (Test-Path $buildScript)) {
        throw "Build script not found at $buildScript. Build manually and re-run."
    }

    Write-Step "Running build script"
    Write-Info "Invoking: $buildScript -Version $VersionStr"

    if ($DryRun) {
        Write-Info "[DRY RUN] Would run build script with -Version $VersionStr"
        return
    }

    & $buildScript -Version $VersionStr -SkipVersionBump
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Write-Success "Build completed"
}

function Restore-StashedChange {
    if ($script:stashedChanges) {
        Write-Info "Restoring stashed changes..."
        git stash pop 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Stashed changes restored"
        }
        else {
            Write-Warn "Could not auto-restore stash. Run 'git stash pop' manually."
        }
        $script:stashedChanges = $false
    }
}

# End Helper Functions

# Main Script

try {

    Write-Step "Redball GitHub Release Script"
    Write-HostSafe "Project root: $($script:ProjectRoot)"
    Write-HostSafe "Dist directory: $($script:DistDir)"
    if ($DryRun) { Write-Warn "DRY RUN MODE - no changes will be made" }

    # ── 1. Validate we're in a git repo ──
    Write-Step "Pre-flight checks"

    if (-not (Test-GitRepo)) {
        throw "Not inside a git repository. Run this script from the Redball project root."
    }
    Write-Success "Inside git repository"

    # ── 2. Validate GitHub CLI ──
    if (-not (Test-GitHubCli)) {
        throw "GitHub CLI (gh) not found. Install from https://cli.github.com/"
    }

    # ── 3. Check GitHub authentication ──
    $authOutput = (& gh auth status 2>&1) | Out-String
    if ($authOutput -match "not logged" -or $LASTEXITCODE -ne 0) {
        Write-Err "Not authenticated with GitHub."
        Write-Info "Attempting 'gh auth login'..."
        if (-not $DryRun) {
            & gh auth login
            if ($LASTEXITCODE -ne 0) {
                throw "GitHub authentication failed. Run 'gh auth login' manually."
            }
        }
        else {
            Write-Info "[DRY RUN] Would run 'gh auth login'"
        }
    }
    Write-Success "Authenticated with GitHub"

    # ── 4. Branch validation ──
    $currentBranch = Get-CurrentBranch
    Write-Info "Current branch: $currentBranch"
    if ($currentBranch -notin @("main", "master", "release") -and $currentBranch -notmatch "^release/") {
        Write-Warn "Releasing from branch '$currentBranch' (not main/master/release)."
        if (-not $Force) {
            Write-Info "Use -Force to release from any branch."
        }
    }

    # ── 5. Fetch latest remote state ──
    Write-Info "Fetching latest from remote..."
    if (-not $DryRun) {
        git fetch --tags --prune origin --quiet
        Write-Success "Remote state synced"
    }

    # ── 6. Handle dirty working tree ──
    if (Test-DirtyWorkingTree) {
        if ($AllowDirty) {
            Write-Warn "Working tree has uncommitted changes (--AllowDirty specified, continuing)."
        }
        else {
            Write-Warn "Working tree has uncommitted changes. Stashing them for a clean release..."
            if (-not $DryRun) {
                git stash push -m "release-script-auto-stash" --include-untracked 2>$null
                if ($LASTEXITCODE -eq 0) {
                    $script:stashedChanges = $true
                    Write-Success "Changes stashed (will restore after release)"
                }
                else {
                    throw "Failed to stash changes. Commit or stash them manually."
                }
            }
            else {
                Write-Info "[DRY RUN] Would stash uncommitted changes"
            }
        }
    }
    else {
        Write-Success "Working tree is clean"
    }

    # ── 7. Get version and tag ──
    Write-Step "Resolving version"
    $version = Get-ProjectVersion
    if (-not $Tag) {
        $Tag = "v$version"
    }
    Write-HostSafe "Version: $version"
    Write-HostSafe "Tag:     $Tag"

    # ── 8. Push any unpushed commits ──
    $unpushed = Get-UnpushedCount
    if ($unpushed -gt 0) {
        Write-Warn "$unpushed unpushed commit(s) detected."
        Write-Info "Pushing commits to origin/$currentBranch..."
        if (-not $DryRun) {
            git push origin $currentBranch --quiet
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Pushed $unpushed commit(s) to origin/$currentBranch"
            }
            else {
                throw "Failed to push commits. Resolve conflicts or push manually."
            }
        }
        else {
            Write-Info "[DRY RUN] Would push $unpushed commit(s)"
        }
    }
    else {
        Write-Success "All commits are pushed"
    }

    # ── 9. Handle tag creation/sync ──
    Write-Step "Tag management"
    $tagLocal = Test-TagExistsLocally -TagName $Tag
    $tagRemote = Test-TagExistsRemotely -TagName $Tag

    if ($tagLocal -and $tagRemote) {
        Write-Success "Tag $Tag exists locally and on remote"
    }
    elseif ($tagLocal -and -not $tagRemote) {
        Write-Warn "Tag $Tag exists locally but not on remote. Pushing..."
        if (-not $DryRun) {
            git push origin $Tag
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Pushed tag $Tag to origin"
            }
            else {
                throw "Failed to push tag $Tag to origin."
            }
        }
        else {
            Write-Info "[DRY RUN] Would push tag $Tag to origin"
        }
    }
    elseif (-not $tagLocal -and $tagRemote) {
        Write-Warn "Tag $Tag exists on remote but not locally. Fetching..."
        if (-not $DryRun) {
            git fetch origin tag $Tag --quiet
            Write-Success "Fetched tag $Tag from origin"
        }
        else {
            Write-Info "[DRY RUN] Would fetch tag $Tag from origin"
        }
    }
    else {
        Write-Warn "Tag $Tag does not exist anywhere. Creating..."
        if (-not $DryRun) {
            git tag -a $Tag -m "Release $Tag"
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to create tag $Tag locally."
            }
            Write-Success "Created tag $Tag locally"

            git push origin $Tag
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to push tag $Tag to origin."
            }
            Write-Success "Pushed tag $Tag to origin"
        }
        else {
            Write-Info "[DRY RUN] Would create and push tag $Tag"
        }
    }

    # ── 10. Validate/build artifacts ──
    Write-Step "Artifact validation"
    $msiPath = Join-Path $DistDir "Redball.msi"
    $msiVersioned = Join-Path $DistDir "Redball-$version.msi"

    if (-not (Test-Path $msiPath) -and -not (Test-Path $msiVersioned)) {
        Write-Warn "No MSI artifacts found in $DistDir."
        if (-not $SkipAutoBuild) {
            Invoke-BuildIfNeeded -VersionStr $version
        }
        else {
            throw "MSI not found at $msiPath. Run build first or remove -SkipAutoBuild."
        }
    }

    # Re-check after potential build
    if (-not (Test-Path $msiPath) -and -not (Test-Path $msiVersioned)) {
        if (-not $DryRun) {
            throw "MSI still not found after build. Check build output."
        }
    }

    if (Test-Path $msiPath) { Write-Success "Found: $msiPath" }
    if (Test-Path $msiVersioned) { Write-Success "Found: $msiVersioned" }

    # Build upload files list
    $uploadFiles = @()
    if (Test-Path $msiPath) { $uploadFiles += $msiPath }
    if (Test-Path $msiVersioned) { $uploadFiles += $msiVersioned }

    $bundlePath = Join-Path $DistDir "Redball-Setup.exe"
    $bundleVersioned = Join-Path $DistDir "Redball-Setup-$version.exe"
    if (Test-Path $bundlePath) { $uploadFiles += $bundlePath }
    if (Test-Path $bundleVersioned) { $uploadFiles += $bundleVersioned }

    # Create a generic Redball.msi copy to satisfy old updater's priority #0
    $genericMsi = Join-Path $DistDir "Redball.msi"
    if (Test-Path $msiVersioned) {
        if ($msiVersioned -ne $genericMsi) {
            Copy-Item -Path $msiVersioned -Destination $genericMsi -Force
            if ($uploadFiles -notcontains $genericMsi) {
                $uploadFiles += $genericMsi
            }
            Write-Success "Created generic MSI for legacy updaters: $genericMsi"
        }
    }

    # Add all modular files from wpf-publish for differential updates
    if (Test-Path $script:PublishDir) {
        # Unblock InputInterceptor.dll if present
        $interceptorFile = Join-Path $script:PublishDir "InputInterceptor.dll"
        if (Test-Path $interceptorFile) {
            Write-HostSafe "  Unblocking InputInterceptor.dll in publish folder..." -ForegroundColor Gray
            Unblock-File -Path $interceptorFile -ErrorAction SilentlyContinue
        }
        
        $modFiles = Get-ChildItem -Path $script:PublishDir -File -Recurse | Where-Object { $_.Name -ne ".keep" }
        foreach ($file in $modFiles) {
            $uploadFiles += $file.FullName
        }
    }

    Write-HostSafe "`nArtifacts to upload:"
    $uploadFiles | ForEach-Object { Write-HostSafe "  - $(Split-Path $_ -Leaf)" }

    if ($uploadFiles.Count -eq 0 -and -not $DryRun) {
        throw "No uploadable artifacts found."
    }

    # -- 11. Skip release if requested --
    if ($SkipRelease) {
        Write-Info "Skipping release creation (-SkipRelease specified)"
        Restore-StashedChange
        Write-HostSafe ""
        Write-Success "Validation completed successfully!"
        return
    }

    # -- 12. Generate release notes --
    Write-Step "Creating GitHub Release: $Tag"

    if (-not $ReleaseNotes) {
        $ReleaseNotes = Get-ChangeLog -CurrentTag $Tag
    }

    $notesFile = Join-Path $env:TEMP "release-notes-$version.md"
    $ReleaseNotes | Set-Content $notesFile -Encoding UTF8
    Write-Info "Release notes saved to $notesFile"

    # ── 13. Create or update release ──
    if ($DryRun) {
        Write-Info "[DRY RUN] Would create/update GitHub release $Tag with $($uploadFiles.Count) artifact(s)"
    }
    else {
        # Check if release already exists
        $releaseExists = $false
        try {
            & gh release view $Tag --json tagName > $null 2>&1
            $releaseExists = ($LASTEXITCODE -eq 0)
        }
        catch {
            $releaseExists = $false
        }

        if ($releaseExists) {
            Write-Warn "Release $Tag already exists. Updating artifacts (--clobber)..."
            foreach ($file in $uploadFiles) {
                if ($PSCmdlet.ShouldProcess($file, "Upload to release $Tag")) {
                    & gh release upload $Tag $file --clobber
                    if ($LASTEXITCODE -eq 0) {
                        Write-Success "Uploaded: $(Split-Path $file -Leaf)"
                    }
                    else {
                        Write-Err "Failed to upload: $(Split-Path $file -Leaf)"
                    }
                }
            }

            # Update release notes if we generated new ones
            if ($PSCmdlet.ShouldProcess($Tag, "Update release notes")) {
                & gh release edit $Tag --notes-file $notesFile 2>$null
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "Release notes updated"
                }
            }
        }
        else {
            # Create new release
            $releaseArgs = @(
                "release", "create", $Tag,
                "--title", "Release $Tag",
                "--notes-file", $notesFile
            )

            foreach ($file in $uploadFiles) {
                $releaseArgs += $file
            }

            if ($PSCmdlet.ShouldProcess($Tag, "Create GitHub release")) {
                & gh @releaseArgs
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "GitHub release created: https://github.com/ArMaTeC/Redball/releases/tag/$Tag"
                }
                else {
                    throw "GitHub release creation failed (exit code $LASTEXITCODE)"
                }
            }
            else {
                Write-HostSafe "Would create release with args:"
                $releaseArgs | ForEach-Object { Write-HostSafe "  $_" }
            }
        }
    }

    # ── 14. Cleanup ──
    Remove-Item $notesFile -Force -ErrorAction SilentlyContinue
    Restore-StashedChange

    Write-HostSafe ""
    Write-Success "Release $Tag completed successfully!"
}
catch {
    # Ensure stashed changes are restored even on failure
    Restore-StashedChange
    Write-Err "Release failed: $_"
    throw
}
finally {
    # Belt-and-suspenders: always try to restore stash
    if ($script:stashedChanges) {
        Restore-StashedChange
    }
}

# End Main Script




























































































