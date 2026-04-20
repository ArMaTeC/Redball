; ============================================================================
; Redball Modern NSIS Installer
; ============================================================================
; A feature-rich, customizable installer for Redball Windows application
; Built for NSIS 3.11+ with Modern UI 2
;
; Features:
;   - Modern branded welcome/finish pages
;;   - Custom component selection
;   - Auto-start with Windows option
;   - Desktop & Start Menu shortcuts
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
!include "WordFunc.nsh"

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
!define MUI_HEADERIMAGE_BITMAP "${PROJECT_ROOT}\installer\nsis-header.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${PROJECT_ROOT}\installer\nsis-welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${PROJECT_ROOT}\installer\nsis-welcome.bmp"

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
!insertmacro MUI_PAGE_LICENSE "${PROJECT_ROOT}\LICENSE"
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
LangString DESC_SecStartMenu ${LANG_ENGLISH} "Add Redball to your Start Menu"
LangString DESC_SecDesktop ${LANG_ENGLISH} "Add Redball shortcut to your Desktop"
LangString DESC_SecStartup ${LANG_ENGLISH} "Start Redball automatically when Windows starts"

; ============================================================================
; Variables
; ============================================================================

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
    File "${PROJECT_ROOT}\dist\wpf-publish\${DOTNET_INSTALLER}"

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
    File "${PROJECT_ROOT}\dist\wpf-publish\Redball.UI.WPF.exe"
    File "${PROJECT_ROOT}\dist\wpf-publish\Redball.UI.WPF.dll"
    File "${PROJECT_ROOT}\dist\wpf-publish\Redball.UI.WPF.deps.json"
    File "${PROJECT_ROOT}\dist\wpf-publish\Redball.UI.WPF.runtimeconfig.json"
    File "Redball.json"

    ; Copy DLL folder (contains dependency assemblies resolved via dll/ subfolder)
    SetOutPath "$INSTDIR\dll"
    File /r "${PROJECT_ROOT}\dist\wpf-publish\dll\*.*"
    SetOutPath "$INSTDIR"

    ; Copy Assets folder (animations, icons, themes)
    SetOutPath "$INSTDIR\Assets"
    File /r "Assets\*.*"
    SetOutPath "$INSTDIR"

    ; Create logs directory
    CreateDirectory "$INSTDIR\logs"

    ; Create comprehensive README
    FileOpen $0 "$INSTDIR\README.txt" w
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  ${PRODUCT_NAME} ${PRODUCT_VERSION_SHORT} - Keep Your Windows PC Awake$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "Thank you for installing ${PRODUCT_NAME}! This document will help you get$\r$\n"
    FileWrite $0 "started with the application and discover all its features.$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  WHAT IS REDBALL?$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "Redball is a modern, lightweight keep-awake utility for Windows that prevents$\r$\n"
    FileWrite $0 "your screen from locking, sleeping, or going idle when you need your PC to$\r$\n"
    FileWrite $0 "stay active. Perfect for presentations, long downloads, video rendering,$\r$\n"
    FileWrite $0 "or any task that requires uninterrupted PC activity.$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  QUICK START$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "1. Launch Redball:$\r$\n"
    FileWrite $0 "   - From the Start Menu: Start > Redball$\r$\n"
    FileWrite $0 "   - From Desktop: Double-click the Redball icon$\r$\n"
    FileWrite $0 "   - The app starts minimised to your system tray (notification area)$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "2. Control Redball:$\r$\n"
    FileWrite $0 "   - Left-click the tray icon to open the main window$\r$\n"
    FileWrite $0 "   - Right-click the tray icon for quick access menu$\r$\n"
    FileWrite $0 "   - Use the toggle switch to enable/disable Keep Awake$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "3. The Keep Awake feature is active when the status shows 'Active'$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  KEY FEATURES$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Keep-Alive Engine$\r$\n"
    FileWrite $0 "  -----------------$\r$\n"
    FileWrite $0 "  - Prevents screen lock and sleep automatically$\r$\n"
    FileWrite $0 "  - Optional display sleep prevention$\r$\n"
    FileWrite $0 "  - Configurable F15 heartbeat for undetectable activity$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  TypeThing Clipboard Typing$\r$\n"
    FileWrite $0 "  ---------------------------$\r$\n"
    FileWrite $0 "  - Paste text as simulated keystrokes$\r$\n"
    FileWrite $0 "  - Bypasses clipboard restrictions in remote sessions$\r$\n"
    FileWrite $0 "  - Perfect for secure environments (RDP, Citrix, VMware)$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Session Timer$\r$\n"
    FileWrite $0 "  --------------$\r$\n"
    FileWrite $0 "  - Track how long your session stays active$\r$\n"
    FileWrite $0 "  - Set automatic shut-off timers$\r$\n"
    FileWrite $0 "  - Get notifications when timer expires$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Smart Power Management$\r$\n"
    FileWrite $0 "  ----------------------$\r$\n"
    FileWrite $0 "  - Temperature monitoring with thermal protection$\r$\n"
    FileWrite $0 "  - CPU usage tracking$\r$\n"
    FileWrite $0 "  - Automatic power plan switching$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Beautiful Themes$\r$\n"
    FileWrite $0 "  ----------------$\r$\n"
    FileWrite $0 "  - 14 custom themes: Dark, Light, Midnight Blue, Forest Green,$\r$\n"
    FileWrite $0 "    Ocean Blue, Sunset Orange, Royal Purple, Slate Grey, Rose Gold,$\r$\n"
    FileWrite $0 "    Cyberpunk, Coffee, and Arctic Frost$\r$\n"
    FileWrite $0 "  - System theme detection$\r$\n"
    FileWrite $0 "  - Mica material effects on Windows 11$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Advanced Features$\r$\n"
    FileWrite $0 "  -----------------$\r$\n"
    FileWrite $0 "  - Mini Widget: Floating status window with drag positioning$\r$\n"
    FileWrite $0 "  - Smart Detection: Pause when battery is low or focus assist is on$\r$\n"
    FileWrite $0 "  - Auto-Updates: Delta patches for fast, efficient updates$\r$\n"
    FileWrite $0 "  - Windows 11 Widgets: System tray integration$\r$\n"
    FileWrite $0 "  - Plugin System: Extend functionality with custom plugins$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  KEYBOARD SHORTCUTS$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Ctrl + Shift + V    Activate TypeThing (type clipboard as keystrokes)$\r$\n"
    FileWrite $0 "  Ctrl + Shift + R    Refresh/redock the Mini Widget$\r$\n"
    FileWrite $0 "  Ctrl + Shift + P    Pause/Resume Keep Awake$\r$\n"
    FileWrite $0 "  Ctrl + Shift + O    Open main window$\r$\n"
    FileWrite $0 "  Ctrl + Shift + T    Toggle Mini Widget visibility$\r$\n"
    FileWrite $0 "  Ctrl + Shift + Q    Quick exit application$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Note: Shortcuts work even when the main window is closed/minimised.$\r$\n"
    FileWrite $0 "        You can customise shortcuts in Settings > Hotkeys.$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  SYSTEM REQUIREMENTS$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Operating System:   Windows 10 (1809+) or Windows 11$\r$\n"
    FileWrite $0 "  Architecture:       64-bit (x64)$\r$\n"
    FileWrite $0 "  .NET Runtime:       .NET 10 Desktop Runtime (included in installer)$\r$\n"
    FileWrite $0 "  Memory:             100 MB RAM minimum$\r$\n"
    FileWrite $0 "  Disk Space:         50 MB free space$\r$\n"
    FileWrite $0 "  Display:            Any display supported by Windows$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Optional Features:$\r$\n"
    FileWrite $0 "  - Background Service: Requires administrator rights to install$\r$\n"
    FileWrite $0 "  - Windows 11 Widgets: Requires Windows 11 22H2 or later$\r$\n"
    FileWrite $0 "  - Mica Effects:       Requires Windows 11 22H2 or later$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  GETTING HELP$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Documentation:      ${PRODUCT_WEB_SITE}/wiki$\r$\n"
    FileWrite $0 "  Report Issues:      ${PRODUCT_WEB_SITE}/issues$\r$\n"
    FileWrite $0 "  Releases:           ${PRODUCT_WEB_SITE}/releases$\r$\n"
    FileWrite $0 "  Discussions:        ${PRODUCT_WEB_SITE}/discussions$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  TROUBLESHOOTING$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  App won't start?$\r$\n"
    FileWrite $0 "  - Check logs in: %LOCALAPPDATA%\Redball\logs\$\r$\n"
    FileWrite $0 "  - Verify .NET 10 Runtime is installed$\r$\n"
    FileWrite $0 "  - Try running as administrator (for service features)$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Keep Awake not working?$\r$\n"
    FileWrite $0 "  - Check if Windows Focus Assist is blocking notifications$\r$\n"
    FileWrite $0 "  - Verify 'Prevent Display Sleep' is enabled if needed$\r$\n"
    FileWrite $0 "  - Some corporate policies may override keep-awake$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  TypeThing not working?$\r$\n"
    FileWrite $0 "  - Some applications block simulated input$\r$\n"
    FileWrite $0 "  - Try the background service option for elevated scenarios$\r$\n"
    FileWrite $0 "  - Ensure the target window has input focus$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  High CPU or memory usage?$\r$\n"
    FileWrite $0 "  - Check for update availability (updates fix known issues)$\r$\n"
    FileWrite $0 "  - Disable unused features in Settings$\r$\n"
    FileWrite $0 "  - Restart the application$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  UNINSTALLING REDBALL$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  1. Open Windows Settings > Apps > Installed apps$\r$\n"
    FileWrite $0 "  2. Find 'Redball' in the list$\r$\n"
    FileWrite $0 "  3. Click the three dots and select 'Uninstall'$\r$\n"
    FileWrite $0 "  4. Follow the prompts to complete removal$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Or use the Start Menu shortcut:$\r$\n"
    FileWrite $0 "  Start > Redball > Uninstall Redball$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "  Note: Your settings and logs are preserved in:$\r$\n"
    FileWrite $0 "        %LOCALAPPDATA%\Redball\ (delete manually if desired)$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  PRIVACY & DATA COLLECTION$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "Redball respects your privacy:$\r$\n"
    FileWrite $0 "- No telemetry or analytics data is collected$\r$\n"
    FileWrite $0 "- No internet connection required (except for update checks)$\r$\n"
    FileWrite $0 "- All configuration stored locally on your PC$\r$\n"
    FileWrite $0 "- TypeThing clipboard data is never stored or transmitted$\r$\n"
    FileWrite $0 "- Optional diagnostics only sent when you manually report an issue$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  LICENCE$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "Redball is open source software licensed under the MIT Licence.$\r$\n"
    FileWrite $0 "See the LICENSE file included with this distribution, or visit:$\r$\n"
    FileWrite $0 "${PRODUCT_WEB_SITE}/blob/main/LICENSE$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  THANK YOU!$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "Thank you for choosing Redball! We hope it helps you stay productive.$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "If you find Redball useful, please consider starring the project on GitHub$\r$\n"
    FileWrite $0 "and sharing it with others who might benefit.$\r$\n"
    FileWrite $0 "\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
    FileWrite $0 "  ${PRODUCT_PUBLISHER} | ${PRODUCT_WEB_SITE}$\r$\n"
    FileWrite $0 "================================================================================$\r$\n"
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

    ; Stop running process more aggressively
    DetailPrint "Stopping ${PRODUCT_NAME}..."

    ; 1. Try graceful close via window message first
    System::Call 'user32::FindWindowW(i 0, w "Redball") i .r1'
    ${If} $1 != 0
        System::Call 'user32::PostMessageW(i r1, i 16, i 0, i 0) i .r0'
        Sleep 1000
    ${EndIf}

    ; Force kill binaries
    nsExec::Exec 'taskkill /F /IM Redball.UI.WPF.exe /T'
    Sleep 2000

    ; Remove files
    DetailPrint "Removing files..."
    Delete "$INSTDIR\Redball.UI.WPF.exe"
    Delete "$INSTDIR\Redball.UI.WPF.dll"
    Delete "$INSTDIR\Redball.UI.WPF.deps.json"
    Delete "$INSTDIR\Redball.UI.WPF.runtimeconfig.json"
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

    StrCpy $R0 0

    ; Primary check: use tasklist CSV output and look for the actual EXE name in the output.
    ; tasklist always outputs text — "INFO: No tasks..." when empty — so we must search
    ; for the process name string, not just test for non-empty output.
    nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq Redball.UI.WPF.exe" /FO CSV /NH'
    Pop $0  ; exit code
    Pop $1  ; stdout
    ; $1 will contain "Redball.UI.WPF.exe" only if the process is actually running
    ${WordFind} "$1" "Redball.UI.WPF.exe" "E+1{" $2
    IfErrors 0 +2
        Goto wpf_not_found
    StrCpy $R0 1
