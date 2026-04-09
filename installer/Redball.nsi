; ============================================================================
; Redball Modern NSIS Installer
; ============================================================================
; A feature-rich, customizable installer for Redball Windows application
; Built for NSIS 3.0+ with Modern UI 2
;
; Features:
;   - Modern branded welcome/finish pages
;;   - Custom component selection
;   - Auto-start with Windows option
;   - Desktop & Start Menu shortcuts
;   - Service installation
;   - Auto-launch after install
;   - Silent install support (/S)
;   - Windows 10/11 themed
; ============================================================================

!define PRODUCT_NAME "Redball"
!define PRODUCT_PUBLISHER "ArMaTeC"
!define PRODUCT_WEB_SITE "https://github.com/ArMaTeC/Redball"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\Redball.UI.WPF.exe"
!define PRODUCT_REGISTRY_KEY "Software\ArMaTeC\Redball"

; Version will be replaced by build script
!define PRODUCT_VERSION "2.1.443.0"
!define PRODUCT_VERSION_SHORT "2.1.443"
!define PROJECT_ROOT "..\.."

; Installer settings
Name "${PRODUCT_NAME} ${PRODUCT_VERSION_SHORT}"
OutFile "Redball-${PRODUCT_VERSION_SHORT}-Setup.exe"
InstallDir "$LOCALAPPDATA\${PRODUCT_NAME}"
InstallDirRegKey HKCU "${PRODUCT_DIR_REGKEY}" ""
RequestExecutionLevel user
SetCompressor lzma
SetCompressorDictSize 32

; Includes
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"
!include "Sections.nsh"
!include "nsDialogs.nsh"
!include "NSISdl.nsh"

; ============================================================================
; .NET Runtime Settings
; ============================================================================
!define DOTNET_VERSION "10.0.0"
!define DOTNET_MAJOR "10"
!define DOTNET_DOWNLOAD_URL "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.5/windowsdesktop-runtime-10.0.5-win-x64.exe"
!define DOTNET_INSTALLER "windowsdesktop-runtime-10.0.5-win-x64.exe"
!define DOTNET_SIZE_MB "64"

; ============================================================================
; MUI Settings
; ============================================================================

; General MUI settings
!define MUI_ABORTWARNING
!define MUI_ICON "redball.ico"
!define MUI_UNICON "redball.ico"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "nsis-header.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP "nsis-welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "nsis-welcome.bmp"

; Welcome page
!define MUI_WELCOMEPAGE_TITLE "Welcome to ${PRODUCT_NAME} ${PRODUCT_VERSION_SHORT}"
!define MUI_WELCOMEPAGE_TEXT "Redball is a modern keep-awake utility for Windows. Features include: Keep-Alive Engine, TypeThing clipboard typing, Session Timer, 14 custom themes, and smart power management. Click Next to continue."

; License page
!define MUI_LICENSEPAGE_TEXT_TOP "Please review the license terms before installing ${PRODUCT_NAME}."
!define MUI_LICENSEPAGE_TEXT_BOTTOM "If you accept the terms, click I Agree to continue. You must accept to install."
!define MUI_LICENSEPAGE_BUTTON "I Agree"

; Directory page
!define MUI_DIRECTORYPAGE_TEXT_TOP "Choose the folder in which to install ${PRODUCT_NAME}."
!define MUI_DIRECTORYPAGE_TEXT_DESTINATION "Installation Folder"

; Components page
!define MUI_COMPONENTSPAGE_TEXT_TOP "Select which components to install. Required components are already selected."
!define MUI_COMPONENTSPAGE_TEXT_COMPLIST "Components:"
!define MUI_COMPONENTSPAGE_TEXT_DESCRIPTION_TITLE "Description"
!define MUI_COMPONENTSPAGE_TEXT_DESCRIPTION_INFO "Hover over a component to see its description."

