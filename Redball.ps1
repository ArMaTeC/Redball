#requires -Version 5.1
<#requires -RunAsAdministrator#>
<#
# Copyright (c) 2024-2026 GCI Network Solutions / Redball Contributors
# Licensed under the MIT License. See LICENSE file in the project root.
# https://github.com/ArMaTeC/Redball
#>
<#
.SYNOPSIS
    Redball - A system tray utility to prevent Windows from going to sleep.
.DESCRIPTION
    Redball prevents your Windows computer from sleeping using the SetThreadExecutionState API.
    It provides a system tray icon for easy control and optional F15 keypress heartbeat.
.PARAMETER ConfigPath
    Path to configuration JSON file. Defaults to $PSScriptRoot\Redball.json
.PARAMETER Duration
    Run for a specific duration in minutes, then exit. Implies -Minimized.
.PARAMETER Minimized
    Start minimized to system tray (no initial icon visible).
.PARAMETER Install
    Install Redball to start with Windows.
.PARAMETER Uninstall
    Remove Redball from Windows startup.
.PARAMETER Status
    Return current status as JSON and exit (for automation).
.PARAMETER ExitOnComplete
    Exit after timed duration completes (use with -Duration).
.EXAMPLE
    .\Redball.ps1
    Starts Redball with default settings.
.EXAMPLE
    .\Redball.ps1 -Duration 60 -ExitOnComplete
    Keep awake for 60 minutes then exit automatically.
.EXAMPLE
    .\Redball.ps1 -Status | ConvertFrom-Json
    Get current Redball status programmatically.
.NOTES
    Version: 2.1.4
    Requires: PowerShell 5.1+, Windows 8.1+
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string]$ConfigPath = '',
    
    [Parameter()]
    [ValidateRange(1, 720)]
    [int]$Duration,
    
    [Parameter()]
    [switch]$Minimized,
    
    [Parameter()]
    [switch]$Install,
    
    [Parameter()]
    [switch]$Uninstall,
    
    [Parameter()]
    [switch]$Status,

    [Parameter()]
    [switch]$CheckUpdate,

    [Parameter()]
    [switch]$Update,

    [Parameter()]
    [switch]$SignScript,

    [Parameter()]
    [string]$SignPath = '',

    [Parameter()]
    [string]$CertThumbprint,

    [Parameter()]
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    
    [Parameter()]
    [switch]$ExitOnComplete,

    [Parameter()]
    [switch]$UseModernUI
)

$script:VERSION = '2.1.4'
$script:APP_NAME = 'Redball'
$script:IsPS7 = $PSVersionTable.PSVersion.Major -ge 7

# Resolve script root once - works in direct run, ps2exe EXE, and dot-source contexts
$script:AppRoot = if ($PSScriptRoot -and $PSScriptRoot -is [string] -and (Test-Path $PSScriptRoot)) {
    $PSScriptRoot
}
elseif ($MyInvocation.MyCommand.Path) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}
else {
    (Get-Location).Path
}

# Resolve paths that could not use if-expressions in param block defaults
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $script:AppRoot 'Redball.json'
}
if (-not $SignPath) {
    $SignPath = Join-Path $script:AppRoot 'Redball.ps1'
}

# Add Windows Runtime for toast notifications (Win10/11)
if ($script:IsPS7) {
    try {
        Add-Type -AssemblyName 'System.Runtime.WindowsRuntime' -ErrorAction SilentlyContinue
        $script:HasWinRT = $true
    }
    catch {
        $script:HasWinRT = $false
    }
}
else {
    $script:HasWinRT = $false
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$signature = @"
using System;
using System.Runtime.InteropServices;

namespace Win32 {
    public static class Power {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern uint SetThreadExecutionState(uint esFlags);
    }
}
"@

Add-Type -TypeDefinition $signature -Language CSharp

# Enforce TLS 1.2+ for all HTTPS requests (prevents TLS downgrade attacks)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

$script:ES_CONTINUOUS = [uint32]2147483648
$script:ES_SYSTEM_REQUIRED = [uint32]1
$script:ES_DISPLAY_REQUIRED = [uint32]2

# Named constants for global hotkey IDs
$script:HOTKEY_ID_TYPETHING_START = 100
$script:HOTKEY_ID_TYPETHING_STOP = 101

# NOTE: PreventDisplaySleep, UseHeartbeatKeypress, HeartbeatSeconds, BatteryAware,
# BatteryThreshold, NetworkAware, IdleDetection are duplicated in both $script:state
# and $script:config. Config is the persisted source; state is synced on load/save.
# Future refactor: designate $script:config as single source of truth and remove
# duplicate keys from $script:state.
$script:state = [ordered]@{
    Active                      = $true
    PreventDisplaySleep         = $true
    UseHeartbeatKeypress        = $true
    HeartbeatSeconds            = 59
    Until                       = $null
    NotifyIcon                  = $null
    Context                     = $null
    HeartbeatTimer              = $null
    DurationTimer               = $null
    ToggleMenuItem              = $null
    DisplayMenuItem             = $null
    HeartbeatMenuItem           = $null
    StatusMenuItem              = $null
    BatteryMenuItem             = $null
    StartupMenuItem             = $null
    PreviousIcon                = $null
    IsShuttingDown              = $false
    LastStatusText              = ''
    LastIconState               = ''
    UiUpdatePending             = $false
    BatteryAware                = $false
    BatteryThreshold            = 20
    OnBattery                   = $false
    AutoPausedBattery           = $false
    NetworkAware                = $false
    AutoPausedNetwork           = $false
    ActiveBeforeNetwork         = $false
    IdleDetection               = $false
    IdleThresholdMinutes        = 30
    AutoPausedIdle              = $false
    AutoPausedPresentation      = $false
    AutoPausedSchedule          = $false
    ManualOverride              = $false
    StartTime                   = $null
    ActiveBeforeBattery         = $false
    ActiveBeforeIdle            = $false
    SessionId                   = [guid]::NewGuid().ToString()
    KeepAwakeRunspaceInfo       = $null
    # TypeThing - Clipboard Typer state
    TypeThingIsTyping           = $false
    TypeThingShouldStop         = $false
    TypeThingText               = ''
    TypeThingIndex              = 0
    TypeThingTotalChars         = 0
    TypeThingStartTime          = $null
    TypeThingTimer              = $null
    TypeThingCountdown          = $null
    TypeThingCountdownRemaining = 0
    TypeThingMenuType           = $null
    TypeThingMenuStop           = $null
    TypeThingMenuStatus         = $null
    TypeThingHotkeyWindow       = $null
    TypeThingHotkeyStartId      = $script:HOTKEY_ID_TYPETHING_START
    TypeThingHotkeyStopId       = $script:HOTKEY_ID_TYPETHING_STOP
    TypeThingHotkeysRegistered  = $false
}

$_detectedLocale = try { (Get-Culture).TwoLetterISOLanguageName } catch { 'en' }

$script:config = @{
    HeartbeatSeconds           = 59
    PreventDisplaySleep        = $true
    UseHeartbeatKeypress       = $true
    DefaultDuration            = 60
    LogPath                    = (Join-Path $script:AppRoot 'Redball.log')
    MaxLogSizeMB               = 10
    ShowBalloonOnStart         = $true
    Locale                     = $_detectedLocale
    MinimizeOnStart            = $false
    BatteryAware               = $false
    BatteryThreshold           = 20
    NetworkAware               = $false
    IdleDetection              = $false
    AutoExitOnComplete         = $false
    ScheduleEnabled            = $false
    ScheduleStartTime          = '09:00'
    ScheduleStopTime           = '18:00'
    ScheduleDays               = @('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday')
    PresentationModeDetection  = $false
    EnablePerformanceMetrics   = $false
    EnableTelemetry            = $false
    ProcessIsolation           = $false
    UpdateRepoOwner            = 'karl-lawrence'
    UpdateRepoName             = 'Redball'
    UpdateChannel              = 'stable'
    VerifyUpdateSignature      = $false
    # TypeThing - Clipboard Typer settings
    TypeThingEnabled           = $true
    TypeThingMinDelayMs        = 30
    TypeThingMaxDelayMs        = 120
    TypeThingStartDelaySec     = 3
    TypeThingStartHotkey       = 'Ctrl+Shift+V'
    TypeThingStopHotkey        = 'Ctrl+Shift+X'
    TypeThingTheme             = 'dark'
    TypeThingAddRandomPauses   = $true
    TypeThingRandomPauseChance = 5
    TypeThingRandomPauseMaxMs  = 500
    TypeThingTypeNewlines      = $true
    TypeThingNotifications     = $true
    # Debug/Logging options
    VerboseLogging             = $false
}

$script:locales = @{}
$script:currentLocale = 'en'

# --- Instance Management (Singleton Pattern) ---

$script:singletonMutex = $null
$script:wshShell = $null
$script:lastBatteryCheck = $null
$script:lastBatteryResult = @{ HasBattery = $false }
$script:lastUpdateCheck = $null
$script:lastUpdateResult = $null

function Test-RedballInstanceRunning {
    <#
    .SYNOPSIS
        Checks if another instance of Redball is already running.
    .DESCRIPTION
        Uses a named mutex to detect if another Redball process is active.
        Returns $true if another instance is running, $false otherwise.
    #>
    try {
        # Try to create/open a named mutex - this is the proper singleton mechanism
        $mutex = [System.Threading.Mutex]::OpenExisting('Global\Redball_Singleton_Mutex')
        # If we can open it, another instance has it
        $mutex.Close()
        return $true
    }
    catch [System.Threading.WaitHandleCannotBeOpenedException] {
        # Mutex doesn't exist - no other instance running
        return $false
    }
    catch {
        # Any other error, assume no other instance for safety
        return $false
    }
}

function Initialize-RedballSingleton {
    <#
    .SYNOPSIS
        Creates the singleton mutex for this instance.
    .DESCRIPTION
        Creates a named mutex to mark this as the active Redball instance.
        Call this after confirming no other instance is running.
    #>
    try {
        $script:singletonMutex = New-Object System.Threading.Mutex($false, 'Global\Redball_Singleton_Mutex')
        # Acquire the mutex immediately
        if (-not $script:singletonMutex.WaitOne(0)) {
            # Someone else got it between our check and now
            $script:singletonMutex.Dispose()
            $script:singletonMutex = $null
            return $false
        }
        return $true
    }
    catch {
        Write-Verbose "Failed to create singleton mutex: $_"
        return $false
    }
}

function Get-RedballProcess {
    <#
    .SYNOPSIS
        Gets Redball PowerShell processes (excluding current process).
    .DESCRIPTION
        Finds other PowerShell processes running Redball.ps1.
    #>
    $currentPID = $PID
    Get-CimInstance -ClassName Win32_Process -Filter "Name = 'powershell.exe' OR Name = 'pwsh.exe'" | Where-Object {
        $_.ProcessId -ne $currentPID -and $_.CommandLine -like '*Redball.ps1*'
    } | ForEach-Object {
        [PSCustomObject]@{
            ProcessId   = $_.ProcessId
            Name        = $_.Name
            CommandLine = $_.CommandLine
            StartTime   = $_.CreationDate
        }
    }
}

function Stop-RedballProcess {
    <#
    .SYNOPSIS
        Gracefully stops a Redball process.
    .DESCRIPTION
        Attempts graceful shutdown first, then force kills if needed.
    .PARAMETER ProcessId
        The process ID to stop.
    .PARAMETER Force
        Force kill immediately without graceful attempt.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory)]
        [int]$ProcessId,
        [switch]$Force
    )
    
    if (-not $PSCmdlet.ShouldProcess("PID $ProcessId", 'Terminate Redball process')) {
        return $false
    }
    
    try {
        $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if (-not $process) {
            return $true  # Already gone
        }
        
        if (-not $Force) {
            # Try graceful shutdown first - look for the main window
            try {
                $process.CloseMainWindow() | Out-Null
                Start-Sleep -Milliseconds 500
                
                # Check if it exited
                $process.Refresh()
                if ($process.HasExited) {
                    return $true
                }
                
                # Give it a bit more time
                Start-Sleep -Milliseconds 1500
                $process.Refresh()
                if ($process.HasExited) {
                    return $true
                }
            }
            catch {
                # CloseMainWindow can fail for processes without windows
            }
        }
        
        # Force kill
        Stop-Process -Id $ProcessId -Force -ErrorAction Stop
        return $true
    }
    catch {
        Write-Warning "Failed to stop process $ProcessId`: $_"
        return $false
    }
}