wpf_not_found:
    ClearErrors

    ; Secondary check: find window by exact title "Redball" (main window)
    ${If} $R0 == 0
        System::Call 'user32::FindWindowW(i 0, w "Redball") i .r0'
        ${If} $0 != 0
            StrCpy $R0 1
        ${EndIf}
    ${EndIf}

    ; Tertiary check: file lock on the EXE (only if previously installed)
    ${If} $R0 == 0
        ${If} ${FileExists} "$INSTDIR\Redball.UI.WPF.exe"
            ClearErrors
            Rename "$INSTDIR\Redball.UI.WPF.exe" "$INSTDIR\Redball.UI.WPF.exe.check"
            ${If} ${Errors}
                ClearErrors
                StrCpy $R0 1
            ${Else}
                Rename "$INSTDIR\Redball.UI.WPF.exe.check" "$INSTDIR\Redball.UI.WPF.exe"
            ${EndIf}
        ${EndIf}
    ${EndIf}

    ; If running, ask user what to do (Silent Default: IDYES to ensure updates proceed)
    ${If} $R0 != 0
        MessageBox MB_YESNOCANCEL|MB_ICONQUESTION "${PRODUCT_NAME} is currently running. Would you like to close it and continue with installation? (Yes=Close, No=Continue, Cancel=Abort)" /SD IDYES IDYES kill_process IDNO continue_install
        Goto abort_install