; Finish page
!define MUI_FINISHPAGE_TITLE "${PRODUCT_NAME} Installation Complete"
!define MUI_FINISHPAGE_TEXT "${PRODUCT_NAME} ${PRODUCT_VERSION_SHORT} has been installed. Click Finish to close the installer."
!define MUI_FINISHPAGE_RUN "$INSTDIR\Redball.UI.WPF.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${PRODUCT_NAME}"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\README.txt"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Show Readme"

; Uninstaller pages
!define MUI_UNABORTWARNING

; ============================================================================
; Pages
; ============================================================================

; Installer pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "/root/Redball/LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; Uninstaller pages
!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

; ============================================================================
; Languages
; ============================================================================

!insertmacro MUI_LANGUAGE "English"

; ============================================================================
; Section Descriptions
; ============================================================================

LangString DESC_SecApp ${LANG_ENGLISH} "The main Redball application (required)"
LangString DESC_SecService ${LANG_ENGLISH} "Background service for advanced features (optional)"
LangString DESC_SecStartMenu ${LANG_ENGLISH} "Add Redball to your Start Menu"
LangString DESC_SecDesktop ${LANG_ENGLISH} "Add Redball shortcut to your Desktop"
LangString DESC_SecStartup ${LANG_ENGLISH} "Start Redball automatically when Windows starts"

; ============================================================================
; Variables
; ============================================================================

Var ServiceInstalled
Var DotNetInstalled
Var DotNetDownloaded

; ============================================================================
; .NET Runtime Detection and Installation
; ============================================================================

Function CheckDotNet
    ; Check multiple registry locations for .NET 10 Windows Desktop Runtime
    
    ; Method 1: Check Windows Desktop App 10.0.x (specific version)
    ClearErrors
    ReadRegStr $0 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App" "10.0.5"
    ${IfNot} ${Errors}
        StrCpy $DotNetInstalled 1
        DetailPrint ".NET 10 Windows Desktop Runtime found (10.0.5)"
        Return
    ${EndIf}
    
    ; Method 2: Check for any 10.0.x version
    ClearErrors
    ReadRegStr $0 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App" "10.0.0"
    ${IfNot} ${Errors}
        StrCpy $DotNetInstalled 1
        DetailPrint ".NET 10 Windows Desktop Runtime found (10.0.0)"
        Return
    ${EndIf}
    
    ; Method 3: Check WOW64 node for 32-bit registry on 64-bit Windows
    ClearErrors
    ReadRegStr $0 HKLM "SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App" "10.0.5"
    ${IfNot} ${Errors}
        StrCpy $DotNetInstalled 1
        DetailPrint ".NET 10 Windows Desktop Runtime found (WOW64)"
        Return
    ${EndIf}
    
    ; Method 4: Check if dotnet.exe exists and can report version
    ClearErrors
    nsExec::ExecToStack '"$ProgramFiles64\dotnet\dotnet.exe" --version'
    Pop $0
    Pop $1
    ${If} $0 == 0
        ${If} $1 != ""
            StrCpy $DotNetInstalled 1
            DetailPrint ".NET 10 found via dotnet.exe: $1"
            Return
        ${EndIf}
    ${EndIf}
    
    ; Method 5: Check for self-contained flag
    ${If} ${FileExists} "$INSTDIR\.selfcontained"
        StrCpy $DotNetInstalled 1
        DetailPrint "Self-contained build detected"
        Return
    ${EndIf}
    
    StrCpy $DotNetInstalled 0
    DetailPrint ".NET 10 Windows Desktop Runtime not found"
FunctionEnd