function Clear-RedballLogLock {
    <#
    .SYNOPSIS
        Attempts to clear locks on the log file by stopping processes holding it.
    .DESCRIPTION
        Detects processes locking the log file and attempts to stop them.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()
    
    $logPath = $script:config.LogPath
    if (-not (Test-Path $logPath)) {
        return $true
    }
    
    try {
        # Try to open the file exclusively to see if it's locked
        $fileStream = [System.IO.File]::Open($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $fileStream.Close()
        $fileStream.Dispose()
        return $true  # Not locked
    }
    catch [System.IO.IOException] {
        # File is locked - try to find and stop Redball processes
        Write-Warning "Log file is locked. Attempting to clear locks..."
        
        if (-not $PSCmdlet.ShouldProcess('Redball processes', 'Stop to clear log file lock')) {
            return $false
        }
        
        $processes = Get-RedballProcess
        $stopped = 0
        
        foreach ($proc in $processes) {
            if (Stop-RedballProcess -ProcessId $proc.ProcessId) {
                $stopped++
            }
        }
        
        # Give processes time to exit and release handles
        Start-Sleep -Milliseconds 1000
        
        # Try again
        try {
            $fileStream = [System.IO.File]::Open($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $fileStream.Close()
            $fileStream.Dispose()
            Write-Output "Log file lock cleared ($stopped processes stopped)"
            return $true
        }
        catch {
            Write-Warning "Unable to clear log file lock. You may need to restart your computer or delete the log file manually."
            return $false
        }
    }
    catch {
        Write-Warning "Unexpected error checking log file: $_"
        return $false
    }
}

# --- Embedded Localization Data ---
$script:embeddedLocales = @'
{
  "en": {
    "AppName": "Redball",
    "TrayTooltipActive": "Redball (Active)",
    "TrayTooltipPaused": "Redball (Paused)",
    "MenuPause": "Pause Keep-Awake",
    "MenuResume": "Resume Keep-Awake",
    "MenuPreventDisplaySleep": "Prevent Display Sleep",
    "MenuUseF15Heartbeat": "Use F15 Heartbeat",
    "MenuStayAwakeFor": "Stay Awake For",
    "MenuStayAwakeIndefinitely": "Stay Awake Until Paused",
    "MenuExit": "Exit",
    "StatusActive": "Active",
    "StatusPaused": "Paused",
    "StatusDisplayOn": "Display On",
    "StatusDisplayNormal": "Display Normal",
    "StatusF15On": "F15 On",
    "StatusF15Off": "F15 Off",
    "StatusMinLeft": "min left",
    "StatusUnavailable": "Status unavailable",
    "BalloonStarted": "Redball started - keeping system awake",
    "BalloonPaused": "Redball paused",
    "BalloonResumed": "Redball resumed",
    "LogStarted": "Redball started",
    "LogPaused": "Redball paused",
    "LogResumed": "Redball resumed",
    "LogExited": "Redball exited",
    "ErrorIconCreate": "Failed to create icon",
    "ErrorUIUpdate": "UI update failed",
    "ErrorSetActiveState": "Failed to set active state",
    "ErrorSwitchState": "Failed to switch state",
    "ErrorTimedAwake": "Failed to start timed awake"
  },
  "es": {
    "AppName": "Redball",
    "TrayTooltipActive": "Redball (Activo)",
    "TrayTooltipPaused": "Redball (Pausado)",
    "MenuPause": "Pausar Mantener Despierto",
    "MenuResume": "Reanudar Mantener Despierto",
    "MenuPreventDisplaySleep": "Prevenir Suspensión de Pantalla",
    "MenuUseF15Heartbeat": "Usar Latido F15",
    "MenuStayAwakeFor": "Mantener Despierto Por",
    "MenuStayAwakeIndefinitely": "Mantener Despierto Hasta Pausar",
    "MenuExit": "Salir",
    "StatusActive": "Activo",
    "StatusPaused": "Pausado",
    "StatusDisplayOn": "Pantalla On",
    "StatusDisplayNormal": "Pantalla Normal",
    "StatusF15On": "F15 On",
    "StatusF15Off": "F15 Off",
    "StatusMinLeft": "min restantes",
    "StatusUnavailable": "Estado no disponible",
    "BalloonStarted": "Redball iniciado - manteniendo sistema despierto",
    "BalloonPaused": "Redball pausado",
    "BalloonResumed": "Redball reanudado",
    "LogStarted": "Redball iniciado",
    "LogPaused": "Redball pausado",
    "LogResumed": "Redball reanudado",
    "LogExited": "Redball cerrado",
    "ErrorIconCreate": "Error al crear icono",
    "ErrorUIUpdate": "Error al actualizar UI",
    "ErrorSetActiveState": "Error al establecer estado activo",
    "ErrorSwitchState": "Error al cambiar estado",
    "ErrorTimedAwake": "Error al iniciar temporizador"
  },
  "fr": {
    "AppName": "Redball",
    "TrayTooltipActive": "Redball (Actif)",
    "TrayTooltipPaused": "Redball (En pause)",
    "MenuPause": "Pause Garder Éveillé",
    "MenuResume": "Reprendre Garder Éveillé",
    "MenuPreventDisplaySleep": "Empêcher Veille Écran",
    "MenuUseF15Heartbeat": "Utiliser Battement F15",
    "MenuStayAwakeFor": "Rester Éveillé Pendant",
    "MenuStayAwakeIndefinitely": "Rester Éveillé Jusqu'à Pause",
    "MenuExit": "Quitter",
    "StatusActive": "Actif",
    "StatusPaused": "En Pause",
    "StatusDisplayOn": "Écran On",
    "StatusDisplayNormal": "Écran Normal",
    "StatusF15On": "F15 On",
    "StatusF15Off": "F15 Off",
    "StatusMinLeft": "min restantes",
    "StatusUnavailable": "Statut indisponible",
    "BalloonStarted": "Redball démarré - maintient le système éveillé",
    "BalloonPaused": "Redball en pause",
    "BalloonResumed": "Redball repris",
    "LogStarted": "Redball démarré",
    "LogPaused": "Redball en pause",
    "LogResumed": "Redball repris",
    "LogExited": "Redball fermé",
    "ErrorIconCreate": "Échec création icône",
    "ErrorUIUpdate": "Échec mise à jour UI",
    "ErrorSetActiveState": "Échec définition état actif",
    "ErrorSwitchState": "Échec changement état",
    "ErrorTimedAwake": "Échec démarrage minuterie"
  },
  "de": {
    "AppName": "Redball",
    "TrayTooltipActive": "Redball (Aktiv)",
    "TrayTooltipPaused": "Redball (Pausiert)",
    "MenuPause": "Pause Wachhalten",
    "MenuResume": "Wachhalten Fortsetzen",
    "MenuPreventDisplaySleep": "Bildschirmschutz Verhindern",
    "MenuUseF15Heartbeat": "F15-Tastendruck Nutzen",
    "MenuStayAwakeFor": "Wachhalten Für",
    "MenuStayAwakeIndefinitely": "Wachhalten Bis Pausiert",
    "MenuExit": "Beenden",
    "StatusActive": "Aktiv",
    "StatusPaused": "Pausiert",
    "StatusDisplayOn": "Bildschirm An",
    "StatusDisplayNormal": "Bildschirm Normal",
    "StatusF15On": "F15 An",
    "StatusF15Off": "F15 Aus",
    "StatusMinLeft": "min übrig",
    "StatusUnavailable": "Status nicht verfügbar",
    "BalloonStarted": "Redball gestartet - System wachgehalten",
    "BalloonPaused": "Redball pausiert",
    "BalloonResumed": "Redball fortgesetzt",
    "LogStarted": "Redball gestartet",
    "LogPaused": "Redball pausiert",
    "LogResumed": "Redball fortgesetzt",
    "LogExited": "Redball beendet",
    "ErrorIconCreate": "Symbolerstellung fehlgeschlagen",
    "ErrorUIUpdate": "UI-Aktualisierung fehlgeschlagen",
    "ErrorSetActiveState": "Aktivierungsstatus fehlgeschlagen",
    "ErrorSwitchState": "Statuswechsel fehlgeschlagen",
    "ErrorTimedAwake": "Timer-Start fehlgeschlagen"
  },
  "bl": {
    "AppName": "Redball",
    "TrayTooltipActive": "Redball (Jacked In)",
    "TrayTooltipPaused": "Redball (Offline)",
    "MenuPause": "Pull the Plug",
    "MenuResume": "Jack Back In",
    "MenuPreventDisplaySleep": "Keep The Lights On",
    "MenuUseF15Heartbeat": "Synth Pulse F15",
    "MenuStayAwakeFor": "Run For",
    "MenuStayAwakeIndefinitely": "Run Until Flatline",
    "MenuExit": "Kill Process",
    "StatusActive": "Online",
    "StatusPaused": "Offline",
    "StatusDisplayOn": "Lit Up",
    "StatusDisplayNormal": "Standard",
    "StatusF15On": "Pulse On",
    "StatusF15Off": "No Pulse",
    "StatusMinLeft": "ticks left",
    "StatusUnavailable": "Signal Lost",
    "BalloonStarted": "Redball jacked in - keeping the system lit",
    "BalloonPaused": "Redball pulled the plug",
    "BalloonResumed": "Redball back online",
    "LogStarted": "Redball initialized",
    "LogPaused": "Redball offline",
    "LogResumed": "Redball reconnected",
    "LogExited": "Redball terminated",
    "ErrorIconCreate": "Icon construct failed",
    "ErrorUIUpdate": "Interface glitch",
    "ErrorSetActiveState": "Failed to bring online",
    "ErrorSwitchState": "State transition error",
    "ErrorTimedAwake": "Timer protocol failed"
  }
}
'@

function Import-RedballLocales {
    param([string]$Path = '')
    if (-not $Path) { $Path = Join-Path $script:AppRoot 'locales.json' }
    
    # Helper function to convert PSCustomObject to hashtable (PS5.1 compatible)
    function ConvertTo-Hashtable {
        param($InputObject)
        if ($InputObject -is [System.Collections.Hashtable]) {
            return $InputObject
        }
        if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
            $hash = @{}
            $InputObject.PSObject.Properties | ForEach-Object {
                $hash[$_.Name] = ConvertTo-Hashtable -InputObject $_.Value
            }
            return $hash
        }
        if ($InputObject -is [System.Object[]]) {
            return @($InputObject | ForEach-Object { ConvertTo-Hashtable -InputObject $_ })
        }
        return $InputObject
    }
    
    try {
        # First load embedded locales as fallback
        $script:locales = ConvertTo-Hashtable -InputObject ($script:embeddedLocales | ConvertFrom-Json)
        
        # Try to load external file if it exists (allows user customization)
        if (Test-Path $Path) {
            $content = Get-Content $Path -Raw -Encoding UTF8
            $externalLocales = ConvertTo-Hashtable -InputObject ($content | ConvertFrom-Json)
            # Merge external locales with embedded (external takes precedence)
            foreach ($locale in $externalLocales.Keys) {
                $script:locales[$locale] = $externalLocales[$locale]
            }
            Write-RedballLog -Level 'INFO' -Message "Loaded external locales from: $Path"
        }
        
        # Validate system locale exists, fallback to English
        $systemLocale = try { (Get-Culture).TwoLetterISOLanguageName } catch { 'en' }
        if ($script:locales.ContainsKey($systemLocale)) {
            $script:currentLocale = $systemLocale
        }
        Write-RedballLog -Level 'INFO' -Message "Available locales: $($script:locales.Keys -join ', ')"
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to load locales: $_. Using embedded defaults."
        # Ensure embedded locales are loaded even on error
        if ($script:locales.Count -eq 0) {
            $script:locales = ConvertTo-Hashtable -InputObject ($script:embeddedLocales | ConvertFrom-Json)
        }
    }
}

function Get-LocalizedString {
    param(
        [Parameter(Mandatory)]
        [string]$Key,
        [string]$Locale = $script:currentLocale
    )
    try {
        if ($script:locales.ContainsKey($Locale) -and $script:locales[$Locale].ContainsKey($Key)) {
            return $script:locales[$Locale][$Key]
        }
        # Fallback to English
        if ($script:locales.ContainsKey('en') -and $script:locales['en'].ContainsKey($Key)) {
            return $script:locales['en'][$Key]
        }
        # Hardcoded English fallbacks for core strings
        $fallbacks = @{
            'StatusActive'        = 'Active'
            'StatusPaused'        = 'Paused'
            'StatusDisplayOn'     = 'Display On'
            'StatusDisplayNormal' = 'Display Normal'
            'StatusF15On'         = 'F15 On'
            'StatusF15Off'        = 'F15 Off'
            'StatusMinLeft'       = 'min left'
            'StatusUnavailable'   = 'Status Unavailable'
            'MenuPause'           = 'Pause Keep-Awake'
            'MenuResume'          = 'Resume Keep-Awake'
        }
        if ($fallbacks.ContainsKey($Key)) {
            return $fallbacks[$Key]
        }
        return $Key  # Return key name if translation not found
    }
    catch {
        return $Key
    }
}

function Write-RedballLog {
    <#
    .SYNOPSIS
        Writes a log entry to the Redball log file with retry logic for file locks.
    .DESCRIPTION
        Appends a timestamped log entry to the configured log file.
        Implements retry logic with exponential backoff to handle file locks from other instances.
        Falls back to alternate log path if primary is persistently locked.
    .PARAMETER Level
        Log level: INFO, WARN, ERROR, DEBUG
    .PARAMETER Message
        The message to log
    #>
    param(
        [Parameter(Mandatory)]
        [ValidateSet('INFO', 'WARN', 'ERROR', 'DEBUG')]
        [string]$Level,
        [Parameter(Mandatory)]
        [string]$Message
    )
    
    $maxRetries = 3
    $retryDelayMs = 100
    
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $logEntry = "[$timestamp] [$Level] $Message"
    
    # Ensure log directory exists
    try {
        $logDir = Split-Path $script:config.LogPath -Parent
        if (-not (Test-Path $logDir)) {
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        }
    }
    catch {
        # Can't create directory - will try fallback
    }
    
    # Try to write with retries
    for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
        try {
            # Check for log rotation every 50 writes to reduce filesystem overhead
            if (-not $script:logWriteCount) { $script:logWriteCount = 0 }
            $script:logWriteCount++
            if (($script:logWriteCount -eq 1 -or $script:logWriteCount % 50 -eq 0) -and (Test-Path $script:config.LogPath)) {
                $logSize = (Get-Item $script:config.LogPath).Length / 1MB
                if ($logSize -gt $script:config.MaxLogSizeMB) {
                    $backupPath = "$($script:config.LogPath).$(Get-Date -Format 'yyyyMMddHHmmss').bak"
                    Move-Item $script:config.LogPath $backupPath -Force
                }
            }
            
            # Use FileStream for better control over file locking
            $fileStream = [System.IO.File]::Open($script:config.LogPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
            try {
                $fileStream.Seek(0, [System.IO.SeekOrigin]::End) | Out-Null
                $writer = [System.IO.StreamWriter]::new($fileStream)
                try {
                    $writer.WriteLine($logEntry)
                    $writer.Flush()
                }
                finally {
                    $writer.Dispose()
                }
            }
            finally {
                $fileStream.Dispose()
            }
            return  # Success - exit function
        }
        catch [System.IO.IOException] {
            # File is locked - wait and retry
            if ($attempt -lt $maxRetries) {
                Start-Sleep -Milliseconds ($retryDelayMs * $attempt)
            }
        }
        catch {
            # Other error - break and try fallback
            break
        }
    }
    
    # All retries failed - try fallback to temp location
    try {
        $fallbackPath = Join-Path $env:TEMP 'Redball_fallback.log'
        Add-Content -Path $fallbackPath -Value "[FALLBACK from $($script:config.LogPath)] $logEntry" -ErrorAction Stop
    }
    catch {
        # Total failure - write to verbose stream as last resort
        Write-Verbose "CRITICAL: Log write failed completely. Entry: $logEntry"
    }
}

function Write-VerboseLog {
    <#
    .SYNOPSIS
        Writes verbose log messages to file and console when verbose logging is enabled.
    .DESCRIPTION
        Logs detailed diagnostic messages to a separate verbose log file and outputs
        to the verbose stream. Only logs when VerboseLogging is enabled in config.
        Includes source tagging for easier debugging.
    .PARAMETER Message
        The message to log.
    .PARAMETER Source
        Optional source identifier for the log entry (e.g., "Core", "TypeThing").
    #>
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        [string]$Source = 'General'
    )
    
    if (-not $script:config.VerboseLogging) { return }
    
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'
    $logEntry = "[$timestamp] [$Source] $Message"
    
    # Output to verbose stream
    Write-Verbose $logEntry
    
    # Write to verbose log file
    try {
        $verboseLogPath = Join-Path $script:AppRoot 'Redball.verbose.log'
        Add-Content -Path $verboseLogPath -Value $logEntry -ErrorAction SilentlyContinue
    }
    catch {
        # Silently fail - verbose logging is not critical
    }
}

function Set-KeepAwakeState {
    <#
    .SYNOPSIS
        Sets the Windows execution state to prevent or allow sleep.
    .DESCRIPTION
        Uses the SetThreadExecutionState Win32 API to control system sleep behavior.
        When enabled, prevents the system from sleeping and optionally prevents
        the display from turning off.
    .PARAMETER Enable
        $true to prevent sleep, $false to allow normal sleep behavior.
    #>
    param(
        [Parameter(Mandatory)]
        [bool]$Enable
    )
    
    try {
        if (-not ([System.Management.Automation.PSTypeName]'Win32.Power').Type) {
            $signature = @"
using System;
using System.Runtime.InteropServices;
namespace Win32 {
    public static class Power {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern uint SetThreadExecutionState(uint esFlags);
        
        public const uint ES_AWAYMODE_REQUIRED = 0x00000040;
        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_DISPLAY_REQUIRED = 0x00000002;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    }
}
"@
            Add-Type -TypeDefinition $signature -Language CSharp -ErrorAction SilentlyContinue
        }
        
        if ($Enable) {
            $flags = [Win32.Power]::ES_CONTINUOUS -bor [Win32.Power]::ES_SYSTEM_REQUIRED
            if ($script:state.PreventDisplaySleep) {
                $flags = $flags -bor [Win32.Power]::ES_DISPLAY_REQUIRED
            }
            [Win32.Power]::SetThreadExecutionState($flags) | Out-Null
            Write-VerboseLog -Message "Keep-awake state enabled (flags: 0x$($flags.ToString('X8')))" -Source "Power"
        }
        else {
            $flags = [Win32.Power]::ES_CONTINUOUS
            [Win32.Power]::SetThreadExecutionState($flags) | Out-Null
            Write-VerboseLog -Message "Keep-awake state disabled" -Source "Power"
        }
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to set keep-awake state: $_"
    }
}

function Send-HeartbeatKey {
    <#
    .SYNOPSIS
        Sends an F15 keypress to prevent idle detection.
    .DESCRIPTION
        Uses WScript.Shell to send an invisible F15 keypress.
        This keeps the system awake without affecting work.
    #>
    try {
        if (-not $script:wshShell) {
            $script:wshShell = New-Object -ComObject WScript.Shell
        }
        $script:wshShell.SendKeys([char]127)  # F15 key
        Write-VerboseLog -Message "F15 heartbeat sent" -Source "Heartbeat"
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "Heartbeat key send failed: $_"
    }
}

function Get-StatusText {
    <#
    .SYNOPSIS
        Returns the current status text for the tooltip and menu.
    .DESCRIPTION
        Builds a status string showing Active/Paused state, display sleep status,
        and F15 heartbeat status. Also shows time remaining if timer is set.
    #>
    try {
        if ($script:state.Active) {
            $statusActive = Get-LocalizedString -Key 'StatusActive'
            $statusDisplay = if ($script:state.PreventDisplaySleep) { Get-LocalizedString -Key 'StatusDisplayOn' } else { Get-LocalizedString -Key 'StatusDisplayNormal' }
            $statusF15 = if ($script:state.UseHeartbeatKeypress) { Get-LocalizedString -Key 'StatusF15On' } else { Get-LocalizedString -Key 'StatusF15Off' }
            $result = "$statusActive | $statusDisplay | $statusF15"
            
            if ($script:state.Until) {
                $minsLeft = [math]::Round(($script:state.Until - (Get-Date)).TotalMinutes)
                $minText = Get-LocalizedString -Key 'StatusMinLeft'
                $result += " | ${minsLeft}${minText}"
            }
            return $result
        }
        else {
            $statusPaused = Get-LocalizedString -Key 'StatusPaused'
            $statusDisplay = Get-LocalizedString -Key 'StatusDisplayNormal'
            $statusF15 = Get-LocalizedString -Key 'StatusF15Off'
            return "$statusPaused | $statusDisplay | $statusF15"
        }
    }
    catch {
        return Get-LocalizedString -Key 'StatusUnavailable'
    }
}

function Get-CustomTrayIcon {
    <#
    .SYNOPSIS
        Creates a custom tray icon for the specified state.
    .DESCRIPTION
        Generates a colored circle icon based on the state:
        - active: Red circle
        - timed: Orange circle
        - paused: Gray circle
    .PARAMETER State
        The state to create an icon for: 'active', 'timed', or 'paused'.
    #>
    param(
        [Parameter(Mandatory)]
        [ValidateSet('active', 'timed', 'paused')]
        [string]$State
    )
    
    try {
        # Define colors for each state
        $colors = @{
            active = [System.Drawing.Color]::FromArgb(220, 53, 69)   # Red
            timed  = [System.Drawing.Color]::FromArgb(253, 126, 20) # Orange
            paused = [System.Drawing.Color]::FromArgb(108, 117, 125) # Gray
        }
        
        $color = $colors[$State]
        $size = 16
        $bitmap = New-Object System.Drawing.Bitmap($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        
        # Fill background with transparent
        $graphics.Clear([System.Drawing.Color]::Transparent)
        
        # Draw circle
        $brush = New-Object System.Drawing.SolidBrush($color)
        $graphics.FillEllipse($brush, 0, 0, $size - 1, $size - 1)
        
        # Draw border
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(80, 80, 80), 1)
        $graphics.DrawEllipse($pen, 0, 0, $size - 1, $size - 1)
        
        $graphics.Dispose()
        $brush.Dispose()
        $pen.Dispose()
        
        $iconHandle = $bitmap.GetHicon()
        $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
        
        # Store bitmap reference for cleanup
        $script:lastIconBitmap = $bitmap
        
        return $icon
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to create custom icon: $_"
        return $null
    }
}

function Update-RedballUI {
    <#
    .SYNOPSIS
        Updates the tray icon and menu items to reflect current state.
    .DESCRIPTION
        Updates the NotifyIcon tooltip text, icon color, and menu item states
        based on the current application state (active/paused, settings).
        Thread-safe - can be called from any context.
    #>
    try {
        if ($script:state.IsShuttingDown) { return }
        
        # Get status text for tooltip
        $statusText = Get-StatusText
        $script:state.NotifyIcon.Text = "Redball ($statusText)"
        
        # Determine icon state
        $iconState = if ($script:state.Active) {
            if ($script:state.Until) { 'timed' } else { 'active' }
        }
        else {
            'paused'
        }
        
        # Only update icon if state changed (reduce flicker)
        if ($script:state.LastIconState -ne $iconState) {
            $newIcon = Get-CustomTrayIcon -State $iconState
            if ($newIcon) {
                # Dispose previous icon to prevent memory leak
                if ($script:state.PreviousIcon) {
                    try { $script:state.PreviousIcon.Dispose() } catch {}
                }
                $script:state.PreviousIcon = $script:state.NotifyIcon.Icon
                $script:state.NotifyIcon.Icon = $newIcon
                $script:state.LastIconState = $iconState
            }
        }
        
        # Update menu items
        $menuText = if ($script:state.Active) { Get-LocalizedString -Key 'MenuPause' } else { Get-LocalizedString -Key 'MenuResume' }
        $script:state.ToggleMenuItem.Text = $menuText
        $script:state.StatusMenuItem.Text = $statusText
        $script:state.DisplayMenuItem.Checked = $script:state.PreventDisplaySleep
        $script:state.HeartbeatMenuItem.Checked = $script:state.UseHeartbeatKeypress
        $script:state.BatteryMenuItem.Checked = $script:state.BatteryAware
        $script:state.NetworkMenuItem.Checked = $script:state.NetworkAware
        $script:state.IdleMenuItem.Checked = $script:state.IdleDetection
        
        Write-VerboseLog -Message "UI updated - State:$iconState" -Source "UI"
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "UI update skipped: $_"
    }
}

function Write-FeatureUsageSummary {
    <#
    .SYNOPSIS
        Logs a summary of feature usage before shutdown.
    .DESCRIPTION
        Records which features were used during the session for analytics.
        Helps understand which features are popular.
    #>
    try {
        $featuresUsed = @()
        if ($script:state.BatteryAware) { $featuresUsed += 'BatteryAware' }
        if ($script:state.NetworkAware) { $featuresUsed += 'NetworkAware' }
        if ($script:state.IdleDetection) { $featuresUsed += 'IdleDetection' }
        if ($script:state.UseHeartbeatKeypress) { $featuresUsed += 'F15Heartbeat' }
        if ($script:config.TypeThingEnabled) { $featuresUsed += 'TypeThing' }
        if ($script:config.ScheduleEnabled) { $featuresUsed += 'Schedule' }
        if ($script:config.PresentationModeDetection) { $featuresUsed += 'PresentationMode' }
        
        if ($featuresUsed.Count -gt 0) {
            Write-RedballLog -Level 'INFO' -Message "Features used this session: $($featuresUsed -join ', ')"
        }
        
        Write-VerboseLog -Message "Feature usage summary logged" -Source "Analytics"
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "Feature usage summary failed: $_"
    }
}

function Get-TypeThingTheme {
    <#
    .SYNOPSIS
        Returns theme settings for the TypeThing clipboard typer.
    .DESCRIPTION
        Retrieves color and font settings for the specified theme.
        Falls back to 'dark' theme if the requested theme doesn't exist.
    .PARAMETER ThemeName
        The name of the theme to retrieve ('dark', 'light', or 'hacker').
    #>
    param(
        [string]$ThemeName = $script:config.TypeThingTheme
    )
    
    # Ensure themes are initialized
    if (-not $script:TypeThingThemes) {
        try {
            $script:TypeThingThemes = @{
                light  = @{ Background = [System.Drawing.Color]::FromArgb(245, 245, 245); Surface = [System.Drawing.Color]::White; Text = [System.Drawing.Color]::FromArgb(33, 33, 33); Accent = [System.Drawing.Color]::FromArgb(0, 120, 212); FontName = 'Segoe UI'; FontSize = 11 }
                dark   = @{ Background = [System.Drawing.Color]::FromArgb(30, 30, 30); Surface = [System.Drawing.Color]::FromArgb(45, 45, 45); Text = [System.Drawing.Color]::FromArgb(204, 204, 204); Accent = [System.Drawing.Color]::FromArgb(0, 120, 212); FontName = 'Segoe UI'; FontSize = 11 }
                hacker = @{ Background = [System.Drawing.Color]::Black; Surface = [System.Drawing.Color]::FromArgb(10, 10, 10); Text = [System.Drawing.Color]::FromArgb(0, 255, 0); Accent = [System.Drawing.Color]::FromArgb(0, 200, 0); FontName = 'Consolas'; FontSize = 11 }
            }
        }
        catch {
            # Fallback without System.Drawing types for headless environments
            $script:TypeThingThemes = @{
                light  = @{ Background = 'LightGray'; Surface = 'White'; Text = 'Black'; Accent = 'Blue'; FontName = 'Segoe UI'; FontSize = 11 }
                dark   = @{ Background = 'DarkGray'; Surface = 'Gray'; Text = 'White'; Accent = 'Blue'; FontName = 'Segoe UI'; FontSize = 11 }
                hacker = @{ Background = 'Black'; Surface = 'Black'; Text = 'Green'; Accent = 'Green'; FontName = 'Consolas'; FontSize = 11 }
            }
        }
    }
    
    # Return requested theme or fallback to dark
    if ($script:TypeThingThemes.ContainsKey($ThemeName)) {
        return $script:TypeThingThemes[$ThemeName]
    }
    return $script:TypeThingThemes['dark']
}