kill_process:
            DetailPrint "Attempting to close ${PRODUCT_NAME}..."

            ; Try graceful close using taskkill without /F (sends WM_CLOSE)
            DetailPrint "Requesting graceful shutdown..."
            nsExec::Exec 'taskkill /IM Redball.UI.WPF.exe /T'
            Sleep 2000

            ; Use System call to close window by title as backup
            System::Call 'user32::FindWindowW(i 0, w "Redball") i .r0'
            ${If} $0 != 0
                System::Call 'user32::PostMessageW(i r0, i 16, i 0, i 0) i .r1'
                Sleep 1000
            ${EndIf}

            ; Force kill if still running
            DetailPrint "Ensuring process is stopped (force kill)..."
            nsExec::Exec 'taskkill /F /IM Redball.UI.WPF.exe /T'
            Sleep 2000

            ; Final check loop (up to 3 times)
            StrCpy $R1 0
        final_check_loop:
            IntOp $R1 $R1 + 1
            ${If} $R1 > 3
                Goto process_failure
            ${EndIf}

            ; Check WPF process
            nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq Redball.UI.WPF.exe" /FO CSV /NH'
            Pop $0
            Pop $1
            ${WordFind} "$1" "Redball.UI.WPF.exe" "E+1{" $2
            IfErrors +3
                DetailPrint "WPF process still detected, retrying force kill ($R1)..."
                nsExec::Exec 'taskkill /F /IM Redball.UI.WPF.exe /T'
                Sleep 1000
                Goto final_check_loop
            ClearErrors

            Goto process_killed

        process_failure:
            ; Processes did not stop cleanly — do one last force kill and proceed rather
            ; than prompting the user with a retry dialog.
            DetailPrint "Force killing ${PRODUCT_NAME} and continuing..."
            nsExec::Exec 'taskkill /F /IM Redball.UI.WPF.exe /T'
            Sleep 1500

        process_killed:
            DetailPrint "${PRODUCT_NAME} stopped successfully"
            Goto done

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