Function InstallDotNet
    ${If} $DotNetInstalled == 1
        DetailPrint ".NET already installed, skipping"
        Return
    ${EndIf}
    
    ; Use the embedded installer from $PLUGINSDIR
    ${If} ${FileExists} "$PLUGINSDIR\${DOTNET_INSTALLER}"
        DetailPrint "Installing bundled .NET 10 Runtime..."
        ExecWait '"$PLUGINSDIR\${DOTNET_INSTALLER}" /install /quiet /norestart' $0
        ${If} $0 == 0
            DetailPrint ".NET 10 installed successfully"
            StrCpy $DotNetInstalled 1
        ${Else}
            DetailPrint ".NET installation failed (code: $0)"
            MessageBox MB_OK|MB_ICONEXCLAMATION ".NET 10 installation failed. You may need to install it manually from https://dotnet.microsoft.com/download"
        ${EndIf}
        Return
    ${EndIf}
    
    ; Fallback: Download .NET installer (shouldn't happen with embedded)
    DetailPrint "Downloading .NET 10 Windows Desktop Runtime (~${DOTNET_SIZE_MB} MB)..."
    DetailPrint "This may take a few minutes depending on your connection..."
    
    NSISdl::download "${DOTNET_DOWNLOAD_URL}" "$TEMP\${DOTNET_INSTALLER}"
    Pop $0
    ${If} $0 != "success"
        DetailPrint "Download failed: $0"
        MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to download .NET 10 Runtime ($0). Please install it manually from https://dotnet.microsoft.com/download/dotnet/10.0"
        Return
    ${EndIf}
    
    StrCpy $DotNetDownloaded 1
    DetailPrint "Download complete. Installing .NET 10..."
    
    ; Install .NET
    ExecWait '"$TEMP\${DOTNET_INSTALLER}" /install /quiet /norestart' $0
    
    ${If} $0 == 0
        DetailPrint ".NET 10 installed successfully"
        StrCpy $DotNetInstalled 1
    ${Else}
        DetailPrint ".NET installation failed (code: $0)"
        MessageBox MB_OK|MB_ICONEXCLAMATION ".NET 10 installation failed (code: $0). You may need to install it manually from https://dotnet.microsoft.com/download"
    ${EndIf}
    
    ; Clean up downloaded file
    Delete "$TEMP\${DOTNET_INSTALLER}"
FunctionEnd

; ============================================================================
; .NET Runtime Section - Embeds installer in package
; ============================================================================
Section ".NET 10 Runtime (Embedded)" SecDotNet
    SectionIn RO
    
    ; Extract embedded .NET installer to plugins directory
    SetOutPath "$PLUGINSDIR"
    File "${DOTNET_INSTALLER}"
    
    Call CheckDotNet
    ${If} $DotNetInstalled == 0
        Call InstallDotNet
    ${EndIf}
SectionEnd

LangString DESC_SecDotNet ${LANG_ENGLISH} ".NET 10 Windows Desktop Runtime (~64 MB bundled) - required to run Redball"

; ============================================================================
; Installer Sections
; ============================================================================

Section "!${PRODUCT_NAME} Application" SecApp
    SectionIn RO
    
    DetailPrint "Installing ${PRODUCT_NAME}..."
    SetOutPath "$INSTDIR"
    SetOverwrite on
    
    ; Main executable and DLLs
    File "Redball.UI.WPF.exe"
    File "Redball.UI.WPF.dll"
    File "Redball.UI.WPF.deps.json"
    File "Redball.UI.WPF.runtimeconfig.json"
    
    ; Copy DLL folder if exists
    IfFileExists "dll\*.*" 0 +3
        SetOutPath "$INSTDIR\dll"
        File /r "dll\*.*"
        SetOutPath "$INSTDIR"
    
    ; Copy Assets if exists
    IfFileExists "Assets\*.*" 0 +3
        SetOutPath "$INSTDIR\Assets"
        File /r "Assets\*.*"
        SetOutPath "$INSTDIR"
    
    ; Create logs directory
    CreateDirectory "$INSTDIR\logs"
    
    ; Create README
    FileOpen $0 "$INSTDIR\README.txt" w
    FileWrite $0 "${PRODUCT_NAME} ${PRODUCT_VERSION_SHORT}$\r$\n"
    FileWrite $0 "=========================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "Thank you for installing ${PRODUCT_NAME}!$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "Quick Start:$\r$\n"
    FileWrite $0 "1. Launch ${PRODUCT_NAME} from Start Menu or Desktop$\r$\n"
    FileWrite $0 "2. Right-click the system tray icon to access settings$\r$\n"
    FileWrite $0 "3. Enable 'Keep Awake' to prevent Windows sleep$\r$\n"
    FileWrite $0 "4. Use Ctrl+Shift+V for TypeThing clipboard typing$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "For more information, visit:$\r$\n"
    FileWrite $0 "${PRODUCT_WEB_SITE}$\r$\n"
    FileClose $0
    
    ; Register application
    WriteRegStr HKCU "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\Redball.UI.WPF.exe"
    WriteRegStr HKCU "${PRODUCT_REGISTRY_KEY}" "" "$INSTDIR"
    WriteRegStr HKCU "${PRODUCT_REGISTRY_KEY}" "Version" "${PRODUCT_VERSION}"
    WriteRegStr HKCU "${PRODUCT_REGISTRY_KEY}" "InstallDate" "${PRODUCT_VERSION}"
    
    ; Uninstall information
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayName" "${PRODUCT_NAME}"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "UninstallString" "$INSTDIR\uninstall.exe"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\Redball.UI.WPF.exe"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "URLUpdateInfo" "${PRODUCT_WEB_SITE}/releases"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "HelpLink" "${PRODUCT_WEB_SITE}/issues"
    WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoRepair" 1
    
    ; Write uninstaller
    WriteUninstaller "$INSTDIR\uninstall.exe"
    
    DetailPrint "${PRODUCT_NAME} installed successfully"
SectionEnd

Section /o "Background Service" SecService
    SectionIn 2
    
    DetailPrint "Installing Background Service..."
    SetOutPath "$INSTDIR"
    
    ; Copy service files (single-file published, no separate DLL)
    File "Redball.Service.exe"
    
    ; Install service (requires admin - skip if not elevated)
    UserInfo::GetAccountType
    Pop $0
    ${If} $0 == "Admin"
        DetailPrint "Registering Windows Service..."
        nsExec::Exec '"$INSTDIR\Redball.Service.exe" install'
        Pop $0
        ${If} $0 == 0
            StrCpy $ServiceInstalled 1
            WriteRegDWORD HKCU "${PRODUCT_REGISTRY_KEY}" "ServiceInstalled" 1
            DetailPrint "Service installed successfully"
        ${Else}
            DetailPrint "Service installation failed (code: $0)"
        ${EndIf}
    ${Else}
        DetailPrint "Admin rights required for service installation - skipped"
        WriteRegDWORD HKCU "${PRODUCT_REGISTRY_KEY}" "ServiceSkipped" 1
    ${EndIf}
SectionEnd

Section "Start Menu Shortcuts" SecStartMenu
    SectionIn 1 2
    
    DetailPrint "Creating Start Menu shortcuts..."
    CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
    CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk" "$INSTDIR\Redball.UI.WPF.exe"
    CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\README.lnk" "$INSTDIR\README.txt"
    CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall.lnk" "$INSTDIR\uninstall.exe"
    
    WriteRegStr HKCU "${PRODUCT_REGISTRY_KEY}" "StartMenuShortcuts" "1"
SectionEnd

Section /o "Desktop Shortcut" SecDesktop
    SectionIn 1 2
    
    DetailPrint "Creating Desktop shortcut..."
    CreateShortcut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\Redball.UI.WPF.exe"
    
    WriteRegStr HKCU "${PRODUCT_REGISTRY_KEY}" "DesktopShortcut" "1"
SectionEnd

Section /o "Start with Windows" SecStartup
    SectionIn 2
    
    DetailPrint "Configuring auto-start..."
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}" "$INSTDIR\Redball.UI.WPF.exe --minimized"
    WriteRegDWORD HKCU "${PRODUCT_REGISTRY_KEY}" "AutoStart" 1