function Import-RedballConfig {
    <#
    .SYNOPSIS
        Loads Redball configuration from JSON.
    .DESCRIPTION
        Reads configuration values from disk and merges known keys into the in-memory
    .PARAMETER Path
        The file path to load the configuration from.
    .EXAMPLE
        Import-RedballConfig -Path 'C:\Redball\Redball.json'
        Loads configuration from the specified path.
    .NOTES
        Creates default config file if none exists.
        Syncs certain config values to state for runtime use.
    #>
    param([string]$Path = '')
    if (-not $Path) { $Path = Join-Path $script:AppRoot 'Redball.json' }
    Write-VerboseLog -Message "Import-RedballConfig called with path: $Path" -Source "Config"
    
    try {
        if (Test-Path $Path) {
            Write-VerboseLog -Message "Config file found, loading..." -Source "Config"
            $content = Get-Content $Path -Raw
            $loadedConfig = $content | ConvertFrom-Json
            
            # Merge loaded values into current config
            foreach ($key in $loadedConfig.PSObject.Properties.Name) {
                if ($script:config.ContainsKey($key)) {
                    $script:config[$key] = $loadedConfig.$key
                    Write-VerboseLog -Message "  Loaded config: $key = $($loadedConfig.$key)" -Source "Config"
                }
            }
            
            # Sync config values to state
            $script:state.HeartbeatSeconds = $script:config.HeartbeatSeconds
            $script:state.PreventDisplaySleep = $script:config.PreventDisplaySleep
            $script:state.UseHeartbeatKeypress = $script:config.UseHeartbeatKeypress
            $script:state.BatteryAware = $script:config.BatteryAware
            $script:state.BatteryThreshold = $script:config.BatteryThreshold
            $script:state.NetworkAware = $script:config.NetworkAware
            $script:state.IdleDetection = $script:config.IdleDetection
            $script:state.IdleThresholdMinutes = $script:config.IdleThresholdMinutes
            
            Write-RedballLog -Level 'INFO' -Message "Configuration loaded from: $Path"
            Write-VerboseLog -Message "Config loaded successfully with $($loadedConfig.PSObject.Properties.Count) settings" -Source "Config"
        }
        else {
            Write-VerboseLog -Message "Config file not found, using defaults" -Source "Config"
            # Save default config so user can edit it
            Save-RedballConfig -Path $Path
            Write-RedballLog -Level 'INFO' -Message "Default configuration saved to: $Path"
        }
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to load config: $_"
        Write-VerboseLog -Message "Config load exception: $_" -Source "Config"
    }
}

Set-Alias -Name Load-RedballConfig -Value Import-RedballConfig -Scope Script

function Save-RedballConfig {
    <#
    .SYNOPSIS
        Saves Redball configuration to JSON.
    .DESCRIPTION
        Persists the current application configuration to a JSON file.
        Creates directory structure if needed.
        Sanitizes certain values before saving.
    .PARAMETER Path
        The file path to save the configuration to.
    .EXAMPLE
        Save-RedballConfig -Path 'C:\Redball\Redball.json'
        Saves configuration to the specified path.
    .NOTES
        Creates parent directories if they don't exist.
        Only saves config values that should persist.
    #>
    param([string]$Path = '')
    if (-not $Path) { $Path = Join-Path $script:AppRoot 'Redball.json' }
    Write-VerboseLog -Message "Save-RedballConfig called with path: $Path" -Source "Config"
    
    try {
        $dir = Split-Path $Path -Parent
        if ($dir -and -not (Test-Path $dir)) {
            Write-VerboseLog -Message "Creating config directory: $dir" -Source "Config"
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        
        # Sanitize hotkey strings: remove spaces around + signs
        $sanitizedStartHotkey = $script:config.TypeThingStartHotkey -replace '\s*\+\s*', '+'
        $sanitizedStopHotkey = $script:config.TypeThingStopHotkey -replace '\s*\+\s*', '+'
        
        if ($sanitizedStartHotkey -ne $script:config.TypeThingStartHotkey) {
            Write-VerboseLog -Message "Sanitized start hotkey: '$($script:config.TypeThingStartHotkey)' -> '$sanitizedStartHotkey'" -Source "Config"
            $script:config.TypeThingStartHotkey = $sanitizedStartHotkey
        }
        if ($sanitizedStopHotkey -ne $script:config.TypeThingStopHotkey) {
            Write-VerboseLog -Message "Sanitized stop hotkey: '$($script:config.TypeThingStopHotkey)' -> '$sanitizedStopHotkey'" -Source "Config"
            $script:config.TypeThingStopHotkey = $sanitizedStopHotkey
        }

        $configData = @{
        }
        $script:config.Keys | ForEach-Object { $configData[$_] = $script:config[$_] }
        $configData | ConvertTo-Json | Set-Content -Path $Path -Encoding UTF8
        
        Write-RedballLog -Level 'INFO' -Message "Configuration saved to: $Path"
        Write-VerboseLog -Message "Saved $($configData.Count) configuration values" -Source "Config"
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to save config: $_"
        Write-VerboseLog -Message "Config save exception: $_" -Source "Config"
    }
}

function Restore-RedballState {
    <#
    .SYNOPSIS
        Restores session state from a JSON file.
    .DESCRIPTION
        Loads previously saved application state and restores settings
        and timer information if the saved state is still valid.
    .PARAMETER Path
        The file path to load the state from. Defaults to Redball.state.json
    .EXAMPLE
        Restore-RedballState
        Restores state from default location.
    #>
    param([string]$Path = '')
    if (-not $Path) { $Path = Join-Path $script:AppRoot 'Redball.state.json' }
    Write-VerboseLog -Message "Restore-RedballState called with path: $Path" -Source "State"
    
    try {
        if (Test-Path $Path) {
            Write-VerboseLog -Message "State file found, loading..." -Source "State"
            $content = Get-Content $Path -Raw
            $stateData = $content | ConvertFrom-Json
            
            # Restore state values
            $script:state.PreventDisplaySleep = $stateData.PreventDisplaySleep
            $script:state.UseHeartbeatKeypress = $stateData.UseHeartbeatKeypress
            $script:state.BatteryAware = $stateData.BatteryAware
            $script:state.BatteryThreshold = $stateData.BatteryThreshold
            
            # Restore timed mode if still valid
            if ($stateData.Until) {
                $until = [datetime]::Parse($stateData.Until)
                if ($until -gt (Get-Date)) {
                    $script:state.Until = $until
                }
            }
            
            Write-RedballLog -Level 'INFO' -Message 'Session state restored'
            Remove-Item $Path -Force -ErrorAction SilentlyContinue
            Write-VerboseLog -Message "State loaded successfully" -Source "State"
            return $true
        }
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to restore state: $_"
        Write-VerboseLog -Message "State load exception: $_" -Source "State"
    }
    return $false
}

function Save-RedballState {
    <#
    .SYNOPSIS
        Saves the current session state to a JSON file.
    .DESCRIPTION
        Persists the current application state including active status,
        settings, and timer information for session restore on next launch.
    .PARAMETER Path
        The file path to save the state to. Defaults to Redball.state.json
    .EXAMPLE
        Save-RedballState
        Saves state to default location.
    #>
    param([string]$Path = '')
    if (-not $Path) { $Path = Join-Path $script:AppRoot 'Redball.state.json' }
    Write-VerboseLog -Message "Save-RedballState called with path: $Path" -Source "State"
    
    try {
        $stateData = @{
            Active               = $script:state.Active
            PreventDisplaySleep  = $script:state.PreventDisplaySleep
            UseHeartbeatKeypress = $script:state.UseHeartbeatKeypress
            Until                = if ($script:state.Until) { $script:state.Until.ToString('o') } else { $null }
            BatteryAware         = $script:state.BatteryAware
            BatteryThreshold     = $script:state.BatteryThreshold
            SavedAt              = (Get-Date).ToString('o')
        }
        $stateData | ConvertTo-Json | Set-Content -Path $Path -Encoding UTF8
        Write-VerboseLog -Message "State saved successfully" -Source "State"
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to save state: $_"
        Write-VerboseLog -Message "State save exception: $_" -Source "State"
    }
}

function Set-ActiveState {
    <#
    .SYNOPSIS
        Activates or deactivates the keep-awake functionality.
    .DESCRIPTION
        Sets the active state, updates the execution state via SetThreadExecutionState,
        and updates the UI. Can optionally show a balloon notification.
        Supports process isolation mode for enhanced reliability.
    .PARAMETER Active
        Whether to activate ($true) or deactivate ($false) keep-awake.
    .PARAMETER Until
        Optional datetime when to automatically deactivate.
    .PARAMETER ShowBalloon
        Whether to show a tray balloon notification.
    .EXAMPLE
        Set-ActiveState -Active $true
        Activates keep-awake indefinitely.
    .EXAMPLE
        Set-ActiveState -Active $true -Until (Get-Date).AddMinutes(30)
        Activates keep-awake for 30 minutes.
    .NOTES
        Uses SetThreadExecutionState API to prevent sleep.
        Supports process isolation via separate runspace.
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [bool]$Active,
        [Nullable[datetime]]$Until,
        [bool]$ShowBalloon = $true
    )
    Write-VerboseLog -Message "Set-ActiveState called - Active:$Active Until:$Until ShowBalloon:$ShowBalloon" -Source "Core"
    
    if ($script:state.IsShuttingDown) { 
        Write-VerboseLog -Message "Shutting down, ignoring state change" -Source "Core"
        return 
    }

    $targetState = if ($Active) { 'active' } else { 'paused' }
    if (-not $PSCmdlet.ShouldProcess('Redball keep-awake state', "Set state to $targetState")) {
        Write-VerboseLog -Message "State change cancelled by ShouldProcess" -Source "Core"
        return
    }

    try {
        $script:state.Active = $Active
        $script:state.Until = if ($Active) { $Until } else { $null }
        Write-VerboseLog -Message "State set to Active:$Active Until:$($script:state.Until)" -Source "Core"

        if ($script:config.ProcessIsolation) {
            Write-VerboseLog -Message "Using process isolation mode" -Source "Core"
            if ($script:state.Active) {
                if (-not $script:state.KeepAwakeRunspaceInfo) {
                    Write-VerboseLog -Message "Starting keep-awake runspace" -Source "Core"
                    $script:state.KeepAwakeRunspaceInfo = Start-KeepAwakeRunspace
                }

                if (-not $script:state.KeepAwakeRunspaceInfo) {
                    Write-VerboseLog -Message "Runspace failed to start, falling back to direct API" -Source "Core"
                    Set-KeepAwakeState -Enable:$true
                }
            }
            else {
                if ($script:state.KeepAwakeRunspaceInfo) {
                    Write-VerboseLog -Message "Stopping keep-awake runspace" -Source "Core"
                    Stop-KeepAwakeRunspace -RunspaceInfo $script:state.KeepAwakeRunspaceInfo
                    $script:state.KeepAwakeRunspaceInfo = $null
                }
                Set-KeepAwakeState -Enable:$false
            }
        }
        else {
            Write-VerboseLog -Message "Using direct API mode" -Source "Core"
            Set-KeepAwakeState -Enable:$script:state.Active
        }

        Write-RedballTelemetryEvent -TelemetryEvent 'StateChanged' -Data @{
            Active = $script:state.Active
            Timed  = [bool]$script:state.Until
        }

        Update-RedballUI
        if ($ShowBalloon) {
            if ($script:state.NotifyIcon -and -not $script:state.NotifyIcon.IsDisposed) {
                try {
                    $script:state.NotifyIcon.ShowBalloonTip(1500)
                    Write-VerboseLog -Message "Balloon tip displayed" -Source "Core"
                }
                catch {
                    Write-RedballLog -Level 'DEBUG' -Message "Balloon tip display skipped: $_"
                }
            }
        }
    }
    catch [System.Management.Automation.PipelineStoppedException] {
        Write-VerboseLog -Message "Pipeline stopped exception, exiting application" -Source "Core"
        Exit-Application
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to set active state: $_"
        Write-VerboseLog -Message "SetActiveState exception: $_" -Source "Core"
    }
}

function Switch-ActiveState {
    try {
        Set-ActiveState -Active:(-not $script:state.Active)
    }
    catch [System.Management.Automation.PipelineStoppedException] {
        Exit-Application
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to switch: $_"
    }
}

function Start-TimedAwake {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter(Mandatory)]
        [ValidateRange(1, 720)]
        [int]$Minutes
    )

    if (-not $PSCmdlet.ShouldProcess('Redball keep-awake timer', "Start timed awake for $Minutes minutes")) {
        return
    }

    try {
        $until = (Get-Date).AddMinutes($Minutes)
        Set-ActiveState -Active:$true -Until $until
    }
    catch [System.Management.Automation.PipelineStoppedException] {
        Exit-Application
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to start timed: $_"
    }
}

function Exit-Application {
    <#
    .SYNOPSIS
        Gracefully shuts down the Redball application.
    .DESCRIPTION
        Hides the tray icon, resets power state, saves session state,
        stops timers, disposes resources, and exits the application.
    .EXAMPLE
        Exit-Application
        Shuts down Redball cleanly.
    #>
    $script:state.IsShuttingDown = $true
    
    # Log feature usage summary before shutdown (Analytics)
    Write-FeatureUsageSummary
    
    # TypeThing cleanup - stop typing and unregister hotkeys
    try {
        if ($script:state.TypeThingIsTyping) {
            Stop-TypeThingTyping
        }
        if ($script:state.TypeThingTimer) {
            $script:state.TypeThingTimer.Stop()
            $script:state.TypeThingTimer.Dispose()
            $script:state.TypeThingTimer = $null
        }
        if ($script:state.TypeThingCountdown) {
            $script:state.TypeThingCountdown.Stop()
            $script:state.TypeThingCountdown.Dispose()
            $script:state.TypeThingCountdown = $null
        }
        Unregister-TypeThingHotkeys
        if ($script:state.TypeThingHotkeyWindow) {
            $script:state.TypeThingHotkeyWindow.Destroy()
            $script:state.TypeThingHotkeyWindow = $null
        }
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "TypeThing cleanup skipped: $_"
    }
    
    # CRITICAL: Hide icon IMMEDIATELY before any other cleanup
    if ($script:state.NotifyIcon) {
        try {
            $script:state.NotifyIcon.Visible = $false
            $script:state.NotifyIcon.Dispose()
            Start-Sleep -Milliseconds 100  # Give Windows time to remove icon
        }
        catch {
            Write-RedballLog -Level 'DEBUG' -Message "Notify icon cleanup skipped: $_"
        }
    }
    
    # Reset power state
    try {
        if ($script:state.KeepAwakeRunspaceInfo) {
            Stop-KeepAwakeRunspace -RunspaceInfo $script:state.KeepAwakeRunspaceInfo
            $script:state.KeepAwakeRunspaceInfo = $null
        }
        Set-KeepAwakeState -Enable:$false
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "Power state reset during exit encountered an issue: $_"
    }
    
    # Save session state
    try {
        Save-RedballState
        Save-RedballConfig -Path $ConfigPath
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "State/config save during exit encountered an issue: $_"
    }
    
    # Stop and dispose timers
    if ($script:state.HeartbeatTimer) {
        try {
            $script:state.HeartbeatTimer.Stop()
            $script:state.HeartbeatTimer.Dispose()
        }
        catch {
            Write-RedballLog -Level 'DEBUG' -Message "Heartbeat timer cleanup skipped: $_"
        }
    }
    if ($script:state.DurationTimer) {
        try {
            $script:state.DurationTimer.Stop()
            $script:state.DurationTimer.Dispose()
        }
        catch {
            Write-RedballLog -Level 'DEBUG' -Message "Duration timer cleanup skipped: $_"
        }
    }
    
    # Clean up icon resources
    if ($script:state.PreviousIcon) {
        try {
            $script:state.PreviousIcon.Dispose()
        }
        catch {
            Write-RedballLog -Level 'DEBUG' -Message "Previous icon cleanup skipped: $_"
        }
    }
    
    # Release WScript.Shell COM object if cached
    if ($script:wshShell) {
        try {
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($script:wshShell) | Out-Null
            $script:wshShell = $null
        }
        catch {
            Write-RedballLog -Level 'DEBUG' -Message "WScript.Shell release skipped: $_"
        }
    }
    
    # Release singleton mutex so new instances can start
    if ($script:singletonMutex) {
        try {
            $script:singletonMutex.ReleaseMutex()
            $script:singletonMutex.Dispose()
            $script:singletonMutex = $null
        }
        catch {
            Write-RedballLog -Level 'DEBUG' -Message "Singleton mutex release skipped: $_"
        }
    }
    
    # Force garbage collection
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    
    # Exit application
    [System.Windows.Forms.Application]::Exit()
}

# --- Session State Management ---

function Save-RedballState {
    <#
    .SYNOPSIS
        Saves the current session state to a JSON file.
    .DESCRIPTION
        Persists the current application state including active status,
        settings, and timer information for session restore on next launch.
    .PARAMETER Path
        The file path to save the state to. Defaults to Redball.state.json
    .EXAMPLE
        Save-RedballState
        Saves state to default location.
    #>
    param([string]$Path = '')
    if (-not $Path) { $Path = Join-Path $script:AppRoot 'Redball.state.json' }
    Write-VerboseLog -Message "Save-RedballState called with path: $Path" -Source "State"
    
    try {
        $stateData = @{
            Active               = $script:state.Active
            PreventDisplaySleep  = $script:state.PreventDisplaySleep
            UseHeartbeatKeypress = $script:state.UseHeartbeatKeypress
            Until                = if ($script:state.Until) { $script:state.Until.ToString('o') } else { $null }
            BatteryAware         = $script:state.BatteryAware
            BatteryThreshold     = $script:state.BatteryThreshold
            SavedAt              = (Get-Date).ToString('o')
        }
        $stateData | ConvertTo-Json | Set-Content -Path $Path -Encoding UTF8
        Write-VerboseLog -Message "State saved successfully" -Source "State"
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to save state: $_"
        Write-VerboseLog -Message "State save exception: $_" -Source "State"
    }
}

