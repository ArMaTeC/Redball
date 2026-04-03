@echo off
if not "%1"=="launch" (
    mshta vbscript:Execute("CreateObject(""WScript.Shell"").Run ""cmd /c """"%~f0"""" launch"",0:close")
    exit /b
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%LOCALAPPDATA%\Redball\Redball.ps1"