SectionEnd

; ============================================================================
; Uninstaller Section
; ============================================================================

Section "Uninstall"
    DetailPrint "Removing ${PRODUCT_NAME}..."
    
    ; Stop service if installed
    ReadRegDWORD $0 HKCU "${PRODUCT_REGISTRY_KEY}" "ServiceInstalled"
    ${If} $0 == 1
        DetailPrint "Stopping service..."
        nsExec::Exec 'sc stop "Redball Input Service"'
        Sleep 3000
        nsExec::Exec 'sc delete "Redball Input Service"'
        Sleep 1000
    ${EndIf}
    
    ; Remove auto-start
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${PRODUCT_NAME}"
    
    ; Remove shortcuts
    ReadRegDWORD $0 HKCU "${PRODUCT_REGISTRY_KEY}" "StartMenuShortcuts"
    ${If} $0 == 1
        Delete "$SMPROGRAMS\${PRODUCT_NAME}\${PRODUCT_NAME}.lnk"
        Delete "$SMPROGRAMS\${PRODUCT_NAME}\README.lnk"
        Delete "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall.lnk"
        RMDir "$SMPROGRAMS\${PRODUCT_NAME}"
    ${EndIf}
    
    ReadRegDWORD $0 HKCU "${PRODUCT_REGISTRY_KEY}" "DesktopShortcut"
    ${If} $0 == 1
        Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
    ${EndIf}
    
    ; Stop running process
    DetailPrint "Stopping ${PRODUCT_NAME}..."
    
    ; Try graceful close via window message first
    System::Call 'user32::FindWindowW(i 0, w "Redball") i .r1'
    ${If} $1 != 0
        DetailPrint "Found Redball window, sending close message..."
        System::Call 'user32::PostMessageW(i r1, i 16, i 0, i 0) i .r0'
        Sleep 2000
    ${EndIf}
    
    ; Force kill if still running
    nsExec::Exec 'taskkill /F /IM Redball.UI.WPF.exe /T 2>nul'
    nsExec::Exec 'taskkill /F /IM Redball.Service.exe /T 2>nul'
    Sleep 2000
    
    ; Remove files
    DetailPrint "Removing files..."
    Delete "$INSTDIR\Redball.UI.WPF.exe"
    Delete "$INSTDIR\Redball.UI.WPF.dll"
    Delete "$INSTDIR\Redball.UI.WPF.deps.json"
    Delete "$INSTDIR\Redball.UI.WPF.runtimeconfig.json"
    Delete "$INSTDIR\Redball.Service.exe"
    Delete "$INSTDIR\README.txt"
    Delete "$INSTDIR\uninstall.exe"
    
    ; Remove DLL folder
    RMDir /r "$INSTDIR\dll"
    
    ; Remove Assets folder
    RMDir /r "$INSTDIR\Assets"
    
    ; Remove logs folder
    RMDir /r "$INSTDIR\logs"
    
    ; Remove installation directory
    RMDir "$INSTDIR"
    
    ; Remove registry keys
    DeleteRegKey HKCU "${PRODUCT_DIR_REGKEY}"
    DeleteRegKey HKCU "${PRODUCT_REGISTRY_KEY}"
    DeleteRegKey HKCU "${PRODUCT_UNINST_KEY}"
    
    DetailPrint "${PRODUCT_NAME} has been removed"