function Restore-RedballState {
    <#
    .SYNOPSIS
        Restores session state from a JSON file.
    .DESCRIPTION
        Loads previously saved application state and restores settings
        and timer information if the saved state is still valid.
    .PARAMETER Path
        The file path to load the state from. Defaults to Redball.state.json
    .EXAMPLE
        Restore-RedballState
        Restores state from default location.
    #>
    param([string]$Path = '')
    if (-not $Path) { $Path = Join-Path $script:AppRoot 'Redball.state.json' }
    try {
        if (Test-Path $Path) {
            $content = Get-Content $Path -Raw
            $stateData = $content | ConvertFrom-Json
            
            # Restore state values
            $script:state.PreventDisplaySleep = $stateData.PreventDisplaySleep
            $script:state.UseHeartbeatKeypress = $stateData.UseHeartbeatKeypress
            $script:state.BatteryAware = $stateData.BatteryAware
            $script:state.BatteryThreshold = $stateData.BatteryThreshold
            
            # Restore timed mode if still valid
            if ($stateData.Until) {
                $until = [datetime]::Parse($stateData.Until)
                if ($until -gt (Get-Date)) {
                    $script:state.Until = $until
                }
            }
            
            Write-RedballLog -Level 'INFO' -Message 'Session state restored'
            Remove-Item $Path -Force -ErrorAction SilentlyContinue
            return $true
        }
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to restore state: $_"
    }
    return $false
}

# --- Windows Startup Management ---

function Install-RedballStartup {
    try {
        $scriptPath = Join-Path $script:AppRoot 'Redball.ps1'
        $startupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
        $shortcutPath = Join-Path $startupPath 'Redball.lnk'
        
        $WshShell = New-Object -ComObject WScript.Shell
        $shortcut = $WshShell.CreateShortcut($shortcutPath)
        
        # Use powershellw.exe (windowless) if available, otherwise powershell.exe with -WindowStyle Hidden
        $pwshPath = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
        $ps5Path = (Get-Command powershell -ErrorAction SilentlyContinue).Source
        $ps5wPath = $ps5Path -replace 'powershell\.exe$', 'powershellw.exe'
        
        if ($pwshPath -and (Test-Path $pwshPath)) {
            # PowerShell 7+ - use -WindowStyle Hidden (works reliably in PS7)
            $shortcut.TargetPath = $pwshPath
            $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`" -Minimized"
        }
        elseif (Test-Path $ps5wPath) {
            # Windows PowerShell 5.1 windowless executable
            $shortcut.TargetPath = $ps5wPath
            $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Minimized"
        }
        else {
            # Fallback to regular powershell with hidden window style
            $shortcut.TargetPath = $ps5Path
            $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`" -Minimized"
        }
        
        $shortcut.WorkingDirectory = $script:AppRoot
        $shortcut.IconLocation = 'powershell.exe,0'
        $shortcut.Save()
        
        Write-RedballLog -Level 'INFO' -Message "Installed to Windows startup: $shortcutPath"
        return $true
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to install startup: $_"
        return $false
    }
}

function Uninstall-RedballStartup {
    try {
        $startupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
        $shortcutPath = Join-Path $startupPath 'Redball.lnk'
        
        if (Test-Path $shortcutPath) {
            Remove-Item $shortcutPath -Force
            Write-RedballLog -Level 'INFO' -Message 'Removed from Windows startup'
            return $true
        }
        return $false
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to uninstall startup: $_"
        return $false
    }
}

function Test-RedballStartup {
    $startupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\Redball.lnk"
    return Test-Path $startupPath
}

# --- Toast Notifications (Win10/11) ---

function Send-RedballToast {
    <#
    .SYNOPSIS
        Displays a Windows toast notification.
    .DESCRIPTION
        Shows a native Windows 10/11 toast notification with the specified
        title and message. Falls back to balloon tip on older Windows versions.
    .PARAMETER Title
        The title text for the notification.
    .PARAMETER Message
        The message body text for the notification.
    .PARAMETER Icon
        The icon type: 'info', 'warning', or 'error'. Defaults to 'info'.
    .EXAMPLE
        Send-RedballToast -Title 'Redball' -Message 'Keep-awake activated'
        Shows a toast notification.
    #>
    param(
        [Parameter(Mandatory)]
        [string]$Title,
        [Parameter(Mandatory)]
        [string]$Message,
        [string]$Icon = 'info'
    )
    try {
        # Try Windows 10/11 Toast Notifications first
        if ($script:HasWinRT -and (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name 'CurrentMajorVersionNumber' -ErrorAction SilentlyContinue).CurrentMajorVersionNumber -ge 10) {
            [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
            [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
            
            $toastXml = [Windows.Data.Xml.Dom.XmlDocument]::New()
            $toastXml.LoadXml(@"
<toast>
    <visual>
        <binding template="ToastGeneric">
            <text>$Title</text>
            <text>$Message</text>
        </binding>
    </visual>
</toast>
"@)
            $toast = [Windows.UI.Notifications.ToastNotification]::New($toastXml)
            [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Redball').Show($toast)
        }
        else {
            # Fallback to balloon tip
            if ($script:state.NotifyIcon) {
                $script:state.NotifyIcon.ShowBalloonTip(2000, $Title, $Message, [System.Windows.Forms.ToolTipIcon]::$Icon)
            }
        }
    }
    catch {
        # Fallback to balloon tip
        if ($script:state.NotifyIcon) {
            $script:state.NotifyIcon.ShowBalloonTip(2000, $Title, $Message, [System.Windows.Forms.ToolTipIcon]::$Icon)
        }
    }
}

# --- Battery Monitoring ---

function Get-BatteryStatus {
    <#
    .SYNOPSIS
        Retrieves the current battery status.
    .DESCRIPTION
        Queries WMI to get battery information including charge percentage
        and whether the system is running on battery power.
        Results are cached for 30 seconds to avoid expensive WMI calls on every timer tick.
    .EXAMPLE
        $battery = Get-BatteryStatus
        Returns battery information hashtable.
    #>
    # Return cached result if still fresh (60-second TTL to reduce CIM overhead)
    if ($script:lastBatteryCheck -and ((Get-Date) - $script:lastBatteryCheck).TotalSeconds -lt 60) {
        return $script:lastBatteryResult
    }
    
    try {
        $battery = Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($battery) {
            $powerStatus = Get-CimInstance -ClassName BatteryStatus -Namespace 'root/wmi' -ErrorAction SilentlyContinue | Select-Object -First 1
            $isOnBattery = $powerStatus.PowerOnLine -eq $false
            $estimatedCharge = $battery.EstimatedChargeRemaining
            
            $script:lastBatteryResult = @{
                HasBattery    = $true
                OnBattery     = $isOnBattery
                ChargePercent = $estimatedCharge
                BatteryStatus = $battery.BatteryStatus
            }
        }
        else {
            $script:lastBatteryResult = @{ HasBattery = $false }
        }
        $script:lastBatteryCheck = Get-Date
        return $script:lastBatteryResult
    }
    catch {
        return @{ HasBattery = $false }
    }
}

function Test-BatteryThreshold {
    if (-not $script:state.BatteryAware) { return $true }
    
    $battery = Get-BatteryStatus
    if (-not $battery.HasBattery) { return $true }
    
    if ($battery.OnBattery -and $battery.ChargePercent -le $script:state.BatteryThreshold) {
        return $false
    }
    return $true
}

function Update-BatteryAwareState {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()

    if (-not $script:state.BatteryAware) { return }
    
    $canRun = Test-BatteryThreshold
    $battery = Get-BatteryStatus
    
    if (-not $canRun -and $script:state.Active -and -not $script:state.AutoPausedBattery) {
        # Auto-pause due to low battery
        $script:state.AutoPausedBattery = $true
        $script:state.ActiveBeforeBattery = $script:state.Active
        Set-ActiveState -Active:$false -ShowBalloon:$true
        Send-RedballToast -Title 'Redball Paused' -Message "Battery below $($script:state.BatteryThreshold)%. Keep-awake paused to save power."
        Write-RedballLog -Level 'INFO' -Message "Auto-paused due to low battery: $($battery.ChargePercent)%"
    }
    elseif ($canRun -and $script:state.AutoPausedBattery) {
        # Auto-resume when power restored or charged
        $script:state.AutoPausedBattery = $false
        if ($script:state.ActiveBeforeBattery) {
            Set-ActiveState -Active:$true -ShowBalloon:$true
            Send-RedballToast -Title 'Redball Resumed' -Message 'Power restored or battery charged. Keep-awake resumed.'
            Write-RedballLog -Level 'INFO' -Message 'Auto-resumed - power/battery OK'
        }
    }
    
    $script:state.OnBattery = $battery.OnBattery
}

# --- CLI Status Output ---

function Get-RedballStatus {
    $battery = Get-BatteryStatus
    $status = @{
        Version              = $script:VERSION
        Active               = $script:state.Active
        Until                = if ($script:state.Until) { $script:state.Until.ToString('o') } else { $null }
        PreventDisplaySleep  = $script:state.PreventDisplaySleep
        UseHeartbeatKeypress = $script:state.UseHeartbeatKeypress
        BatteryAware         = $script:state.BatteryAware
        OnBattery            = $battery.OnBattery
        BatteryPercent       = if ($battery.HasBattery) { $battery.ChargePercent } else { $null }
        HasBattery           = $battery.HasBattery
        AutoPausedBattery    = $script:state.AutoPausedBattery
        Uptime               = if ($script:state.StartTime) { ([DateTime]::Now - $script:state.StartTime).ToString() } else { $null }
    }
    return $status | ConvertTo-Json -Depth 3
}

# --- Global Hotkey Support ---

$script:hotkeyRegistered = $false
$script:hotkeyId = 1

$hotkeySignature = @"
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class HotkeyHelper {
    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint VK_PAUSE = 0x13;
    public const uint VK_SPACE = 0x20;
    public const uint WM_HOTKEY = 0x0312;
}
"@

try {
    Add-Type -TypeDefinition $hotkeySignature -Language CSharp -ErrorAction SilentlyContinue
}
catch {
    Write-RedballLog -Level 'DEBUG' -Message "Hotkey helper type initialization skipped: $_"
}

# --- TypeThing SendInput Interop ---

# Determine correct INPUT struct layout for current platform (32-bit vs 64-bit)
$inputFieldOffset = if ([IntPtr]::Size -eq 8) { 8 } else { 4 }
$inputStructSize = if ([IntPtr]::Size -eq 8) { 40 } else { 28 }

$typeThingInteropSignature = @"
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT {
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Explicit, Size = $inputStructSize)]
public struct INPUT {
    [FieldOffset(0)] public uint type;
    [FieldOffset($inputFieldOffset)] public KEYBDINPUT ki;
}

public static class TypeThingInput {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_TAB = 0x09;
}

public class HotkeyMessageWindow : NativeWindow {
    public event Action<int> HotkeyPressed;

    public HotkeyMessageWindow() {
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x0312) {
            if (HotkeyPressed != null) HotkeyPressed(m.WParam.ToInt32());
        }
        base.WndProc(ref m);
    }

    public void Destroy() {
        DestroyHandle();
    }
}
"@

try {
    Add-Type -TypeDefinition $typeThingInteropSignature -ReferencedAssemblies System.Windows.Forms -Language CSharp -ErrorAction SilentlyContinue
}
catch {
    Write-RedballLog -Level 'DEBUG' -Message "TypeThing interop type initialization skipped: $_"
}

function Register-GlobalHotkey {
    param([IntPtr]$WindowHandle)
    try {
        if ($script:hotkeyRegistered) { return }
        
        # Register Ctrl+Alt+Pause to toggle
        $result = [HotkeyHelper]::RegisterHotKey($WindowHandle, $script:hotkeyId, 
            ([HotkeyHelper]::MOD_CONTROL -bor [HotkeyHelper]::MOD_ALT), [HotkeyHelper]::VK_PAUSE)
        
        if ($result) {
            $script:hotkeyRegistered = $true
            Write-RedballLog -Level 'INFO' -Message 'Global hotkey registered: Ctrl+Alt+Pause'
        }
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to register hotkey: $_"
    }
}

function Unregister-GlobalHotkey {
    param([IntPtr]$WindowHandle)
    try {
        if (-not $script:hotkeyRegistered) { return }
        
        [HotkeyHelper]::UnregisterHotKey($WindowHandle, $script:hotkeyId)
        $script:hotkeyRegistered = $false
        Write-RedballLog -Level 'INFO' -Message 'Global hotkey unregistered'
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "Hotkey unregister skipped: $_"
    }
}

$contextMenu = New-Object System.Windows.Forms.ContextMenuStrip

$statusMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem
$statusMenuItem.Enabled = $false
$script:state.StatusMenuItem = $statusMenuItem

$toggleMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Pause Keep-Awake'
$toggleMenuItem.ShortcutKeyDisplayString = 'Space'
$script:state.ToggleMenuItem = $toggleMenuItem

$displayMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Prevent Display Sleep'
$displayMenuItem.CheckOnClick = $true
$displayMenuItem.ShortcutKeyDisplayString = 'D'
$displayMenuItem.AccessibleName = 'Prevent Display Sleep Toggle'
$displayMenuItem.AccessibleDescription = 'Toggle whether to prevent the display from going to sleep'
$script:state.DisplayMenuItem = $displayMenuItem

$heartbeatMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Use F15 Heartbeat'
$heartbeatMenuItem.CheckOnClick = $true
$heartbeatMenuItem.ShortcutKeyDisplayString = 'H'
$heartbeatMenuItem.AccessibleName = 'F15 Heartbeat Toggle'
$heartbeatMenuItem.AccessibleDescription = 'Toggle sending invisible F15 keypresses to prevent idle detection'
$script:state.HeartbeatMenuItem = $heartbeatMenuItem

$stayAwakeMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Stay Awake For'

foreach ($duration in @(15, 30, 60, 120)) {
    $durationItem = New-Object System.Windows.Forms.ToolStripMenuItem "$duration minutes"
    $durationItem.Tag = $duration
    $durationItem.add_Click({
            Start-TimedAwake -Minutes ([int]$this.Tag)
        })
    [void]$stayAwakeMenuItem.DropDownItems.Add($durationItem)
}

$alwaysOnMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Stay Awake Until Paused'
$alwaysOnMenuItem.ShortcutKeyDisplayString = 'I'
$alwaysOnMenuItem.AccessibleName = 'Stay awake indefinitely'
$alwaysOnMenuItem.add_Click({
        Set-ActiveState -Active:$true -Until $null
    })

$exitMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Exit'
$exitMenuItem.ShortcutKeyDisplayString = 'X'
$exitMenuItem.AccessibleName = 'Exit Redball'
$exitMenuItem.AccessibleDescription = 'Close Redball and restore normal power settings'

[void]$contextMenu.Items.Add($statusMenuItem)
[void]$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$contextMenu.Items.Add($toggleMenuItem)
[void]$contextMenu.Items.Add($displayMenuItem)
[void]$contextMenu.Items.Add($heartbeatMenuItem)
[void]$contextMenu.Items.Add($stayAwakeMenuItem)
[void]$contextMenu.Items.Add($alwaysOnMenuItem)
[void]$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))

# Battery Aware Menu Item
$batteryMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Battery-Aware Mode'
$batteryMenuItem.CheckOnClick = $true
$batteryMenuItem.Checked = $script:state.BatteryAware
$batteryMenuItem.ShortcutKeyDisplayString = 'B'
$batteryMenuItem.AccessibleName = 'Battery Aware Mode Toggle'
$batteryMenuItem.AccessibleDescription = 'Auto-pause when battery is low to save power'
$batteryMenuItem.add_Click({
        try {
            if ($script:state.IsShuttingDown) { return }
            $script:state.BatteryAware = $batteryMenuItem.Checked
            $script:config.BatteryAware = $batteryMenuItem.Checked
            Save-RedballConfig -Path $ConfigPath
            Write-RedballLog -Level 'INFO' -Message "Battery-aware mode: $($script:state.BatteryAware)"
            Update-RedballUI
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "Battery toggle error: $_"
        }
    })
$script:state.BatteryMenuItem = $batteryMenuItem
[void]$contextMenu.Items.Add($batteryMenuItem)

# Startup Menu Item
$startupMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Start with Windows'
$startupMenuItem.CheckOnClick = $true
$startupMenuItem.Checked = Test-RedballStartup
$startupMenuItem.ShortcutKeyDisplayString = 'S'
$startupMenuItem.AccessibleName = 'Startup Toggle'
$startupMenuItem.AccessibleDescription = 'Launch Redball automatically when Windows starts'
$startupMenuItem.add_Click({
        try {
            if ($script:state.IsShuttingDown) { return }
            if ($startupMenuItem.Checked) {
                Install-RedballStartup
            }
            else {
                Uninstall-RedballStartup
            }
            Update-RedballUI
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "Startup toggle error: $_"
        }
    })
$script:state.StartupMenuItem = $startupMenuItem
[void]$contextMenu.Items.Add($startupMenuItem)

# Network Aware Menu Item
$networkMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Network-Aware Mode'
$networkMenuItem.CheckOnClick = $true
$networkMenuItem.Checked = $script:state.NetworkAware
$networkMenuItem.ShortcutKeyDisplayString = 'N'
$networkMenuItem.AccessibleName = 'Network Aware Mode Toggle'
$networkMenuItem.AccessibleDescription = 'Auto-pause when network disconnects'
$networkMenuItem.add_Click({
        try {
            if ($script:state.IsShuttingDown) { return }
            $script:state.NetworkAware = $networkMenuItem.Checked
            $script:config.NetworkAware = $networkMenuItem.Checked
            Save-RedballConfig -Path $ConfigPath
            Write-RedballLog -Level 'INFO' -Message "Network-aware mode: $($script:state.NetworkAware)"
            Update-RedballUI
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "Network toggle error: $_"
        }
    })
$script:state.NetworkMenuItem = $networkMenuItem
[void]$contextMenu.Items.Add($networkMenuItem)

# Idle Detection Menu Item
$idleMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem "Idle Detection ($($script:state.IdleThresholdMinutes)min)"
$idleMenuItem.CheckOnClick = $true
$idleMenuItem.Checked = $script:state.IdleDetection
$idleMenuItem.ShortcutKeyDisplayString = 'L'
$idleMenuItem.AccessibleName = 'Idle Detection Toggle'
$idleMenuItem.AccessibleDescription = "Auto-pause when user idle for $($script:state.IdleThresholdMinutes) minutes"
$idleMenuItem.add_Click({
        try {
            if ($script:state.IsShuttingDown) { return }
            $script:state.IdleDetection = $idleMenuItem.Checked
            $script:config.IdleDetection = $idleMenuItem.Checked
            Save-RedballConfig -Path $ConfigPath
            Write-RedballLog -Level 'INFO' -Message "Idle detection: $($script:state.IdleDetection)"
            Update-RedballUI
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "Idle toggle error: $_"
        }
    })
$script:state.IdleMenuItem = $idleMenuItem
[void]$contextMenu.Items.Add($idleMenuItem)

# --- TypeThing Submenu ---
if ($script:config.TypeThingEnabled) {
    [void]$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))

    $typeThingMenu = New-Object System.Windows.Forms.ToolStripMenuItem 'TypeThing'
    $typeThingMenu.AccessibleName = 'TypeThing Clipboard Typer'

    $typeThingTypeItem = New-Object System.Windows.Forms.ToolStripMenuItem "Type Clipboard `t$($script:config.TypeThingStartHotkey)"
    $typeThingTypeItem.AccessibleName = 'Type clipboard contents'
    $typeThingTypeItem.add_Click({
            try {
                if ($script:state.IsShuttingDown) { return }
                Start-TypeThingTyping
            }
            catch {
                Write-RedballLog -Level 'WARN' -Message "TypeThing: Type menu error: $_"
            }
        })
    $script:state.TypeThingMenuType = $typeThingTypeItem
    [void]$typeThingMenu.DropDownItems.Add($typeThingTypeItem)

    $typeThingStopItem = New-Object System.Windows.Forms.ToolStripMenuItem "Stop Typing `t$($script:config.TypeThingStopHotkey)"
    $typeThingStopItem.AccessibleName = 'Emergency stop typing'
    $typeThingStopItem.Enabled = $false
    $typeThingStopItem.add_Click({
            try {
                if ($script:state.IsShuttingDown) { return }
                Stop-TypeThingTyping
            }
            catch {
                Write-RedballLog -Level 'WARN' -Message "TypeThing: Stop menu error: $_"
            }
        })
    $script:state.TypeThingMenuStop = $typeThingStopItem
    [void]$typeThingMenu.DropDownItems.Add($typeThingStopItem)

    [void]$typeThingMenu.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))

    $typeThingStatusText = if ($script:config.TypeThingEnabled) { 'Status: Idle' } else { 'Status: Disabled' }
    $typeThingStatusItem = New-Object System.Windows.Forms.ToolStripMenuItem $typeThingStatusText
    $typeThingStatusItem.Enabled = $false
    $script:state.TypeThingMenuStatus = $typeThingStatusItem
    [void]$typeThingMenu.DropDownItems.Add($typeThingStatusItem)

    [void]$typeThingMenu.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))

    $typeThingSettingsItem = New-Object System.Windows.Forms.ToolStripMenuItem 'TypeThing Settings...'
    $typeThingSettingsItem.add_Click({
            try {
                if ($script:state.IsShuttingDown) { return }
                Show-TypeThingSettings
            }
            catch {
                Write-RedballLog -Level 'WARN' -Message "TypeThing: Settings menu error: $_"
            }
        })
    [void]$typeThingMenu.DropDownItems.Add($typeThingSettingsItem)

    [void]$contextMenu.Items.Add($typeThingMenu)
}

