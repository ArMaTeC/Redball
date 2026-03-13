#Requires -Version 5.1
<#
.SYNOPSIS
    Bumps the version number in Redball.UI.WPF.csproj
.DESCRIPTION
    Increments the version number in Redball.UI.WPF.csproj and optionally commits/pushes the change.
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

$projectRoot = Split-Path $PSScriptRoot -Parent
$wpfProjectPath = Join-Path $projectRoot 'src' 'Redball.UI.WPF' 'Redball.UI.WPF.csproj'
$versionFilePath = Join-Path $PSScriptRoot 'version.txt'

if (-not (Test-Path $wpfProjectPath)) {
    throw "WPF project not found at: $wpfProjectPath"
}

# Read current version from WPF project
$csprojContent = Get-Content $wpfProjectPath -Raw
$versionPattern = '<Version>([0-9]+)\.([0-9]+)\.([0-9]+)</Version>'
$match = [regex]::Match($csprojContent, $versionPattern)

if (-not $match.Success) {
    throw "Could not find version pattern in $wpfProjectPath"
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

# Update WPF .csproj
$csprojContent = $csprojContent -replace '<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>', "<Version>$newVersion</Version>"
$csprojContent = $csprojContent -replace '<FileVersion>[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?</FileVersion>', "<FileVersion>$newVersion.0</FileVersion>"
$csprojContent = $csprojContent -replace '<AssemblyVersion>[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?</AssemblyVersion>', "<AssemblyVersion>$newVersion.0</AssemblyVersion>"

Set-Content -Path $wpfProjectPath -Value $csprojContent -NoNewline
Write-Host "Updated $wpfProjectPath" -ForegroundColor Green

# Write version file for MSI and other build processes
Set-Content -Path $versionFilePath -Value $newVersion -NoNewline
Write-Host "Updated version.txt" -ForegroundColor Green

# Commit if requested
if ($Commit -or $Push) {
    $commitMessage = if ($Message) { $Message } else { "Bump version to $newVersion" }

    Write-Host "Committing with message: $commitMessage" -ForegroundColor Yellow
    git add $versionFilePath
    git add $wpfProjectPath
    git commit -m $commitMessage

    if ($Push) {
        Write-Host "Pushing to remote..." -ForegroundColor Yellow
        git push
    }
}

Write-Host "Done!" -ForegroundColor Green