SectionEnd

; ============================================================================
; Check if Redball is running and offer to kill it
; ============================================================================
Function CheckAndKillRedball
    Push $R0
    Push $R1
    
retry_check:
    StrCpy $R0 0
    
    ; Check by process enumeration (most reliable - only checks for actual app process)
    nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq Redball.UI.WPF.exe" /NH 2>nul'
    Pop $0
    Pop $1
    ${If} $0 == 0
        ${If} $1 != ""
            ; Check that it's not just empty/no process found output
            nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq Redball.UI.WPF.exe" /NH /FO CSV 2>nul'
            Pop $0
            Pop $1
            ${If} $1 != ""
                StrCpy $R0 1
            ${EndIf}
        ${EndIf}
    ${EndIf}
    
    ; Secondary check: window class (WPF app windows)
    ${If} $R0 == 0
        System::Call 'user32::FindWindowW(w "HwndWrapper*[DefaultGray]*", i 0) i .r0'
        ${If} $0 != 0
            StrCpy $R0 1
        ${EndIf}
    ${EndIf}
    
    ; Check by file lock (only if installed)
    ${If} $R0 == 0
        ${If} ${FileExists} "$INSTDIR\Redball.UI.WPF.exe"
            Rename "$INSTDIR\Redball.UI.WPF.exe" "$INSTDIR\Redball.UI.WPF.exe.check"
            ${If} ${Errors}
                ClearErrors
                StrCpy $R0 1
            ${Else}
                Rename "$INSTDIR\Redball.UI.WPF.exe.check" "$INSTDIR\Redball.UI.WPF.exe"
            ${EndIf}
        ${EndIf}
    ${EndIf}
    
    ; If running, ask user what to do
    ${If} $R0 != 0
        MessageBox MB_YESNOCANCEL|MB_ICONQUESTION "${PRODUCT_NAME} is currently running. Would you like to close it and continue with installation? (Yes=Close, No=Continue, Cancel=Abort)" /SD IDCANCEL IDYES kill_process IDNO continue_install
        Goto abort_install
        