; KillAllRedballProcesses removed — only the un. variant is needed (see un.KillAllRedballProcesses below)

Function .onInit
    ; Initialize variables
    StrCpy $DotNetInstalled 0
    StrCpy $DotNetDownloaded 0

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

    ; Kill all Redball processes during uninstall (with graceful close first)
    Call un.KillAllRedballProcesses
FunctionEnd

; Uninstaller process kill function
Function un.KillAllRedballProcesses
    DetailPrint "Stopping Redball processes..."

    ; Try graceful close first (taskkill without /F sends WM_CLOSE)
    DetailPrint "Requesting graceful shutdown..."
    nsExec::Exec 'taskkill /IM Redball.UI.WPF.exe /T'
    Sleep 3000

    ; Force kill if still running
    nsExec::Exec 'taskkill /F /IM Redball.UI.WPF.exe /T'
    Sleep 2000

    DetailPrint "Processes stopped"
FunctionEnd

; ============================================================================
; Component Descriptions
; ============================================================================

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDotNet} $(DESC_SecDotNet)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} $(DESC_SecApp)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenu} $(DESC_SecStartMenu)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_SecDesktop)
    !insertmacro MUI_DESCRIPTION_TEXT ${SecStartup} $(DESC_SecStartup)
!insertmacro MUI_FUNCTION_DESCRIPTION_END
