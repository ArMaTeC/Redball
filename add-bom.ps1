# Add UTF-8 BOM to Redball.ps1
$content = Get-Content 'Redball.ps1' -Raw
$encoding = New-Object System.Text.UTF8Encoding($true)
$bytes = $encoding.GetBytes($content)
[System.IO.File]::WriteAllBytes('Redball.ps1', $bytes)
Write-Host 'UTF-8 BOM added to Redball.ps1'
