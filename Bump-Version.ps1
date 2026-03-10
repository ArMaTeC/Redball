#Requires -Version 5.1
<#
.SYNOPSIS
    Bumps the version number in Redball.ps1
.DESCRIPTION
    Increments the version number in Redball.ps1 and optionally commits/pushes the change.
    Supports bumping major, minor, or patch version components.
.PARAMETER Component
    Which version component to bump: Major, Minor, or Patch (default: Patch)
.PARAMETER Commit
    Automatically commit the version bump
.PARAMETER Push
    Automatically push the commit (implies -Commit)
.PARAMETER Message
    Custom commit message (default: "Bump version to X.Y.Z")
.EXAMPLE
    .\Bump-Version.ps1
    Bumps patch version (2.0.32 -> 2.0.33)
.EXAMPLE
    .\Bump-Version.ps1 -Component Minor -Commit -Push
    Bumps minor version (2.0.32 -> 2.1.0), commits and pushes
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string]$Component = 'Patch',

    [Parameter()]
    [switch]$Commit,

    [Parameter()]
    [switch]$Push,

    [Parameter()]
    [string]$Message
)

$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'Redball.ps1'

if (-not (Test-Path $scriptPath)) {
    throw "Redball.ps1 not found at: $scriptPath"
}

# Read current version
$content = Get-Content $scriptPath -Raw
$versionPattern = "\`$script:VERSION\s*=\s*'([0-9]+)\.([0-9]+)\.([0-9]+)'"
$match = [regex]::Match($content, $versionPattern)

if (-not $match.Success) {
    throw "Could not find version pattern in Redball.ps1"
}

$major = [int]$match.Groups[1].Value
$minor = [int]$match.Groups[2].Value
$patch = [int]$match.Groups[3].Value
$currentVersion = "$major.$minor.$patch"

Write-Host "Current version: $currentVersion" -ForegroundColor Cyan

# Calculate new version
switch ($Component) {
    'Major' {
        $major++
        $minor = 0
        $patch = 0
    }
    'Minor' {
        $minor++
        $patch = 0
    }
    'Patch' {
        $patch++
    }
}

$newVersion = "$major.$minor.$patch"
Write-Host "New version: $newVersion" -ForegroundColor Green

# Update file
$newContent = $content -replace $versionPattern, "`$script:VERSION = '$newVersion'"
Set-Content -Path $scriptPath -Value $newContent -NoNewline

Write-Host "Updated $scriptPath" -ForegroundColor Green

# Commit if requested
if ($Commit -or $Push) {
    $commitMessage = if ($Message) { $Message } else { "Bump version to $newVersion" }

    Write-Host "Committing with message: $commitMessage" -ForegroundColor Yellow
    git add $scriptPath
    git commit -m $commitMessage

    if ($Push) {
        Write-Host "Pushing to remote..." -ForegroundColor Yellow
        git push
    }
}

Write-Host "Done!" -ForegroundColor Green
