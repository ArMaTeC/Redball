# Script to add missing using Redball.UI.Services to all files that use Logger
$files = @(
    "src/Redball.UI.WPF/Services/AccessibilityService.cs",
    "src/Redball.UI.WPF/Services/AdaptiveLayoutService.cs",
    "src/Redball.UI.WPF/Services/LatencyMaskingService.cs",
    "src/Redball.UI.WPF/Services/PRDStrategyGateService.cs",
    "src/Redball.UI.WPF/Services/ProductStrategyService.cs",
    "src/Redball.UI.WPF/Services/ReconciliationService.cs",
    "src/Redball.UI.WPF/Services/ReliabilityContractService.cs",
    "src/Redball.UI.WPF/Services/ReproducibleBuildService.cs",
    "src/Redball.UI.WPF/Services/ValueMapService.cs",
    "src/Redball.UI.WPF/Services/VersionedDataModelService.cs",
    "src/Redball.UI.WPF/Services/VisualHierarchyAuditService.cs",
    "src/Redball.UI.WPF/Services/WindowsShellIntegrationService.cs",
    "src/Redball.UI.WPF/Views/CommandPaletteWindow.xaml.cs",
    "src/Redball.UI.WPF/Views/Pages/SyncHealthPage.xaml.cs"
)

foreach ($file in $files) {
    if (Test-Path $file) {
        $content = Get-Content $file -Raw
        if ($content -match 'Logger\.' -and $content -notmatch 'using Redball\.UI\.Services;') {
            # Add using statement before namespace
            $newContent = $content -replace '(^using .*?;\r?\n)(namespace )', "`$1using Redball.UI.Services;`r`n`r`n`$2"
            if ($content -ne $newContent) {
                Set-Content -Path $file -Value $newContent -NoNewline
                Write-Host "Fixed: $file"
            }
        }
    }
}

Write-Host "Done fixing Logger using statements"
