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
!insertmacro MUI_PAGE_LICENSE "LICENSE.txt"
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

Var RunningProcess
Var ServiceInstalled

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
    FileWrite $0 "${PRODUCT_NAME} ${PRODUCT_VERSION_SHORT}$
$
"
    FileWrite $0 "=========================$
$
"
    FileWrite $0 "$
$
"
    FileWrite $0 "Thank you for installing ${PRODUCT_NAME}!$
$
"
    FileWrite $0 "$
$
"
    FileWrite $0 "Quick Start:$
$
"
    FileWrite $0 "1. Launch ${PRODUCT_NAME} from Start Menu or Desktop$
$
"
    FileWrite $0 "2. Right-click the system tray icon to access settings$
$
"
    FileWrite $0 "3. Enable 'Keep Awake' to prevent Windows sleep$
$
"
    FileWrite $0 "4. Use Ctrl+Shift+V for TypeThing clipboard typing$
$
"
    FileWrite $0 "$
$
"
    FileWrite $0 "For more information, visit:$
$
"
    FileWrite $0 "${PRODUCT_WEB_SITE}$
$
"
    FileClose $0
    
    ; Register application
    WriteRegStr HKCU "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\Redball.UI.WPF.exe"
    WriteRegStr HKCU "${PRODUCT_REGISTRY_KEY}" "" "$INSTDIR"
    WriteRegStr HKCU "${PRODUCT_REGISTRY_KEY}" "Version" "${PRODUCT_VERSION}"
    WriteRegStr HKCU "${PRODUCT_REGISTRY_KEY}" "InstallDate" "$INSTLOGTIME"
    
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
    
    ; Copy service files
    File "Redball.Service.exe"
    File "Redball.Service.dll"
    File "Redball.Service.runtimeconfig.json"
    
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
        nsExec::Exec 'sc stop RedballService'
        Sleep 2000
        nsExec::Exec 'sc delete RedballService'
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
    nsProcess::FindProcess "Redball.UI.WPF.exe"
    Pop $0
    ${If} $0 == 1
        nsProcess::KillProcess "Redball.UI.WPF.exe"
        Pop $0
        Sleep 1000
    ${EndIf}
    
    ; Remove files
    DetailPrint "Removing files..."
    Delete "$INSTDIR\Redball.UI.WPF.exe"
    Delete "$INSTDIR\Redball.UI.WPF.dll"
    Delete "$INSTDIR\Redball.UI.WPF.deps.json"
    Delete "$INSTDIR\Redball.UI.WPF.runtimeconfig.json"
    Delete "$INSTDIR\Redball.Service.exe"
    Delete "$INSTDIR\Redball.Service.dll"
    Delete "$INSTDIR\Redball.Service.runtimeconfig.json"
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
; Functions
; ============================================================================

Function .onInit
    ; Check if already running
    System::Call 'kernel32::CreateMutexW(i 0, i 0, w "${PRODUCT_NAME}Setup") i .r0'
    ${If} $0 == 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "${PRODUCT_NAME} installer is already running."
        Abort
    ${EndIf}
    
    ; Check if Redball is already running
    nsProcess::FindProcess "Redball.UI.WPF.exe"
    Pop $RunningProcess
    ${If} $RunningProcess == 1
        MessageBox MB_OKCANCEL|MB_ICONQUESTION "${PRODUCT_NAME} is currently running. Setup will close it to continue. Click OK to close ${PRODUCT_NAME} and continue, or Cancel to exit." IDOK continue IDCANCEL cancel
        cancel:
            Abort
        continue:
            nsProcess::KillProcess "Redball.UI.WPF.exe"
            Sleep 1500
    ${EndIf}
    
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
    
    StrCpy $ServiceInstalled 0
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
FunctionEnd

; ============================================================================
; Component Descriptions
; ============================================================================

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} $(DESC_SecApp)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecService} $(DESC_SecService)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenu} $(DESC_SecStartMenu)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_SecDesktop)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} $(DESC_SecStartup)
!insertmacro MUI_FUNCTION_DESCRIPTION_END