kill_process:
            DetailPrint "Attempting to close ${PRODUCT_NAME}..."
            ; Try graceful close first (find by window title)
            System::Call 'user32::FindWindowW(i 0, w "${PRODUCT_NAME}") i .r1'
            ${If} $1 != 0
                DetailPrint "Found ${PRODUCT_NAME} window, sending close message..."
                System::Call 'user32::PostMessageW(i r1, i 16, i 0, i 0) i .r0' ; WM_CLOSE = 16
                Sleep 2000
            ${EndIf}
            
            ; Also try to find and close Settings window
            System::Call 'user32::FindWindowW(i 0, w "${PRODUCT_NAME} Settings") i .r1'
            ${If} $1 != 0
                System::Call 'user32::PostMessageW(i r1, i 16, i 0, i 0) i .r0'
                Sleep 1000
            ${EndIf}
            
            ; Force kill if still running
            nsExec::Exec 'taskkill /F /IM Redball.UI.WPF.exe /T 2>nul'
            nsExec::Exec 'taskkill /F /IM Redball.Service.exe /T 2>nul'
            Sleep 2000
            
            ; Check again
            Goto retry_check
            
continue_install:
            DetailPrint "Continuing without closing ${PRODUCT_NAME} (may fail if files locked)"
            Goto done
            
abort_install:
            DetailPrint "Installation aborted by user"
            Abort
    ${EndIf}
    
done:
    DetailPrint "Process check complete - ${PRODUCT_NAME} is not running"
    Pop $R1
    Pop $R0
FunctionEnd

; ============================================================================
; Kill All Project EXEs Function (legacy, used by uninstaller)
; ============================================================================
Function KillAllRedballProcesses
    DetailPrint "Stopping Redball processes..."
    
    ; Try graceful close first (by window title)
    System::Call 'user32::FindWindowW(i 0, w "${PRODUCT_NAME}") i .r1'
    ${If} $1 != 0
        DetailPrint "Found ${PRODUCT_NAME} window, sending close..."
        System::Call 'user32::PostMessageW(i r1, i 16, i 0, i 0) i .r0' ; WM_CLOSE = 16
        Sleep 1500
    ${EndIf}
    
    ; Try closing Settings window if open
    System::Call 'user32::FindWindowW(i 0, w "${PRODUCT_NAME} Settings") i .r1'
    ${If} $1 != 0
        System::Call 'user32::PostMessageW(i r1, i 16, i 0, i 0) i .r0'
        Sleep 1000
    ${EndIf}
    
    ; Force kill
    nsExec::Exec 'taskkill /F /IM Redball.UI.WPF.exe /T 2>nul'
    nsExec::Exec 'taskkill /F /IM Redball.Service.exe /T 2>nul'
    Sleep 2000
    
    DetailPrint "Processes stopped"
FunctionEnd