[void]$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))

$settingsMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Settings...'
$settingsMenuItem.ShortcutKeyDisplayString = 'G'
$settingsMenuItem.AccessibleName = 'Open Settings Dialog'
$settingsMenuItem.AccessibleDescription = 'Open the full settings dialog to configure all Redball options'
$settingsMenuItem.add_Click({
        try {
            if ($script:state.IsShuttingDown) { return }
            Show-RedballSettings
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "Settings menu error: $_"
        }
    })
[void]$contextMenu.Items.Add($settingsMenuItem)

$aboutMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'About...'
$aboutMenuItem.ShortcutKeyDisplayString = 'A'
$aboutMenuItem.AccessibleName = 'About Redball'
$aboutMenuItem.AccessibleDescription = 'View version information and check for updates'
$aboutMenuItem.add_Click({
        try {
            if ($script:state.IsShuttingDown) { return }
            Show-AboutDialog
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "About menu error: $_"
        }
    })
[void]$contextMenu.Items.Add($aboutMenuItem)

[void]$contextMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$contextMenu.Items.Add($exitMenuItem)

$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
$notifyIcon.Visible = $true
$notifyIcon.ContextMenuStrip = $contextMenu
$notifyIcon.Text = 'Redball'
$script:state.NotifyIcon = $notifyIcon

$heartbeatTimer = New-Object System.Windows.Forms.Timer
$heartbeatTimer.Interval = $script:state.HeartbeatSeconds * 1000
$heartbeatTimer.add_Tick({
        try {
            if ($script:state.IsShuttingDown) { return }
            if ($script:state.Active) {
                Set-KeepAwakeState -Enable:$true
                Send-HeartbeatKey
                Update-RedballUI
            }
        }
        catch [System.Management.Automation.PipelineStoppedException] {
            # IDE stopped script - exit gracefully
            $script:state.HeartbeatTimer.Stop()
            Exit-Application
        }
        catch {
            Write-RedballLog -Level 'DEBUG' -Message "Heartbeat timer tick ignored error: $_"
        }
    })
$heartbeatTimer.Start()
$script:state.HeartbeatTimer = $heartbeatTimer

# --- Duration Timer with Battery Check and Auto-Exit ---

$durationTimer = New-Object System.Windows.Forms.Timer
$durationTimer.Interval = 1000
$durationTimer.add_Tick({
        try {
            if ($script:state.IsShuttingDown) { return }
        
            # Check battery, network, idle, and schedule state
            if ($script:state.BatteryAware) {
                Update-BatteryAwareState
            }
            if ($script:state.NetworkAware) {
                Update-NetworkAwareState
            }
            if ($script:state.IdleDetection) {
                Update-IdleAwareState
            }
            if ($script:config.ScheduleEnabled) {
                Update-ScheduleState
            }
            if ($script:config.PresentationModeDetection) {
                Update-PresentationModeState
            }
        
            # Check if timed mode expired
            if ($script:state.Active -and $script:state.Until -and (Get-Date) -ge $script:state.Until) {
                Set-ActiveState -Active:$false
            
                # Auto-exit if configured
                if ($script:config.AutoExitOnComplete) {
                    Send-RedballToast -Title 'Redball' -Message 'Timed keep-awake completed. Exiting.'
                    Start-Sleep -Seconds 2
                    Exit-Application
                }
            }
            else {
                Update-RedballUI
            }
        }
        catch [System.Management.Automation.PipelineStoppedException] {
            $script:state.DurationTimer.Stop()
            Exit-Application
        }
        catch {
            Write-RedballLog -Level 'DEBUG' -Message "Duration timer tick ignored error: $_"
        }
    })
$durationTimer.Start()
$script:state.DurationTimer = $durationTimer

# Global exception handler to catch unhandled WinForms exceptions
$onThreadException = [System.Threading.ThreadExceptionEventHandler] {
    param($source, $e)
    $null = $source
    # Check if it's a pipeline stopped exception
    if ($e.Exception -is [System.Management.Automation.PipelineStoppedException] -or 
        $e.Exception.Message -match 'pipeline.*stopped') {
        Write-RedballLog -Level 'INFO' -Message 'Pipeline stopped - shutting down gracefully'
        Exit-Application
    }
    else {
        # Log other exceptions but don't crash
        Write-RedballLog -Level 'ERROR' -Message "Unhandled exception: $($e.Exception.Message)"
        # Write detailed crash report
        try {
            $crashPath = Join-Path $script:AppRoot 'Redball.crash.log'
            $report = @(
                "=== UNHANDLED EXCEPTION ==="
                "Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')"
                "Version: $script:VERSION"
                "SessionId: $($script:state.SessionId)"
                "Error: $($e.Exception.Message)"
                "Type: $($e.Exception.GetType().FullName)"
                "Stack: $($e.Exception.StackTrace)"
                "=== END ==="
                ""
            )
            $report -join "`n" | Add-Content -Path $crashPath -Encoding UTF8
        }
        catch {
            Write-Verbose "Crash report write failed: $_"
        }
    }
}
[System.Windows.Forms.Application]::add_ThreadException($onThreadException)

# --- Idle Detection ---

$idleSignature = @"
using System;
using System.Runtime.InteropServices;

public class IdleHelper {
    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    
    public struct LASTINPUTINFO {
        public uint cbSize;
        public uint dwTime;
    }
    
    public static uint GetIdleTime() {
        var lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        GetLastInputInfo(ref lii);
        return (uint)Environment.TickCount - lii.dwTime;
    }
}
"@

try {
    Add-Type -TypeDefinition $idleSignature -Language CSharp -ErrorAction SilentlyContinue
}
catch {
    Write-RedballLog -Level 'DEBUG' -Message "Idle helper type initialization skipped: $_"
}

function Get-IdleTimeMinutes {
    try {
        $idleMs = [IdleHelper]::GetIdleTime()
        return [math]::Round($idleMs / 60000, 1)  # Convert to minutes
    }
    catch {
        return 0
    }
}

function Update-IdleAwareState {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()

    if (-not $script:state.IdleDetection) { return }
    
    $idleMinutes = Get-IdleTimeMinutes
    $threshold = $script:state.IdleThresholdMinutes
    
    if ($idleMinutes -gt $threshold -and $script:state.Active -and -not $script:state.AutoPausedIdle) {
        # Auto-pause due to user idle
        $script:state.AutoPausedIdle = $true
        $script:state.ActiveBeforeIdle = $script:state.Active
        Set-ActiveState -Active:$false -ShowBalloon:$true
        Send-RedballToast -Title 'Redball Paused' -Message "User idle for $([math]::Round($idleMinutes)) minutes. Keep-awake paused."
        Write-RedballLog -Level 'INFO' -Message "Auto-paused due to idle: ${idleMinutes}min"
    }
    elseif ($idleMinutes -lt 1 -and $script:state.AutoPausedIdle) {
        # Auto-resume when user active
        $script:state.AutoPausedIdle = $false
        if ($script:state.ActiveBeforeIdle) {
            Set-ActiveState -Active:$true -ShowBalloon:$true
            Send-RedballToast -Title 'Redball Resumed' -Message 'User activity detected. Keep-awake resumed.'
            Write-RedballLog -Level 'INFO' -Message 'Auto-resumed - user active'
        }
    }
}
function Get-NetworkStatus {
    try {
        $adapter = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.HardwareInterface -eq $true } | Select-Object -First 1
        if ($adapter) {
            return @{
                IsConnected   = $true
                Name          = $adapter.Name
                InterfaceType = $adapter.InterfaceDescription
            }
        }
        return @{ IsConnected = $false }
    }
    catch {
        return @{ IsConnected = $true }  # Assume connected on error
    }
}

function Update-NetworkAwareState {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()

    if (-not $script:state.NetworkAware) { return }
    
    $network = Get-NetworkStatus
    $isDisconnected = -not $network.IsConnected
    
    if ($isDisconnected -and $script:state.Active -and -not $script:state.AutoPausedNetwork) {
        # Auto-pause due to network disconnect
        $script:state.AutoPausedNetwork = $true
        $script:state.ActiveBeforeNetwork = $script:state.Active
        Set-ActiveState -Active:$false -ShowBalloon:$true
        Send-RedballToast -Title 'Redball Paused' -Message 'Network disconnected. Keep-awake paused.'
        Write-RedballLog -Level 'INFO' -Message 'Auto-paused due to network disconnect'
    }
    elseif (-not $isDisconnected -and $script:state.AutoPausedNetwork) {
        # Auto-resume when network restored
        $script:state.AutoPausedNetwork = $false
        if ($script:state.ActiveBeforeNetwork) {
            Set-ActiveState -Active:$true -ShowBalloon:$true
            Send-RedballToast -Title 'Redball Resumed' -Message 'Network connection restored.'
            Write-RedballLog -Level 'INFO' -Message 'Auto-resumed - network reconnected'
        }
    }
}

# --- Scheduled Operation ---

function Test-ScheduleActive {
    if (-not $script:config.ScheduleEnabled) { return $false }
    
    $now = Get-Date
    $currentDay = $now.DayOfWeek.ToString()
    
    # Check if today is in scheduled days
    if ($script:config.ScheduleDays -notcontains $currentDay) {
        return $false
    }
    
    # Parse start and stop times
    $startTime = [datetime]::ParseExact($script:config.ScheduleStartTime, 'HH:mm', $null)
    $stopTime = [datetime]::ParseExact($script:config.ScheduleStopTime, 'HH:mm', $null)
    
    # Create today's start and stop datetime objects
    $todayStart = Get-Date -Year $now.Year -Month $now.Month -Day $now.Day -Hour $startTime.Hour -Minute $startTime.Minute -Second 0
    $todayStop = Get-Date -Year $now.Year -Month $now.Month -Day $now.Day -Hour $stopTime.Hour -Minute $stopTime.Minute -Second 0
    
    # Check if current time is within scheduled hours
    return ($now -ge $todayStart -and $now -le $todayStop)
}

function Update-ScheduleState {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()

    if (-not $script:config.ScheduleEnabled) { return }
    
    $shouldBeActive = Test-ScheduleActive
    
    if ($shouldBeActive -and -not $script:state.Active -and -not $script:state.AutoPausedSchedule) {
        # Auto-start at scheduled time
        Set-ActiveState -Active:$true -ShowBalloon:$true
        Send-RedballToast -Title 'Redball Started' -Message "Keep-awake started per schedule (Weekdays $($script:config.ScheduleStartTime)-$($script:config.ScheduleStopTime))"
        Write-RedballLog -Level 'INFO' -Message 'Auto-started per schedule'
    }
    elseif (-not $shouldBeActive -and $script:state.Active -and -not $script:state.ManualOverride) {
        # Auto-stop at end of schedule (unless manually overridden)
        $script:state.AutoPausedSchedule = $true
        Set-ActiveState -Active:$false -ShowBalloon:$true
        Send-RedballToast -Title 'Redball Paused' -Message "Keep-awake stopped per schedule"
        Write-RedballLog -Level 'INFO' -Message 'Auto-stopped per schedule'
    }
    elseif ($shouldBeActive -and $script:state.AutoPausedSchedule) {
        # Resume if we're back in schedule window
        $script:state.AutoPausedSchedule = $false
    }
}

# --- Presentation Mode Detection ---

$script:lastPresentationCheck = $null
$script:lastPresentationResult = @{ IsPresenting = $false }

function Test-PresentationMode {
    # Return cached result if checked within 10 seconds (process scans are expensive)
    if ($script:lastPresentationCheck -and ((Get-Date) - $script:lastPresentationCheck).TotalSeconds -lt 10) {
        return $script:lastPresentationResult
    }
    try {
        # Check for PowerPoint presentation mode
        $powerPoint = Get-Process -Name "POWERPNT" -ErrorAction SilentlyContinue
        if ($powerPoint) {
            # PowerPoint is running
            $result = @{ IsPresenting = $true; Source = 'PowerPoint' }
            $script:lastPresentationResult = $result
            $script:lastPresentationCheck = Get-Date
            return $result
        }
        
        # Check for Teams screenshare ( Teams.exe with high CPU or specific window title)
        $teams = Get-Process -Name "Teams" -ErrorAction SilentlyContinue
        if ($teams) {
            # Check if Teams window title contains "Sharing" or "Presenting"
            $teamsWindow = $teams.MainWindowTitle
            if ($teamsWindow -match "Sharing|Presenting|Screen sharing") {
                $result = @{ IsPresenting = $true; Source = 'Teams' }
                $script:lastPresentationResult = $result
                $script:lastPresentationCheck = Get-Date
                return $result
            }
        }
        
        # Check Windows presentation settings (Windows 10/11)
        $presentationSettings = Get-ItemProperty -Path "HKCU:\Software\Microsoft\MobilePC\AdaptableSettings" -Name "PresentationMode" -ErrorAction SilentlyContinue
        if ($presentationSettings -and $presentationSettings.PresentationMode -eq 1) {
            $result = @{ IsPresenting = $true; Source = 'Windows Presentation Mode' }
            $script:lastPresentationResult = $result
            $script:lastPresentationCheck = Get-Date
            return $result
        }
        
        $script:lastPresentationResult = @{ IsPresenting = $false }
        $script:lastPresentationCheck = Get-Date
        return $script:lastPresentationResult
    }
    catch {
        return @{ IsPresenting = $false }
    }
}

function Update-PresentationModeState {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()

    if (-not $script:config.PresentationModeDetection) { return }
    
    $presentation = Test-PresentationMode
    
    if ($presentation.IsPresenting -and -not $script:state.Active -and -not $script:state.AutoPausedPresentation) {
        # Auto-start when presentation detected
        Set-ActiveState -Active:$true -ShowBalloon:$true
        Send-RedballToast -Title 'Redball Auto-Started' -Message "Keep-awake activated for $($presentation.Source) presentation."
        Write-RedballLog -Level 'INFO' -Message "Auto-started for presentation: $($presentation.Source)"
        $script:state.AutoPausedPresentation = $true
    }
    elseif (-not $presentation.IsPresenting -and $script:state.AutoPausedPresentation) {
        # Presentation ended
        $script:state.AutoPausedPresentation = $false
        # Don't auto-stop - user may want to keep it on
    }
}

# --- Performance Metrics ---

$script:performanceMetrics = @{
    StartTime      = $null
    HeartbeatCount = 0
    LastMetricLog  = $null
}

function Update-PerformanceMetrics {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string]$Action,
        [hashtable]$Data = @{}
    )
    
    if (-not $script:config.EnablePerformanceMetrics) { return }
    
    try {
        switch ($Action) {
            'Heartbeat' {
                $script:performanceMetrics.HeartbeatCount++
            }
            'Log' {
                $now = Get-Date
                # Only log every 5 minutes
                if ($script:performanceMetrics.LastMetricLog -and ($now - $script:performanceMetrics.LastMetricLog).TotalMinutes -lt 5) {
                    return
                }
                
                $process = Get-Process -Id $PID
                $uptime = if ($script:performanceMetrics.StartTime) {
                    ($now - $script:performanceMetrics.StartTime).ToString('hh\:mm\:ss')
                }
                else { 'N/A' }
                
                $metrics = @{
                    Timestamp  = $now.ToString('o')
                    Uptime     = $uptime
                    Heartbeats = $script:performanceMetrics.HeartbeatCount
                    CPU        = [math]::Round($process.TotalProcessorTime.TotalSeconds, 2)
                    MemoryMB   = [math]::Round($process.WorkingSet64 / 1MB, 2)
                    Handles    = $process.HandleCount
                    Data       = $Data
                }
                
                Write-RedballLog -Level 'INFO' -Message "METRICS: $($metrics | ConvertTo-Json -Compress)"
                $script:performanceMetrics.LastMetricLog = $now
            }
        }
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "Performance metrics collection skipped: $_"
    }
}

# --- Crash Recovery ---

function Test-CrashRecovery {
    param([string]$CrashFlagPath = (Join-Path $script:AppRoot 'Redball.crash.flag'))
    
    try {
        if (Test-Path $CrashFlagPath) {
            # Previous crash detected
            Write-RedballLog -Level 'WARN' -Message 'Crash recovery: Previous abnormal termination detected. Resetting to safe defaults.'
            
            # Reset to safe defaults
            $script:state.Active = $false
            $script:state.BatteryAware = $false
            $script:state.NetworkAware = $false
            $script:state.IdleDetection = $false
            $script:config.ScheduleEnabled = $false
            $script:config.PresentationModeDetection = $false
            
            # Clear the crash flag
            Remove-Item $CrashFlagPath -Force -ErrorAction SilentlyContinue
            
            Send-RedballToast -Title 'Redball Crash Recovery' -Message 'Previous crash detected. Settings reset to safe defaults.'
            return $true
        }
        
        # Set crash flag for this session
        'Running' | Set-Content -Path $CrashFlagPath -Force
        return $false
    }
    catch {
        return $false
    }
}

