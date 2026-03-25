$file = 'c:\Users\karll\Desktop\Redball\scripts\build.ps1'
$line = (Get-Content $file)[1306]
Write-Host "Line 1307 (index 1306): $line"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($line)
Write-Host "Bytes: $($bytes -join ' ')"
