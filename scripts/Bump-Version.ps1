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

function Write-HostSafe {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'Build script requires console output for user feedback')]
    param(
        [Parameter(Mandatory, Position = 0)]
        [object]$Object,
        [System.ConsoleColor]$ForegroundColor
    )
    Write-Host $Object -ForegroundColor $ForegroundColor
}

$projectRoot = Split-Path $PSScriptRoot -Parent
$srcDir = Join-Path $projectRoot 'src'
$wpfDir = Join-Path $srcDir 'Redball.UI.WPF'
$wpfProjectPath = Join-Path $wpfDir 'Redball.UI.WPF.csproj'
$propsPath = Join-Path $projectRoot 'Directory.Build.props'
$versionFilePath = Join-Path $PSScriptRoot 'version.txt'

$targetPath = $propsPath
if (-not (Test-Path $targetPath)) {
    $targetPath = $wpfProjectPath
}

if (-not (Test-Path $targetPath)) {
    throw "Version target file not found at: $targetPath"
}

# Read current version from target file
$targetContent = Get-Content $targetPath -Raw
$versionPattern = '<Version>([0-9]+)\.([0-9]+)\.([0-9]+)</Version>'
$match = [regex]::Match($targetContent, $versionPattern)

if (-not $match.Success) {
    throw "Could not find version pattern in $targetPath"
}

$major = [int]$match.Groups[1].Value
$minor = [int]$match.Groups[2].Value
$patch = [int]$match.Groups[3].Value
$currentVersion = "$major.$minor.$patch"

Write-HostSafe "Current version (from $(Split-Path $targetPath -Leaf)): $currentVersion" -ForegroundColor Cyan

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
Write-HostSafe "New version: $newVersion" -ForegroundColor Green

# Update target file
$targetContent = $targetContent -replace '<Version>[0-9]+\.[0-9]+\.[0-9]+</Version>', "<Version>$newVersion</Version>"
$targetContent = $targetContent -replace '<FileVersion>[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?</FileVersion>', "<FileVersion>$newVersion.0</FileVersion>"
$targetContent = $targetContent -replace '<AssemblyVersion>[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?</AssemblyVersion>', "<AssemblyVersion>$newVersion.0</AssemblyVersion>"

Set-Content -Path $targetPath -Value $targetContent -NoNewline
Write-HostSafe "Updated $targetPath" -ForegroundColor Green

# Write version file for MSI and other build processes
Set-Content -Path $versionFilePath -Value $newVersion -NoNewline
Write-HostSafe "Updated version.txt" -ForegroundColor Green

# Commit if requested
if ($Commit -or $Push) {
    $commitMessage = if ($Message) { $Message } else { "Bump version to $newVersion" }

    Write-HostSafe "Committing with message: $commitMessage" -ForegroundColor Yellow
    git add $versionFilePath
    git add $targetPath
    git commit -m $commitMessage

    if ($Push) {
        Write-HostSafe "Pushing to remote..." -ForegroundColor Yellow
        git push
    }
}

Write-HostSafe "Done!" -ForegroundColor Green








































































































