function Clear-CrashFlag {
    param([string]$CrashFlagPath = (Join-Path $script:AppRoot 'Redball.crash.flag'))
    try {
        if (Test-Path $CrashFlagPath) {
            Remove-Item $CrashFlagPath -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "Crash flag cleanup skipped: $_"
    }
}

# --- Execution Policy Handler ---

function Test-ExecutionPolicy {
    try {
        $currentPolicy = Get-ExecutionPolicy -Scope Process
        $effectivePolicy = Get-ExecutionPolicy
        
        if ($currentPolicy -eq 'Restricted' -or $currentPolicy -eq 'AllSigned') {
            Write-Warning "PowerShell Execution Policy is '$currentPolicy' which may block this script."
            Write-Output "To run this script, use one of these methods:"
            Write-Output "  1. Run: Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser"
            Write-Output "  2. Run with bypass: PowerShell -ExecutionPolicy Bypass -File .\Redball.ps1"
            Write-Output "  3. Use the -Install parameter to add to startup (handles bypass automatically)"
            
            Write-RedballLog -Level 'WARN' -Message "Execution policy may block script: $currentPolicy"
            return $false
        }
        
        Write-RedballLog -Level 'INFO' -Message "Execution policy check passed: $effectivePolicy"
        return $true
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to check execution policy: $_"
        return $true  # Assume OK if we can't check
    }
}

# --- Installer Defaults Import ---

function Import-RedballInstallerDefaults {
    try {
        $defaultsPath = 'HKCU:\Software\Redball\InstallerDefaults'
        if (-not (Test-Path $defaultsPath)) {
            return
        }

        $defaults = Get-ItemProperty -Path $defaultsPath -ErrorAction SilentlyContinue
        if (-not $defaults) {
            return
        }

        if ($defaults.BatteryAware -eq 1) {
            $script:state.BatteryAware = $true
        }
        if ($defaults.NetworkAware -eq 1) {
            $script:state.NetworkAware = $true
        }
        if ($defaults.IdleDetection -eq 1) {
            $script:state.IdleDetection = $true
        }
        if ($defaults.Minimized -eq 1) {
            $script:config.MinimizeOnStart = $true
        }
        if ($defaults.ExitOnComplete -eq 1) {
            $script:config.AutoExitOnComplete = $true
        }

        Write-RedballLog -Level 'INFO' -Message 'Applied installer defaults from registry.'
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to apply installer defaults: $_"
    }
}

# --- Digital Signature Verification ---

function New-RedballSelfSignedCodeSigningCertificate {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param()

    if (-not $PSCmdlet.ShouldProcess('Cert:\CurrentUser\My', 'Create self-signed code-signing certificate')) {
        return $null
    }

    try {
        $subject = 'CN=Redball Self-Signed Code Signing'
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $subject `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -KeyExportPolicy Exportable `
            -NotAfter (Get-Date).AddYears(3)

        if ($cert) {
            Write-RedballLog -Level 'INFO' -Message "Created self-signed code-signing certificate: $($cert.Thumbprint)"
        }

        return $cert
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to create self-signed code-signing certificate: $_"
        return $null
    }
}

function Get-RedballCodeSigningCertificate {
    param(
        [string]$Thumbprint
    )

    try {
        $certs = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue
        if (-not $certs) {
            return $null
        }

        if ($Thumbprint) {
            return $certs | Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1
        }

        return $certs | Sort-Object NotAfter -Descending | Select-Object -First 1
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to enumerate code-signing certificates: $_"
        return $null
    }
}

function Set-RedballCodeSignature {
    <#
    .SYNOPSIS
        Signs a PowerShell script using an Authenticode certificate.
    .DESCRIPTION
        Finds a code-signing certificate by thumbprint (or latest valid cert)
        and applies Set-AuthenticodeSignature to the target script.
    .PARAMETER Path
        Script path to sign.
    .PARAMETER Thumbprint
        Optional certificate thumbprint to force selection.
    .PARAMETER TimestampServer
        Optional RFC3161 timestamp server URL.
    .EXAMPLE
        Set-RedballCodeSignature -Path '.\Redball.ps1'
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [string]$Thumbprint,
        [string]$TimestampServer = 'http://timestamp.digicert.com'
    )

    try {
        if (-not (Test-Path $Path)) {
            Write-Output "Sign failed: file not found: $Path"
            return $false
        }

        $cert = Get-RedballCodeSigningCertificate -Thumbprint $Thumbprint
        if (-not $cert) {
            if (-not $Thumbprint) {
                Write-Output 'No code-signing certificate found. Creating a self-signed certificate...'
                $cert = New-RedballSelfSignedCodeSigningCertificate
            }

            if (-not $cert) {
                Write-Output 'Sign failed: no usable code-signing certificate found/created in Cert:\CurrentUser\My.'
                return $false
            }
        }

        if (-not $PSCmdlet.ShouldProcess($Path, 'Apply Authenticode signature')) {
            return $false
        }

        $result = Set-AuthenticodeSignature -FilePath $Path -Certificate $cert -TimestampServer $TimestampServer
        if ($result.Status -eq 'Valid') {
            Write-RedballLog -Level 'INFO' -Message "Signed script: $Path using cert $($cert.Thumbprint)"
            Write-Output "Signed successfully: $Path"
            return $true
        }

        Write-RedballLog -Level 'WARN' -Message "Signing failed for $Path. Status: $($result.Status)"
        Write-Output "Sign failed. Status: $($result.Status)"
        return $false
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Script signing error: $_"
        Write-Output "Sign failed: $_"
        return $false
    }
}

function Test-RedballFileSignature {
    <#
    .SYNOPSIS
        Verifies Authenticode signature of a downloaded file.
    .DESCRIPTION
        Uses Get-AuthenticodeSignature to verify file signature status and
        optionally enforce an allowed signer thumbprint list.
    .PARAMETER Path
        File path to verify.
    .PARAMETER AllowedThumbprints
        Optional list of trusted signer certificate thumbprints.
    .EXAMPLE
        Test-RedballFileSignature -Path '.\Redball.ps1'
    .EXAMPLE
        Test-RedballFileSignature -Path '.\update.ps1' -AllowedThumbprints @('ABC123...')
    #>
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [string[]]$AllowedThumbprints = @()
    )

    try {
        if (-not (Test-Path $Path)) {
            Write-RedballLog -Level 'WARN' -Message "Signature check failed: file not found: $Path"
            return $false
        }

        $signature = Get-AuthenticodeSignature -FilePath $Path
        if (-not $signature) {
            Write-RedballLog -Level 'WARN' -Message "Signature check failed: unable to read signature for $Path"
            return $false
        }

        if ($signature.Status -ne 'Valid') {
            Write-RedballLog -Level 'WARN' -Message "Signature invalid for $Path. Status: $($signature.Status)"
            return $false
        }

        if ($AllowedThumbprints.Count -gt 0) {
            $thumbprint = $signature.SignerCertificate.Thumbprint
            if (-not $thumbprint -or ($AllowedThumbprints -notcontains $thumbprint)) {
                Write-RedballLog -Level 'WARN' -Message "Signature signer not trusted for $Path. Thumbprint: $thumbprint"
                return $false
            }
        }

        Write-RedballLog -Level 'INFO' -Message "Signature verified for $Path"
        return $true
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Signature verification error for ${Path}: $_"
        return $false
    }
}

# --- Auto-Updater ---

function Get-RedballLatestRelease {
    param([switch]$Force)
    try {
        # Check if updates are disabled
        if ($script:config.UpdateChannel -eq 'disabled') {
            Write-VerboseLog -Message "Update check skipped - channel is disabled" -Source "Update"
            return $null
        }

        # Rate limiting: return cached result if checked within 5 minutes
        if (-not $Force -and $script:lastUpdateCheck -and $script:lastUpdateResult) {
            $elapsed = (Get-Date) - $script:lastUpdateCheck
            if ($elapsed.TotalMinutes -lt 5) {
                Write-RedballLog -Level 'DEBUG' -Message "Returning cached update check result (age: $([math]::Round($elapsed.TotalSeconds))s)"
                return $script:lastUpdateResult
            }
        }

        $owner = $script:config.UpdateRepoOwner
        $repo = $script:config.UpdateRepoName

        # Validate repo owner/name to prevent update redirection attacks
        if ($owner -notmatch '^[a-zA-Z0-9_.-]+$' -or $repo -notmatch '^[a-zA-Z0-9_.-]+$') {
            Write-RedballLog -Level 'WARN' -Message "Invalid update repo owner/name: $owner/$repo"
            return $null
        }
        if ($owner -ne 'karl-lawrence' -or $repo -ne 'Redball') {
            Write-RedballLog -Level 'WARN' -Message "Non-default update repo: $owner/$repo (default: karl-lawrence/Redball)"
        }

        $uri = "https://api.github.com/repos/$owner/$repo/releases/latest"

        $headers = @{ 'User-Agent' = 'Redball-Updater' }
        $release = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get -TimeoutSec 15

        if (-not $release -or -not $release.tag_name) {
            return $null
        }

        # Cache the result
        $script:lastUpdateCheck = Get-Date
        $script:lastUpdateResult = $release

        return $release
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to fetch latest release info: $_"
        return $null
    }
}

function Test-RedballUpdateAvailable {
    try {
        # Check if updates are disabled
        if ($script:config.UpdateChannel -eq 'disabled') {
            Write-VerboseLog -Message "Update check disabled by configuration" -Source "Update"
            return @{
                UpdateAvailable = $false
                CurrentVersion  = $script:VERSION
                LatestVersion   = $null
                Release         = $null
                Reason          = 'Update checks are disabled'
            }
        }

        $release = Get-RedballLatestRelease
        if (-not $release) {
            return @{
                UpdateAvailable = $false
                CurrentVersion  = $script:VERSION
                LatestVersion   = $null
                Release         = $null
                Reason          = 'Unable to query latest release'
            }
        }

        $current = [version]($script:VERSION -replace '^v', '')
        $latest = [version]($release.tag_name -replace '^v', '')

        return @{
            UpdateAvailable = ($latest -gt $current)
            CurrentVersion  = $current.ToString()
            LatestVersion   = $latest.ToString()
            Release         = $release
            Reason          = $null
        }
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to evaluate update status: $_"
        return @{
            UpdateAvailable = $false
            CurrentVersion  = $script:VERSION
            LatestVersion   = $null
            Release         = $null
            Reason          = "Version parse/evaluation failed: $_"
        }
    }
}

