Dim oShell, installDir, psPath, scriptPath
Set oShell = CreateObject("WScript.Shell")
installDir = Left(WScript.ScriptFullName, InStrRev(WScript.ScriptFullName, "\"))
psPath = "C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
scriptPath = installDir & "Redball.ps1"
oShell.Run Chr(34) & psPath & Chr(34) & " -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " & Chr(34) & scriptPath & Chr(34), 0, False
Set oShell = Nothing