Function .onInit
    ; Initialize variables
    StrCpy $DotNetInstalled 0
    StrCpy $DotNetDownloaded 0
    StrCpy $ServiceInstalled 0
    
    ; Check if installer is already running
    System::Call 'kernel32::CreateMutexW(i 0, i 0, w "${PRODUCT_NAME}Setup") i .r0'
    ${If} $0 == 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "${PRODUCT_NAME} installer is already running."
        Abort
    ${EndIf}
    
    ; Check if Redball is running and offer to kill it
    Call CheckAndKillRedball
    
    ; Check .NET runtime status
    Call CheckDotNet
    
    ; Extract version from command line if provided
    ${GetParameters} $R0
    ClearErrors
    ${GetOptions} $R0 "/VERSION=" $R1
    ${IfNot} ${Errors}
        ; Override version
    ${EndIf}
    
    ; Set installation directory from registry if exists
    ReadRegStr $0 HKCU "${PRODUCT_REGISTRY_KEY}" ""
    ${If} $0 != ""
        StrCpy $INSTDIR $0
    ${EndIf}
FunctionEnd

Function .onInstSuccess
    ${If} ${Silent}
        ; Silent install - auto-launch if requested
        ReadRegDWORD $0 HKCU "${PRODUCT_REGISTRY_KEY}" "SilentLaunch"
        ${If} $0 == 1
            Exec "$INSTDIR\Redball.UI.WPF.exe"
        ${EndIf}
    ${EndIf}
FunctionEnd

Function .onInstFailed
    MessageBox MB_OK|MB_ICONSTOP "Installation failed. Please check the log and try again."
FunctionEnd

Function un.onInit
    ; Verify uninstall
    MessageBox MB_OKCANCEL|MB_ICONQUESTION "Are you sure you want to remove ${PRODUCT_NAME}? This will delete all program files and settings." IDOK continue IDCANCEL cancel
    cancel:
        Abort
    continue:
    
    ; Stop service first if installed
    ReadRegDWORD $0 HKCU "${PRODUCT_REGISTRY_KEY}" "ServiceInstalled"
    ${If} $0 == 1
        DetailPrint "Stopping background service..."
        nsExec::Exec 'sc stop "Redball Input Service"'
        Sleep 3000
        nsExec::Exec 'sc delete "Redball Input Service"'
        Sleep 1000
    ${EndIf}
    
    ; Kill all Redball processes during uninstall (with graceful close first)
    Call un.KillAllRedballProcesses
FunctionEnd

; Uninstaller process kill function
Function un.KillAllRedballProcesses
    DetailPrint "Stopping Redball processes..."
    
    ; Try graceful close first (by window title)
    System::Call 'user32::FindWindowW(i 0, w "${PRODUCT_NAME}") i .r1'
    ${If} $1 != 0
        DetailPrint "Found ${PRODUCT_NAME} window, sending close..."
        System::Call 'user32::PostMessageW(i r1, i 16, i 0, i 0) i .r0' ; WM_CLOSE = 16
        Sleep 1500
    ${EndIf}
    
    ; Try closing Settings window if open
    System::Call 'user32::FindWindowW(i 0, w "${PRODUCT_NAME} Settings") i .r1'
    ${If} $1 != 0
        System::Call 'user32::PostMessageW(i r1, i 16, i 0, i 0) i .r0'
        Sleep 1000
    ${EndIf}
    
    ; Force kill if still running
    nsExec::Exec 'taskkill /F /IM Redball.UI.WPF.exe /T 2>nul'
    nsExec::Exec 'taskkill /F /IM Redball.Service.exe /T 2>nul'
    Sleep 2000
    
    DetailPrint "Processes stopped"
FunctionEnd

; ============================================================================
; Component Descriptions
; ============================================================================

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDotNet} $(DESC_SecDotNet)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} $(DESC_SecApp)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecService} $(DESC_SecService)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenu} $(DESC_SecStartMenu)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_SecDesktop)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} $(DESC_SecStartup)
!insertmacro MUI_FUNCTION_DESCRIPTION_END