function Install-RedballUpdate {
    param(
        [switch]$RestartAfterUpdate
    )

    try {
        $status = Test-RedballUpdateAvailable
        if (-not $status.UpdateAvailable) {
            Write-Output 'No update available.'
            return $false
        }

        $release = $status.Release
        $asset = $release.assets | Where-Object { $_.name -match 'Redball\.ps1$' } | Select-Object -First 1
        if (-not $asset) {
            Write-RedballLog -Level 'WARN' -Message 'Update failed: no Redball.ps1 asset found in latest release.'
            Write-Output 'Update asset not found in release.'
            return $false
        }

        $tempPath = Join-Path $env:TEMP $asset.name
        $scriptPath = Join-Path $script:AppRoot 'Redball.ps1'
        $backupPath = "$scriptPath.bak.$(Get-Date -Format 'yyyyMMddHHmmss')"

        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tempPath -UseBasicParsing -TimeoutSec 30

        if ($script:config.VerifyUpdateSignature) {
            if (-not (Test-RedballFileSignature -Path $tempPath)) {
                Write-Output 'Downloaded update failed signature verification.'
                return $false
            }
        }

        Copy-Item -Path $scriptPath -Destination $backupPath -Force
        Copy-Item -Path $tempPath -Destination $scriptPath -Force

        # Clean up temp file
        Remove-Item $tempPath -Force -ErrorAction SilentlyContinue

        Write-RedballLog -Level 'INFO' -Message "Updated Redball from $($status.CurrentVersion) to $($status.LatestVersion). Backup: $backupPath"
        Write-Output "Update installed. Backup created: $backupPath"

        if ($RestartAfterUpdate) {
            Start-Process -FilePath 'powershell.exe' -ArgumentList "-ExecutionPolicy Bypass -File `"$scriptPath`""
            # Exit current instance cleanly
            Exit-Application
        }

        return $true
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to install update: $_"
        Write-Output "Update failed: $_"
        return $false
    }
}

# --- Backup/Restore Settings ---

function Export-RedballSettings {
    param(
        [string]$Path = (Join-Path $script:AppRoot 'Redball.backup.json'),
        [switch]$Obfuscate = $false
    )
    try {
        $settings = @{
            Config     = $script:config
            State      = @{
                BatteryAware         = $script:state.BatteryAware
                BatteryThreshold     = $script:state.BatteryThreshold
                NetworkAware         = $script:state.NetworkAware
                IdleDetection        = $script:state.IdleDetection
                IdleThresholdMinutes = $script:state.IdleThresholdMinutes
            }
            ExportedAt = (Get-Date).ToString('o')
            Version    = $script:VERSION
        }
        
        $json = $settings | ConvertTo-Json -Depth 5
        
        if ($Obfuscate) {
            # Base64 obfuscation (not true encryption — do not rely on this for security)
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
            $encoded = [Convert]::ToBase64String($bytes)
            $encoded | Set-Content -Path $Path -Encoding UTF8
        }
        else {
            $json | Set-Content -Path $Path -Encoding UTF8
        }
        
        Write-RedballLog -Level 'INFO' -Message "Settings exported to: $Path"
        return $true
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to export settings: $_"
        return $false
    }
}

function Import-RedballSettings {
    param(
        [string]$Path = (Join-Path $script:AppRoot 'Redball.backup.json'),
        [switch]$Obfuscated = $false
    )
    try {
        if (-not (Test-Path $Path)) {
            Write-RedballLog -Level 'WARN' -Message "Backup file not found: $Path"
            return $false
        }
        
        $content = Get-Content $Path -Raw -Encoding UTF8
        
        if ($Obfuscated) {
            # Decode from base64 obfuscation
            $bytes = [Convert]::FromBase64String($content)
            $json = [System.Text.Encoding]::UTF8.GetString($bytes)
            $settings = $json | ConvertFrom-Json
        }
        else {
            $settings = $content | ConvertFrom-Json
        }
        
        # Restore config values
        foreach ($key in $settings.Config.PSObject.Properties.Name) {
            if ($script:config.ContainsKey($key)) {
                $script:config[$key] = $settings.Config.$key
            }
        }
        
        # Restore state values
        foreach ($key in $settings.State.PSObject.Properties.Name) {
            if ($script:state.ContainsKey($key)) {
                $script:state[$key] = $settings.State.$key
            }
        }
        
        Write-RedballLog -Level 'INFO' -Message "Settings imported from: $Path (exported: $($settings.ExportedAt))"
        return $true
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to import settings: $_"
        return $false
    }
}

# --- High Contrast Mode Detection ---

function Test-HighContrastMode {
    try {
        # Check Windows high contrast theme
        $hcTheme = Get-ItemProperty -Path 'HKCU:\Control Panel\Accessibility\HighContrast' -Name 'Flags' -ErrorAction SilentlyContinue
        if ($hcTheme -and $hcTheme.Flags -ne 0) {
            return $true
        }
        
        # Also check if high contrast is enabled via SystemParametersInfo
        if (-not ([System.Management.Automation.PSTypeName]'HighContrastHelper').Type) {
            Add-Type @"
using System;
using System.Runtime.InteropServices;
public class HighContrastHelper {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref HIGHCONTRAST pvParam, uint fWinIni);
    
    public const uint SPI_GETHIGHCONTRAST = 0x0042;
    public const uint HCF_HIGHCONTRASTON = 0x00000001;
    
    [StructLayout(LayoutKind.Sequential)]
    public struct HIGHCONTRAST {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }
    
    public static bool IsHighContrast() {
        HIGHCONTRAST hc = new HIGHCONTRAST();
        hc.cbSize = (uint)Marshal.SizeOf(typeof(HIGHCONTRAST));
        if (SystemParametersInfo(SPI_GETHIGHCONTRAST, 0, ref hc, 0)) {
            return (hc.dwFlags & HCF_HIGHCONTRASTON) == HCF_HIGHCONTRASTON;
        }
        return false;
    }
}
"@ -ErrorAction SilentlyContinue
        }
        
        return [HighContrastHelper]::IsHighContrast()
    }
    catch {
        return $false
    }
}

function Update-HighContrastUI {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()

    if (-not (Test-HighContrastMode)) { return }
    
    try {
        # In high contrast mode, use system icons instead of custom drawn ones
        $script:config.UseSystemIcons = $true
        Write-RedballLog -Level 'INFO' -Message 'High contrast mode detected - using system icons'
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "High contrast UI update skipped: $_"
    }
}

# --- High DPI Awareness ---

function Enable-HighDPI {
    try {
        # Set DPI awareness for the process
        if (-not ([System.Management.Automation.PSTypeName]'DPIHelper').Type) {
            Add-Type @"
using System;
using System.Runtime.InteropServices;
public class DPIHelper {
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
    
    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(int dpiContext);
    
    public const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = -3;
    public const int DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = -2;
    
    public static void EnableHighDPI() {
        try {
            // Try modern Windows 10/11 API first
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE);
        }
        catch {
            // Fallback to legacy API
            SetProcessDPIAware();
        }
    }
}
"@ -ErrorAction SilentlyContinue
        }
        
        [DPIHelper]::EnableHighDPI()
        Write-RedballLog -Level 'INFO' -Message 'High DPI awareness enabled'
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to enable High DPI: $_"
    }
}

# --- Dark Mode Support ---

function Test-DarkMode {
    try {
        # Check Windows 10/11 dark mode setting
        $theme = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'AppsUseLightTheme' -ErrorAction SilentlyContinue
        if ($theme) {
            return ($theme.AppsUseLightTheme -eq 0)
        }
        return $false
    }
    catch {
        return $false
    }
}

function Update-DarkModeUI {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()

    try {
        $isDarkMode = Test-DarkMode
        if ($isDarkMode) {
            Write-RedballLog -Level 'INFO' -Message 'Dark mode detected'
        }
        return $isDarkMode
    }
    catch {
        return $false
    }
}

# --- Telemetry (Opt-in) ---

function Write-RedballTelemetryEvent {
    param(
        [string]$TelemetryEvent,
        [hashtable]$Data = @{}
    )
    
    # Only send if user has opted in
    if (-not $script:config.EnableTelemetry) { return }
    
    try {
        $telemetry = @{
            Version   = $script:VERSION
            Event     = $TelemetryEvent
            Timestamp = (Get-Date).ToString('o')
            SessionId = $script:state.SessionId
            Data      = $Data
        } | ConvertTo-Json -Compress
        
        # Log locally (in a real implementation, this would send to analytics endpoint)
        Write-RedballLog -Level 'DEBUG' -Message "TELEMETRY: $telemetry"
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "Telemetry dispatch skipped: $_"
    }
}

# --- Process Isolation ---

function Start-KeepAwakeRunspace {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param()

    if (-not $PSCmdlet.ShouldProcess('Redball process isolation worker', 'Start keep-awake runspace')) {
        return $null
    }

    try {
        # Create a new runspace for keep-awake logic
        $runspace = [runspacefactory]::CreateRunspace()
        $runspace.Open()
        
        # Set up the PowerShell instance
        $powershell = [powershell]::Create()
        $powershell.Runspace = $runspace
        
        # Add script to maintain keep-awake state
        $powershell.AddScript({
                param($Interval)

                $signature = @"
using System;
using System.Runtime.InteropServices;
namespace Win32 {
    public static class Power {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern uint SetThreadExecutionState(uint esFlags);
    }
}
"@
                Add-Type -TypeDefinition $signature -Language CSharp -ErrorAction SilentlyContinue

                while ($true) {
                    # 0x80000003 = ES_CONTINUOUS (0x80000000) | ES_SYSTEM_REQUIRED (0x1) | ES_DISPLAY_REQUIRED (0x2)
                    [Win32.Power]::SetThreadExecutionState(0x80000003)
                    Start-Sleep -Seconds $Interval
                }
            }).AddArgument($script:config.HeartbeatSeconds)
        
        # Start asynchronously
        $asyncResult = $powershell.BeginInvoke()
        
        Write-RedballLog -Level 'INFO' -Message 'Keep-awake runspace started for process isolation'
        return @{ Runspace = $runspace; PowerShell = $powershell; AsyncResult = $asyncResult }
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to start keep-awake runspace: $_"
        return $null
    }
}

function Stop-KeepAwakeRunspace {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param([hashtable]$RunspaceInfo)

    if (-not $PSCmdlet.ShouldProcess('Redball process isolation worker', 'Stop keep-awake runspace')) {
        return
    }
    
    try {
        if ($RunspaceInfo.PowerShell) {
            $RunspaceInfo.PowerShell.Stop()
            $RunspaceInfo.PowerShell.Dispose()
        }
        if ($RunspaceInfo.Runspace) {
            $RunspaceInfo.Runspace.Close()
            $RunspaceInfo.Runspace.Dispose()
        }
        Write-RedballLog -Level 'INFO' -Message 'Keep-awake runspace stopped'
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to stop keep-awake runspace: $_"
    }
}

$toggleMenuItem.add_Click({
        try {
            if ($script:state.IsShuttingDown) { return }
            Switch-ActiveState
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "Toggle error: $_"
        }
    })

$displayMenuItem.add_Click({
        try {
            if ($script:state.IsShuttingDown) { return }
            $script:state.PreventDisplaySleep = $displayMenuItem.Checked
            if ($script:state.Active) {
                Set-KeepAwakeState -Enable:$true
            }
            Update-RedballUI
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "Display toggle error: $_"
        }
    })

$heartbeatMenuItem.add_Click({
        try {
            if ($script:state.IsShuttingDown) { return }
            $script:state.UseHeartbeatKeypress = $heartbeatMenuItem.Checked
            Update-RedballUI
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "Heartbeat toggle error: $_"
        }
    })

$exitMenuItem.add_Click({
        try {
            Exit-Application
        }
        catch {
            # Force exit if graceful fails
            [Environment]::Exit(0)
        }
    })

$notifyIcon.add_DoubleClick({
        try {
            if ($script:state.IsShuttingDown) { return }
            Switch-ActiveState
        }
        catch {
            Write-RedballLog -Level 'WARN' -Message "Double-click error: $_"
        }
    })

# Note: Global hotkey support would require a proper NativeWindow subclass with WndProc override
# This is disabled for now to prevent runtime errors

$script:state.Context = New-Object System.Windows.Forms.ApplicationContext

# Handle CLI parameters first
if ($Status) {
    # Return status as JSON and exit
    Write-Output (Get-RedballStatus)
    exit 0
}

if ($CheckUpdate) {
    $updateStatus = Test-RedballUpdateAvailable
    Write-Output ($updateStatus | ConvertTo-Json -Depth 6)
    exit 0
}

if ($Update) {
    $result = Install-RedballUpdate -RestartAfterUpdate
    if ($result) { exit 0 } else { exit 1 }
}

if ($SignScript) {
    $signed = Set-RedballCodeSignature -Path $SignPath -Thumbprint $CertThumbprint -TimestampServer $TimestampServer
    if ($signed) { exit 0 } else { exit 1 }
}

if ($Install) {
    $result = Install-RedballStartup
    if ($result) {
        Write-Output 'Redball installed to Windows startup.'
    }
    else {
        Write-Output 'Failed to install Redball startup.'
        exit 1
    }
    exit 0
}

if ($Uninstall) {
    $result = Uninstall-RedballStartup
    if ($result) {
        Write-Output 'Redball removed from Windows startup.'
    }
    else {
        Write-Output 'Redball was not in startup.'
    }
    exit 0
}

# Handle modern UI mode
if ($UseModernUI) {
    Write-RedballLog -Level 'INFO' -Message 'Starting Redball v3.0 with modern WPF UI'
    try {
        $wpfPath = Join-Path $script:AppRoot 'dist\wpf-publish\Redball.UI.WPF.exe'
        if (Test-Path $wpfPath) {
            Start-Process -FilePath $wpfPath -WindowStyle Hidden
            Write-Output 'Redball v3.0 modern UI started. Use tray icon to control.'
            exit 0
        }
        else {
            Write-Warning "Modern UI not found at: $wpfPath"
            Write-Output 'Falling back to classic PowerShell UI...'
        }
    }
    catch {
        Write-Warning "Failed to start modern UI: $_"
        Write-Output 'Falling back to classic PowerShell UI...'
    }
}

# Check execution policy before running
Test-ExecutionPolicy | Out-Null

# --- Singleton Instance Check ---
# Prevent multiple instances from running simultaneously
if (Test-RedballInstanceRunning) {
    # Another instance is running - try to stop it gracefully
    Write-Warning 'Another Redball instance is already running. Attempting to stop it...'
    
    $processes = Get-RedballProcess
    $stopped = 0
    foreach ($proc in $processes) {
        if (Stop-RedballProcess -ProcessId $proc.ProcessId) {
            $stopped++
        }
    }
    
    if ($stopped -gt 0) {
        Write-Output "Stopped $stopped existing Redball process(es). Starting new instance..."
        # Give processes time to fully exit
        Start-Sleep -Milliseconds 1500
    }
    else {
        Write-Error 'Unable to stop existing Redball instance. Please close it manually or restart your computer.'
        exit 1
    }
}

# Claim the singleton mutex
if (-not (Initialize-RedballSingleton)) {
    Write-Error 'Failed to initialize singleton instance. Another Redball may have started concurrently.'
    exit 1
}

# Clear any log file locks from previous instances
Clear-RedballLogLock | Out-Null

# Load persisted config before applying runtime state logic.
Import-RedballConfig -Path $ConfigPath

# Validate and sanitize config values (Security + Backend)
Test-RedballConfigSchema | Out-Null

# Check for crash recovery
Test-CrashRecovery | Out-Null

# Restore previous session state if exists
$restoredState = Restore-RedballState

# If no saved state, apply installer-selected defaults
if (-not $restoredState) {
    Import-RedballInstallerDefaults
}

# Apply CLI parameters
if ($script:config.MinimizeOnStart -or $Minimized -or $Duration) {
    $script:state.NotifyIcon.Visible = $true
}

if ($Duration) {
    $until = (Get-Date).AddMinutes($Duration)
    Set-ActiveState -Active:$true -Until $until -ShowBalloon:$false
    if ($ExitOnComplete) {
        $script:config.AutoExitOnComplete = $true
    }
}
else {
    Set-ActiveState -Active:$true -Until $null -ShowBalloon:$false
}

# Initialize locales
Import-RedballLocales

# Record start time for uptime tracking
$script:state.StartTime = Get-Date
$script:performanceMetrics.StartTime = Get-Date

Update-RedballUI

# First-run onboarding (UX)
Test-RedballFirstRun | Out-Null

# --- TypeThing Functions ---

function ConvertTo-HotkeyParams {
    <#
    .SYNOPSIS
        Parses a hotkey string like "Ctrl+Shift+V" into modifier flags and virtual key code.
    .DESCRIPTION
        Converts a human-readable hotkey string (e.g., "Ctrl+Alt+V") into the numeric
        modifier flags and virtual key code required by the Windows RegisterHotKey API.
        Supports: Ctrl, Alt, Shift, Win modifiers and alphanumeric keys, function keys,
        and special keys (Space, Enter, Tab, etc.).
    .PARAMETER HotkeyString
        The hotkey string to parse (e.g., "Ctrl+Shift+V", "Alt+F4")
    .EXAMPLE
        $params = ConvertTo-HotkeyParams -HotkeyString "Ctrl+Alt+V"
        # Returns: @{ Modifiers = 3; VirtualKey = 0x56 }
    .NOTES
        Modifier flags: Ctrl=0x0002, Alt=0x0001, Shift=0x0004, Win=0x0008
        Multiple modifiers are combined using bitwise OR.
    #>
    param(
        [Parameter(Mandatory)]
        [string]$HotkeyString
    )
    Write-VerboseLog -Message "ConvertTo-HotkeyParams called with: '$HotkeyString'" -Source "Hotkey"
    
    $parts = $HotkeyString -split '\+'
    [uint32]$modifiers = 0
    [uint32]$vk = 0

    $vkMap = @{
        'A' = 0x41; 'B' = 0x42; 'C' = 0x43; 'D' = 0x44; 'E' = 0x45; 'F' = 0x46; 'G' = 0x47; 'H' = 0x48
        'I' = 0x49; 'J' = 0x4A; 'K' = 0x4B; 'L' = 0x4C; 'M' = 0x4D; 'N' = 0x4E; 'O' = 0x4F; 'P' = 0x50
        'Q' = 0x51; 'R' = 0x52; 'S' = 0x53; 'T' = 0x54; 'U' = 0x55; 'V' = 0x56; 'W' = 0x57; 'X' = 0x58
        'Y' = 0x59; 'Z' = 0x5A
        '0' = 0x30; '1' = 0x31; '2' = 0x32; '3' = 0x33; '4' = 0x34; '5' = 0x35; '6' = 0x36; '7' = 0x37
        '8' = 0x38; '9' = 0x39
        'F1' = 0x70; 'F2' = 0x71; 'F3' = 0x72; 'F4' = 0x73; 'F5' = 0x74; 'F6' = 0x75; 'F7' = 0x76
        'F8' = 0x77; 'F9' = 0x78; 'F10' = 0x79; 'F11' = 0x7A; 'F12' = 0x7B
        'Pause' = 0x13; 'Space' = 0x20; 'Escape' = 0x1B; 'Enter' = 0x0D; 'Tab' = 0x09
        'Insert' = 0x2D; 'Delete' = 0x2E; 'Home' = 0x24; 'End' = 0x23
        'PageUp' = 0x21; 'PageDown' = 0x22
        'Left' = 0x25; 'Up' = 0x26; 'Right' = 0x27; 'Down' = 0x28
        'NumPad0' = 0x60; 'NumPad1' = 0x61; 'NumPad2' = 0x62; 'NumPad3' = 0x63; 'NumPad4' = 0x64
        'NumPad5' = 0x65; 'NumPad6' = 0x66; 'NumPad7' = 0x67; 'NumPad8' = 0x68; 'NumPad9' = 0x69
        'OemTilde' = 0xC0; 'OemMinus' = 0xBD; 'OemPlus' = 0xBB
    }

    Write-VerboseLog -Message "Parsing $($parts.Count) hotkey parts" -Source "Hotkey"
    
    foreach ($part in $parts) {
        $trimmed = $part.Trim()
        Write-VerboseLog -Message "Processing part: '$trimmed'" -Source "Hotkey"
        switch ($trimmed) {
            'Ctrl' { 
                $modifiers = $modifiers -bor 0x0002 
                Write-VerboseLog -Message "  -> Added Ctrl modifier (0x0002), total modifiers: $modifiers" -Source "Hotkey"
            }
            'Control' { 
                $modifiers = $modifiers -bor 0x0002 
                Write-VerboseLog -Message "  -> Added Control modifier (0x0002), total modifiers: $modifiers" -Source "Hotkey"
            }
            'Alt' { 
                $modifiers = $modifiers -bor 0x0001 
                Write-VerboseLog -Message "  -> Added Alt modifier (0x0001), total modifiers: $modifiers" -Source "Hotkey"
            }
            'Shift' { 
                $modifiers = $modifiers -bor 0x0004 
                Write-VerboseLog -Message "  -> Added Shift modifier (0x0004), total modifiers: $modifiers" -Source "Hotkey"
            }
            'Win' { 
                $modifiers = $modifiers -bor 0x0008 
                Write-VerboseLog -Message "  -> Added Win modifier (0x0008), total modifiers: $modifiers" -Source "Hotkey"
            }
            default {
                if ($vkMap.ContainsKey($trimmed)) {
                    $vk = $vkMap[$trimmed]
                    Write-VerboseLog -Message "  -> Found virtual key for '$trimmed': 0x$($vk.ToString('X2'))" -Source "Hotkey"
                }
                else {
                    Write-RedballLog -Level 'WARN' -Message "TypeThing: Unknown hotkey part '$trimmed'"
                    Write-VerboseLog -Message "  -> Unknown hotkey part: '$trimmed'" -Source "Hotkey"
                }
            }
        }
    }
    
    Write-VerboseLog -Message "Hotkey '$HotkeyString' parsed -> Modifiers: $modifiers, VK: 0x$($vk.ToString('X2'))" -Source "Hotkey"
    return @{ Modifiers = $modifiers; VirtualKey = $vk }
}

function Register-TypeThingHotkeys {
    <#
    .SYNOPSIS
        Registers TypeThing global hotkeys for start and stop typing.
    .DESCRIPTION
        Registers global Windows hotkeys using the RegisterHotKey API.
        Includes special handling and diagnostics for Alt key combinations.
        Alt hotkeys can be problematic because Windows uses Alt for menu access.
    .NOTES
        Alt modifier = 0x0001 (MOD_ALT)
        Known issue: Some Alt combinations may be intercepted by Windows for menu access.
        If Alt hotkeys fail, try using Ctrl+Alt combinations instead.
    #>
    Write-VerboseLog -Message "Register-TypeThingHotkeys called" -Source "Hotkey"
    
    if (-not $script:config.TypeThingEnabled) { 
        Write-VerboseLog -Message "TypeThing disabled, skipping hotkey registration" -Source "Hotkey"
        return 
    }
    if ($script:state.TypeThingHotkeysRegistered) { 
        Write-VerboseLog -Message "Hotkeys already registered, skipping" -Source "Hotkey"
        return 
    }
    if (-not $script:state.TypeThingHotkeyWindow) { 
        Write-VerboseLog -Message "Hotkey window not created yet, cannot register" -Source "Hotkey"
        return 
    }

    $handle = $script:state.TypeThingHotkeyWindow.Handle
    Write-VerboseLog -Message "Hotkey window handle: $handle" -Source "Hotkey"
    
    if ($handle -eq [IntPtr]::Zero) {
        Write-RedballLog -Level 'WARN' -Message "TypeThing: Hotkey window handle not ready, skipping registration"
        Write-VerboseLog -Message "Handle is Zero, registration aborted" -Source "Hotkey"
        return
    }

    $startRegistered = $false
    $stopRegistered = $false

    try {
        Write-VerboseLog -Message "=== START HOTKEY REGISTRATION ===" -Source "Hotkey"
        Write-VerboseLog -Message "Config start hotkey: $($script:config.TypeThingStartHotkey)" -Source "Hotkey"
        $startParams = ConvertTo-HotkeyParams -HotkeyString $script:config.TypeThingStartHotkey
        
        # Check for Alt modifier specifically
        $hasAlt = ($startParams.Modifiers -band 0x0001) -ne 0
        if ($hasAlt) {
            Write-VerboseLog -Message "WARNING: Start hotkey uses Alt modifier - this may conflict with Windows menu access" -Source "Hotkey"
            Write-VerboseLog -Message "  Alt hotkey registration can fail if the combination is reserved by Windows" -Source "Hotkey"
        }
        
        Write-VerboseLog -Message "Start hotkey parsed - Modifiers: $($startParams.Modifiers) (Alt:$hasAlt Ctrl:$((($startParams.Modifiers -band 0x0002) -ne 0)) Shift:$((($startParams.Modifiers -band 0x0004) -ne 0)) Win:$((($startParams.Modifiers -band 0x0008) -ne 0))) VK: 0x$($startParams.VirtualKey.ToString('X2'))" -Source "Hotkey"
        
        if ($startParams.VirtualKey -gt 0) {
            Write-VerboseLog -Message "Calling RegisterHotKey API for START hotkey" -Source "Hotkey"
            $result = [HotkeyHelper]::RegisterHotKey($handle, $script:state.TypeThingHotkeyStartId,
                $startParams.Modifiers, $startParams.VirtualKey)
            if ($result) {
                $startRegistered = $true
                Write-RedballLog -Level 'INFO' -Message "TypeThing: Start hotkey registered ($($script:config.TypeThingStartHotkey))"
                Write-VerboseLog -Message "Start hotkey registered successfully" -Source "Hotkey"
            }
            else {
                $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
                $errMsg = switch ($err) {
                    1409 { "ERROR_HOTKEY_ALREADY_REGISTERED (1409) - Hotkey is already registered by another application" }
                    5 { "ERROR_ACCESS_DENIED (5) - Insufficient privileges" }
                    87 { "ERROR_INVALID_PARAMETER (87) - Invalid virtual key or modifiers" }
                    default { "Unknown error code: $err" }
                }
                Write-RedballLog -Level 'WARN' -Message "TypeThing: Failed to register start hotkey ($($script:config.TypeThingStartHotkey)) - $errMsg"
                Write-VerboseLog -Message "RegisterHotKey failed with Win32 error $err - $errMsg" -Source "Hotkey"
                
                if ($hasAlt) {
                    Write-VerboseLog -Message "  HINT: Alt hotkeys often fail. Try Ctrl+Alt instead of just Alt" -Source "Hotkey"
                }
            }
        }
        else {
            Write-RedballLog -Level 'WARN' -Message "TypeThing: Start hotkey '$($script:config.TypeThingStartHotkey)' parsed to VK=0 - check hotkey string format"
            Write-VerboseLog -Message "VirtualKey is 0, invalid hotkey format" -Source "Hotkey"
        }

        Write-VerboseLog -Message "=== STOP HOTKEY REGISTRATION ===" -Source "Hotkey"
        Write-VerboseLog -Message "Config stop hotkey: $($script:config.TypeThingStopHotkey)" -Source "Hotkey"
        $stopParams = ConvertTo-HotkeyParams -HotkeyString $script:config.TypeThingStopHotkey
        
        # Check for Alt modifier specifically
        $hasAlt = ($stopParams.Modifiers -band 0x0001) -ne 0
        if ($hasAlt) {
            Write-VerboseLog -Message "WARNING: Stop hotkey uses Alt modifier - this may conflict with Windows menu access" -Source "Hotkey"
        }
        
        Write-VerboseLog -Message "Stop hotkey parsed - Modifiers: $($stopParams.Modifiers) (Alt:$hasAlt Ctrl:$((($stopParams.Modifiers -band 0x0002) -ne 0)) Shift:$((($stopParams.Modifiers -band 0x0004) -ne 0)) Win:$((($stopParams.Modifiers -band 0x0008) -ne 0))) VK: 0x$($stopParams.VirtualKey.ToString('X2'))" -Source "Hotkey"
        
        if ($stopParams.VirtualKey -gt 0) {
            Write-VerboseLog -Message "Calling RegisterHotKey API for STOP hotkey" -Source "Hotkey"
            $result = [HotkeyHelper]::RegisterHotKey($handle, $script:state.TypeThingHotkeyStopId,
                $stopParams.Modifiers, $stopParams.VirtualKey)
            if ($result) {
                $stopRegistered = $true
                Write-RedballLog -Level 'INFO' -Message "TypeThing: Stop hotkey registered ($($script:config.TypeThingStopHotkey))"
                Write-VerboseLog -Message "Stop hotkey registered successfully" -Source "Hotkey"
            }
            else {
                $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
                $errMsg = switch ($err) {
                    1409 { "ERROR_HOTKEY_ALREADY_REGISTERED (1409) - Hotkey is already registered by another application" }
                    5 { "ERROR_ACCESS_DENIED (5) - Insufficient privileges" }
                    87 { "ERROR_INVALID_PARAMETER (87) - Invalid virtual key or modifiers" }
                    default { "Unknown error code: $err" }
                }
                Write-RedballLog -Level 'WARN' -Message "TypeThing: Failed to register stop hotkey ($($script:config.TypeThingStopHotkey)) - $errMsg"
                Write-VerboseLog -Message "RegisterHotKey failed with Win32 error $err - $errMsg" -Source "Hotkey"
                
                if ($hasAlt) {
                    Write-VerboseLog -Message "  HINT: Alt hotkeys often fail. Try Ctrl+Alt instead of just Alt" -Source "Hotkey"
                }
            }
        }
        else {
            Write-RedballLog -Level 'WARN' -Message "TypeThing: Stop hotkey '$($script:config.TypeThingStopHotkey)' parsed to VK=0 - check hotkey string format"
            Write-VerboseLog -Message "VirtualKey is 0, invalid hotkey format" -Source "Hotkey"
        }

        # Only mark as registered if at least one hotkey was successfully registered
        if ($startRegistered -or $stopRegistered) {
            $script:state.TypeThingHotkeysRegistered = $true
            Write-VerboseLog -Message "Hotkeys marked as registered (start:$startRegistered stop:$stopRegistered)" -Source "Hotkey"
        }
        else {
            Write-VerboseLog -Message "No hotkeys were registered successfully" -Source "Hotkey"
        }
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "TypeThing: Hotkey registration error: $_"
        Write-VerboseLog -Message "Exception during registration: $_" -Source "Hotkey"
    }
}

function Unregister-TypeThingHotkeys {
    <#
    .SYNOPSIS
        Unregisters TypeThing global hotkeys.
    #>
    if (-not $script:state.TypeThingHotkeysRegistered) { return }
    if (-not $script:state.TypeThingHotkeyWindow) { return }

    $handle = $script:state.TypeThingHotkeyWindow.Handle
    try {
        [HotkeyHelper]::UnregisterHotKey($handle, $script:state.TypeThingHotkeyStartId) | Out-Null
        [HotkeyHelper]::UnregisterHotKey($handle, $script:state.TypeThingHotkeyStopId) | Out-Null
        $script:state.TypeThingHotkeysRegistered = $false
        Write-RedballLog -Level 'INFO' -Message 'TypeThing: Hotkeys unregistered'
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "TypeThing: Hotkey unregistration skipped: $_"
    }
}

function Get-ClipboardText {
    <#
    .SYNOPSIS
        Gets text content from the clipboard with retry logic.
    #>
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        try {
            $text = [System.Windows.Forms.Clipboard]::GetText()
            if ([string]::IsNullOrEmpty($text)) { return $null }
            return $text
        }
        catch {
            if ($attempt -eq 0) {
                Start-Sleep -Milliseconds 100
            }
            else {
                Write-RedballLog -Level 'DEBUG' -Message "TypeThing: Clipboard access failed: $_"
                return $null
            }
        }
    }
    return $null
}

function Send-TypeThingChar {
    <#
    .SYNOPSIS
        Sends a single character via SendInput using KEYEVENTF_UNICODE.
    #>
    param(
        [Parameter(Mandatory)]
        [char]$Character
    )

    try {
        $charCode = [uint16]$Character
        $cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type][INPUT])

        # Handle newlines
        if ($Character -eq "`n" -or $Character -eq "`r") {
            if (-not $script:config.TypeThingTypeNewlines) { return }
            # Build KEYBDINPUT structs fully before assigning to INPUT
            # (PowerShell returns copies of nested value types, so $input.ki.wVk = x does NOT work)
            $kiDown = New-Object KEYBDINPUT
            $kiDown.wVk = [TypeThingInput]::VK_RETURN
            $kiDown.dwFlags = 0
            $inputDown = New-Object INPUT
            $inputDown.type = [TypeThingInput]::INPUT_KEYBOARD
            $inputDown.ki = $kiDown

            $kiUp = New-Object KEYBDINPUT
            $kiUp.wVk = [TypeThingInput]::VK_RETURN
            $kiUp.dwFlags = [TypeThingInput]::KEYEVENTF_KEYUP
            $inputUp = New-Object INPUT
            $inputUp.type = [TypeThingInput]::INPUT_KEYBOARD
            $inputUp.ki = $kiUp

            [TypeThingInput]::SendInput(2, @($inputDown, $inputUp), $cbSize) | Out-Null
            return
        }

        # Handle tab
        if ($Character -eq "`t") {
            $kiDown = New-Object KEYBDINPUT
            $kiDown.wVk = [TypeThingInput]::VK_TAB
            $kiDown.dwFlags = 0
            $inputDown = New-Object INPUT
            $inputDown.type = [TypeThingInput]::INPUT_KEYBOARD
            $inputDown.ki = $kiDown

            $kiUp = New-Object KEYBDINPUT
            $kiUp.wVk = [TypeThingInput]::VK_TAB
            $kiUp.dwFlags = [TypeThingInput]::KEYEVENTF_KEYUP
            $inputUp = New-Object INPUT
            $inputUp.type = [TypeThingInput]::INPUT_KEYBOARD
            $inputUp.ki = $kiUp

            [TypeThingInput]::SendInput(2, @($inputDown, $inputUp), $cbSize) | Out-Null
            return
        }

        # Skip other control characters
        if ([char]::IsControl($Character)) { return }

        # Send unicode character via KEYEVENTF_UNICODE
        $kiDown = New-Object KEYBDINPUT
        $kiDown.wVk = 0
        $kiDown.wScan = $charCode
        $kiDown.dwFlags = [TypeThingInput]::KEYEVENTF_UNICODE
        $inputDown = New-Object INPUT
        $inputDown.type = [TypeThingInput]::INPUT_KEYBOARD
        $inputDown.ki = $kiDown

        $kiUp = New-Object KEYBDINPUT
        $kiUp.wVk = 0
        $kiUp.wScan = $charCode
        $kiUp.dwFlags = ([TypeThingInput]::KEYEVENTF_UNICODE -bor [TypeThingInput]::KEYEVENTF_KEYUP)
        $inputUp = New-Object INPUT
        $inputUp.type = [TypeThingInput]::INPUT_KEYBOARD
        $inputUp.ki = $kiUp

        $sent = [TypeThingInput]::SendInput(2, @($inputDown, $inputUp), $cbSize)
        if ($sent -eq 0) {
            $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
            Write-RedballLog -Level 'DEBUG' -Message "TypeThing: SendInput returned 0 (Win32 error: $err) - keystroke may have failed"
        }
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "TypeThing: Send char failed: $_"
    }
}

