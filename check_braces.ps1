$file = 'c:\Users\karll\Desktop\Redball\src\Redball.UI.WPF\Services\InterceptionInputService.cs'
$content = Get-Content $file
$openBraces = 0
$lineNumber = 0
foreach ($line in $content) {
    $lineNumber++
    $openCount = ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
    $closeCount = ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
    $openBraces += ($openCount - $closeCount)
    if ($openBraces -eq 0 -and ($openCount -gt 0 -or $closeCount -gt 0)) {
        Write-Host "Open braces = 0 at line $lineNumber: $line"
    }
}
Write-Host "Total open braces at end: $openBraces"
