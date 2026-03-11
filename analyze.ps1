Import-Module PSScriptAnalyzer
$results = Invoke-ScriptAnalyzer -Path ./Redball.ps1 -Severity Warning,Error
$results | Format-Table -AutoSize
Write-Host "Total issues: $($results.Count)"
