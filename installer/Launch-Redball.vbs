' Launch-Redball.vbs - Launches Redball after installation
' This script runs in user context to avoid Session 0 isolation

Sub LaunchApp()
    Dim WshShell, appPath, appDir, launchCommand, fso
    
    ' Read install path from registry (set by installer)
    Set WshShell = CreateObject("WScript.Shell")
    On Error Resume Next
    appPath = WshShell.RegRead("HKCU\Software\Redball\InstallPath")
    On Error GoTo 0
    
    ' Fallback to default if registry value not found
    If appPath = "" Then
        appPath = WshShell.ExpandEnvironmentStrings("%LOCALAPPDATA%") & "\Redball\Redball.UI.WPF.exe"
    End If
    
    Set fso = CreateObject("Scripting.FileSystemObject")
    
    ' Retry up to 5 times with 500ms delay (files may still be committing)
    Dim attempts
    For attempts = 1 To 10
        If fso.FileExists(appPath) Then
            Exit For
        End If
        WScript.Sleep 500
    Next
    
    If fso.FileExists(appPath) Then
        appDir = fso.GetParentFolderName(appPath)
        If appDir <> "" Then
            WshShell.CurrentDirectory = appDir
        End If
        launchCommand = Chr(34) & appPath & Chr(34)
        WshShell.Run launchCommand, 0, False
    End If
End Sub
