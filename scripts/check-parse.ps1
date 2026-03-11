# Check for parse errors in Redball.ps1
$tokens = $null
$parseErrors = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile('Redball.ps1', [ref]$tokens, [ref]$parseErrors)
if ($parseErrors) {
    Write-Host 'Parse errors found:'
    $parseErrors | ForEach-Object {
        Write-Host ('  Line ' + $_.Extent.StartLineNumber + ': ' + $_.Message)
    }
    exit 1
}
else {
    Write-Host 'No parse errors found'
}