function Start-TypeThingTyping {
    <#
    .SYNOPSIS
        Reads the clipboard and begins simulated typing with a countdown.
    #>
    Write-VerboseLog -Message "Start-TypeThingTyping called" -Source "TypeThing"
    
    if ($script:state.TypeThingIsTyping) { 
        Write-VerboseLog -Message "Already typing, ignoring request" -Source "TypeThing"
        return 
    }
    if ($script:state.IsShuttingDown) { 
        Write-VerboseLog -Message "Shutting down, ignoring request" -Source "TypeThing"
        return 
    }
    if (-not $script:config.TypeThingEnabled) { 
        Write-VerboseLog -Message "TypeThing disabled, ignoring request" -Source "TypeThing"
        return 
    }

    Write-VerboseLog -Message "Getting clipboard text" -Source "TypeThing"
    $text = Get-ClipboardText
    if (-not $text) {
        if ($script:config.TypeThingNotifications -and $script:state.NotifyIcon) {
            try {
                $script:state.NotifyIcon.ShowBalloonTip(2000, 'TypeThing', 'Clipboard is empty - copy some text first', [System.Windows.Forms.ToolTipIcon]::Warning)
            }
            catch {
                Write-RedballLog -Level 'DEBUG' -Message "TypeThing balloon tip failed: $_"
            }
        }
        Write-RedballLog -Level 'INFO' -Message 'TypeThing: Clipboard empty, nothing to type'
        Write-VerboseLog -Message "Clipboard was empty" -Source "TypeThing"
        return
    }

    Write-VerboseLog -Message "Clipboard contains $($text.Length) characters" -Source "TypeThing"

    # Warn on very large clipboard
    $largeClipboardThreshold = if ($script:config.TypeThingLargeClipboardThreshold) { $script:config.TypeThingLargeClipboardThreshold } else { 10000 }
    if ($text.Length -gt $largeClipboardThreshold) {
        $confirmResult = [System.Windows.Forms.MessageBox]::Show(
            "The clipboard contains $($text.Length) characters. This may take a while.`n`nContinue?",
            'TypeThing - Large Clipboard',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($confirmResult -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    }

    $script:state.TypeThingIsTyping = $true
    $script:state.TypeThingShouldStop = $false
    $script:state.TypeThingText = $text
    $script:state.TypeThingIndex = 0
    $script:state.TypeThingTotalChars = $text.Length
    $script:state.TypeThingStartTime = Get-Date

    # Update menu items
    if ($script:state.TypeThingMenuType) { $script:state.TypeThingMenuType.Enabled = $false }
    if ($script:state.TypeThingMenuStop) { $script:state.TypeThingMenuStop.Enabled = $true }
    if ($script:state.TypeThingMenuStatus) { $script:state.TypeThingMenuStatus.Text = "Status: Typing 0/$($text.Length) chars..." }

    Write-RedballLog -Level 'INFO' -Message "TypeThing: Starting to type $($text.Length) characters"

    $delaySec = $script:config.TypeThingStartDelaySec
    if ($delaySec -gt 0) {
        $script:state.TypeThingCountdownRemaining = $delaySec

        if ($script:config.TypeThingNotifications -and $script:state.NotifyIcon) {
            try {
                $script:state.NotifyIcon.ShowBalloonTip(2000, 'TypeThing',
                    "Typing in $delaySec seconds... ($($script:config.TypeThingStopHotkey) to cancel)",
                    [System.Windows.Forms.ToolTipIcon]::Info)
            }
            catch {
                Write-RedballLog -Level 'DEBUG' -Message "TypeThing balloon tip failed: $_"
            }
        }

        # Create countdown timer
        if (-not $script:state.TypeThingCountdown) {
            $script:state.TypeThingCountdown = New-Object System.Windows.Forms.Timer
            $script:state.TypeThingCountdown.Interval = 1000
            $script:state.TypeThingCountdown.add_Tick({
                    $script:state.TypeThingCountdownRemaining--
                    if ($script:state.TypeThingShouldStop) {
                        $script:state.TypeThingCountdown.Stop()
                        return
                    }
                    if ($script:state.TypeThingMenuStatus) {
                        $script:state.TypeThingMenuStatus.Text = "Status: Typing in $($script:state.TypeThingCountdownRemaining)s..."
                    }
                    if ($script:state.TypeThingCountdownRemaining -le 0) {
                        $script:state.TypeThingCountdown.Stop()
                        Start-TypeThingTimer
                    }
                })
        }
        $script:state.TypeThingCountdown.Start()
    }
    else {
        Start-TypeThingTimer
    }
}

function Start-TypeThingTimer {
    <#
    .SYNOPSIS
        Creates and starts the per-character typing timer.
    #>
    if (-not $script:state.TypeThingTimer) {
        $script:state.TypeThingTimer = New-Object System.Windows.Forms.Timer
        $script:state.TypeThingTimer.add_Tick({
                if ($script:state.TypeThingShouldStop -or $script:state.IsShuttingDown) {
                    Stop-TypeThingTyping
                    return
                }

                if ($script:state.TypeThingIndex -ge $script:state.TypeThingTotalChars) {
                    Complete-TypeThingTyping
                    return
                }

                try {
                    $char = $script:state.TypeThingText[$script:state.TypeThingIndex]
                    # Skip \r when followed by \n (Windows \r\n) to avoid double-Enter
                    if ($char -eq "`r" -and ($script:state.TypeThingIndex + 1) -lt $script:state.TypeThingTotalChars -and $script:state.TypeThingText[$script:state.TypeThingIndex + 1] -eq "`n") {
                        $script:state.TypeThingIndex++
                        return
                    }
                    Send-TypeThingChar -Character $char
                    $script:state.TypeThingIndex++

                    # Calculate next delay
                    $minDelay = [Math]::Max(10, $script:config.TypeThingMinDelayMs)
                    $maxDelay = [Math]::Max($minDelay + 1, $script:config.TypeThingMaxDelayMs)
                    $delay = Get-Random -Minimum $minDelay -Maximum $maxDelay

                    # Random pause for human-like feel
                    if ($script:config.TypeThingAddRandomPauses) {
                        $roll = Get-Random -Maximum 100
                        if ($roll -lt $script:config.TypeThingRandomPauseChance) {
                            $delay += Get-Random -Maximum ([Math]::Max(1, $script:config.TypeThingRandomPauseMaxMs))
                        }
                    }

                    $script:state.TypeThingTimer.Interval = $delay

                    # Update status periodically (every 10 chars to reduce overhead)
                    if ($script:state.TypeThingIndex % 10 -eq 0 -and $script:state.TypeThingMenuStatus) {
                        $script:state.TypeThingMenuStatus.Text = "Status: Typing $($script:state.TypeThingIndex)/$($script:state.TypeThingTotalChars) chars..."
                    }
                }
                catch {
                    Write-RedballLog -Level 'WARN' -Message "TypeThing: Error during typing: $_"
                    Stop-TypeThingTyping
                }
            })
    }

    $minDelay = [Math]::Max(10, $script:config.TypeThingMinDelayMs)
    $maxDelay = [Math]::Max($minDelay + 1, $script:config.TypeThingMaxDelayMs)
    $script:state.TypeThingTimer.Interval = Get-Random -Minimum $minDelay -Maximum $maxDelay
    $script:state.TypeThingTimer.Start()

    if ($script:state.TypeThingMenuStatus) {
        $script:state.TypeThingMenuStatus.Text = "Status: Typing 0/$($script:state.TypeThingTotalChars) chars..."
    }
}

function Stop-TypeThingTyping {
    <#
    .SYNOPSIS
        Immediately stops typing and resets state.
    #>
    $script:state.TypeThingShouldStop = $true

    if ($script:state.TypeThingTimer) {
        $script:state.TypeThingTimer.Stop()
    }
    if ($script:state.TypeThingCountdown) {
        $script:state.TypeThingCountdown.Stop()
    }

    $typed = $script:state.TypeThingIndex
    $total = $script:state.TypeThingTotalChars

    # Clear sensitive data from memory
    $script:state.TypeThingText = ''
    $script:state.TypeThingIndex = 0
    $script:state.TypeThingTotalChars = 0
    $script:state.TypeThingIsTyping = $false
    $script:state.TypeThingShouldStop = $false
    $script:state.TypeThingStartTime = $null

    # Update menu items
    if ($script:state.TypeThingMenuType) { $script:state.TypeThingMenuType.Enabled = $true }
    if ($script:state.TypeThingMenuStop) { $script:state.TypeThingMenuStop.Enabled = $false }
    if ($script:state.TypeThingMenuStatus) { $script:state.TypeThingMenuStatus.Text = 'Status: Idle' }

    if ($typed -gt 0) {
        Write-RedballLog -Level 'INFO' -Message "TypeThing: Stopped by user at $typed/$total chars"
        if ($script:config.TypeThingNotifications -and $script:state.NotifyIcon) {
            try {
                $script:state.NotifyIcon.ShowBalloonTip(2000, 'TypeThing',
                    "Typing stopped ($typed/$total characters typed)",
                    [System.Windows.Forms.ToolTipIcon]::Warning)
            }
            catch {
                Write-RedballLog -Level 'DEBUG' -Message "TypeThing balloon tip failed: $_"
            }
        }
    }
}

function Complete-TypeThingTyping {
    <#
    .SYNOPSIS
        Called when typing finishes all characters successfully.
    #>
    if ($script:state.TypeThingTimer) {
        $script:state.TypeThingTimer.Stop()
    }

    $total = $script:state.TypeThingTotalChars
    $elapsed = if ($script:state.TypeThingStartTime) {
        ((Get-Date) - $script:state.TypeThingStartTime).TotalSeconds
    }
    else { 0 }
    $elapsedStr = [Math]::Round($elapsed, 1)

    # Clear sensitive data from memory
    $script:state.TypeThingText = ''
    $script:state.TypeThingIndex = 0
    $script:state.TypeThingTotalChars = 0
    $script:state.TypeThingIsTyping = $false
    $script:state.TypeThingShouldStop = $false
    $script:state.TypeThingStartTime = $null

    # Update menu items
    if ($script:state.TypeThingMenuType) { $script:state.TypeThingMenuType.Enabled = $true }
    if ($script:state.TypeThingMenuStop) { $script:state.TypeThingMenuStop.Enabled = $false }
    if ($script:state.TypeThingMenuStatus) { $script:state.TypeThingMenuStatus.Text = 'Status: Idle' }

    Write-RedballLog -Level 'INFO' -Message "TypeThing: Completed $total chars in ${elapsedStr}s"

    if ($script:config.TypeThingNotifications -and $script:state.NotifyIcon) {
        try {
            $script:state.NotifyIcon.ShowBalloonTip(2000, 'TypeThing',
                "Typing complete ($total characters in ${elapsedStr}s)",
                [System.Windows.Forms.ToolTipIcon]::Info)
        }
        catch {
            Write-RedballLog -Level 'DEBUG' -Message "TypeThing balloon tip failed: $_"
        }
    }
}

# --- TypeThing Initialization ---
if ($script:config.TypeThingEnabled) {
    try {
        Write-VerboseLog -Message "Initializing TypeThing hotkey window" -Source "TypeThing"
        $script:state.TypeThingHotkeyWindow = New-Object HotkeyMessageWindow
        Write-VerboseLog -Message "Hotkey window created, handle: $($script:state.TypeThingHotkeyWindow.Handle)" -Source "TypeThing"
        
        $script:state.TypeThingHotkeyWindow.add_HotkeyPressed({
                param($hotkeyId)
                try {
                    Write-VerboseLog -Message "Hotkey pressed event received, ID: $hotkeyId" -Source "TypeThing"
                    if ($script:state.IsShuttingDown) { 
                        Write-VerboseLog -Message "Shutting down, ignoring hotkey" -Source "TypeThing"
                        return 
                    }
                    if ($hotkeyId -eq $script:state.TypeThingHotkeyStartId) {
                        Write-VerboseLog -Message "Start hotkey triggered, calling Start-TypeThingTyping" -Source "TypeThing"
                        Start-TypeThingTyping
                    }
                    elseif ($hotkeyId -eq $script:state.TypeThingHotkeyStopId) {
                        Write-VerboseLog -Message "Stop hotkey triggered, calling Stop-TypeThingTyping" -Source "TypeThing"
                        Stop-TypeThingTyping
                    }
                }
                catch {
                    Write-RedballLog -Level 'WARN' -Message "TypeThing: Hotkey handler error: $_"
                    Write-VerboseLog -Message "Hotkey handler exception: $_" -Source "TypeThing"
                }
            })
        # Try to register immediately, but if handle isn't ready, retry via timer
        Write-VerboseLog -Message "Attempting initial hotkey registration" -Source "TypeThing"
        Register-TypeThingHotkeys
        if (-not $script:state.TypeThingHotkeysRegistered) {
            Write-VerboseLog -Message "Initial registration failed, starting retry timer" -Source "TypeThing"
            # Create a timer to retry registration after the message pump starts
            $typeThingInitTimer = New-Object System.Windows.Forms.Timer
            $typeThingInitTimer.Interval = 500
            $script:typeThingRetryCount = 0
            $script:typeThingMaxRetries = 10
            $typeThingInitTimer.add_Tick({
                    $script:typeThingRetryCount++
                    Write-VerboseLog -Message "Retry attempt $($script:typeThingRetryCount)/$($script:typeThingMaxRetries)" -Source "TypeThing"
                    if ($script:state.TypeThingHotkeysRegistered -or $script:typeThingRetryCount -gt $script:typeThingMaxRetries -or $script:state.IsShuttingDown) {
                        Write-VerboseLog -Message "Stopping retry timer (registered:$($script:state.TypeThingHotkeysRegistered) retryCount:$script:typeThingRetryCount)" -Source "TypeThing"
                        $typeThingInitTimer.Stop()
                        $typeThingInitTimer.Dispose()
                        return
                    }
                    Register-TypeThingHotkeys
                    if ($script:state.TypeThingHotkeysRegistered) {
                        $typeThingInitTimer.Stop()
                        $typeThingInitTimer.Dispose()
                        Write-RedballLog -Level 'INFO' -Message "TypeThing: Hotkeys registered after retry"
                        Write-VerboseLog -Message "Hotkeys registered on retry attempt $script:typeThingRetryCount" -Source "TypeThing"
                    }
                })
            $typeThingInitTimer.Start()
            Write-VerboseLog -Message "Retry timer started with 500ms interval" -Source "TypeThing"
        }
        else {
            Write-VerboseLog -Message "Hotkeys registered successfully on first attempt" -Source "TypeThing"
        }
        Write-RedballLog -Level 'INFO' -Message "TypeThing: Initialized (Start: $($script:config.TypeThingStartHotkey), Stop: $($script:config.TypeThingStopHotkey))"
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "TypeThing: Initialization failed: $_"
        Write-VerboseLog -Message "Initialization exception: $_" -Source "TypeThing"
    }
}
else {
    Write-VerboseLog -Message "TypeThing disabled in config, skipping initialization" -Source "TypeThing"
}

# Clear crash flag on clean exit
$onProcessExit = {
    Clear-CrashFlag
}
[AppDomain]::CurrentDomain.add_ProcessExit($onProcessExit)

[System.Windows.Forms.Application]::Run($script:state.Context)
