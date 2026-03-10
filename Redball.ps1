#requires -Version 5.1
<#requires -RunAsAdministrator#>
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
    Version: 2.0.0
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
    [switch]$ExitOnComplete
)

$script:VERSION = '2.0.0'
$script:APP_NAME = 'Redball'
$script:IsPS7 = $PSVersionTable.PSVersion.Major -ge 7

# Resolve script root once - works in direct run, ps2exe EXE, and dot-source contexts
$script:AppRoot = if ($PSScriptRoot -and $PSScriptRoot -is [string] -and (Test-Path $PSScriptRoot)) {
    $PSScriptRoot
} elseif ($MyInvocation.MyCommand.Path) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
} else {
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

$ES_CONTINUOUS = [uint32]2147483648
$ES_SYSTEM_REQUIRED = [uint32]1
$ES_DISPLAY_REQUIRED = [uint32]2

$script:state = [ordered]@{
    Active = $true
    PreventDisplaySleep = $true
    UseHeartbeatKeypress = $true
    HeartbeatSeconds = 59
    Until = $null
    NotifyIcon = $null
    Context = $null
    HeartbeatTimer = $null
    DurationTimer = $null
    ToggleMenuItem = $null
    DisplayMenuItem = $null
    HeartbeatMenuItem = $null
    StatusMenuItem = $null
    BatteryMenuItem = $null
    StartupMenuItem = $null
    PreviousIcon = $null
    IsShuttingDown = $false
    LastStatusText = ''
    LastIconState = ''
    UiUpdatePending = $false
    BatteryAware = $false
    BatteryThreshold = 20
    OnBattery = $false
    AutoPausedBattery = $false
    NetworkAware = $false
    AutoPausedNetwork = $false
    ActiveBeforeNetwork = $false
    IdleDetection = $false
    IdleThresholdMinutes = 30
    AutoPausedIdle = $false
    AutoPausedPresentation = $false
    AutoPausedSchedule = $false
    ManualOverride = $false
    StartTime = $null
    ActiveBeforeBattery = $false
    ActiveBeforeIdle = $false
    SessionId = [guid]::NewGuid().ToString()
    KeepAwakeRunspaceInfo = $null
}

$_detectedLocale = try { (Get-Culture).TwoLetterISOLanguageName } catch { 'en' }

$script:config = @{
    HeartbeatSeconds = 59
    PreventDisplaySleep = $true
    UseHeartbeatKeypress = $true
    DefaultDuration = 60
    LogPath = (Join-Path $script:AppRoot 'Redball.log')
    MaxLogSizeMB = 10
    ShowBalloonOnStart = $true
    Locale = $_detectedLocale
    MinimizeOnStart = $false
    BatteryAware = $false
    BatteryThreshold = 20
    NetworkAware = $false
    IdleDetection = $false
    AutoExitOnComplete = $false
    ScheduleEnabled = $false
    ScheduleStartTime = '09:00'
    ScheduleStopTime = '18:00'
    ScheduleDays = @('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday')
    PresentationModeDetection = $false
    EnablePerformanceMetrics = $false
    EnableTelemetry = $false
    ProcessIsolation = $false
    UpdateRepoOwner = 'karl-lawrence'
    UpdateRepoName = 'Redball'
    UpdateChannel = 'stable'
    VerifyUpdateSignature = $false
}

$script:locales = @{}
$script:currentLocale = 'en'

# --- Instance Management (Singleton Pattern) ---

$script:singletonMutex = $null
$script:wshShell = $null
$script:lastBatteryCheck = $null
$script:lastBatteryResult = @{ HasBattery = $false }

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
            ProcessId = $_.ProcessId
            Name = $_.Name
            CommandLine = $_.CommandLine
            StartTime = $_.CreationDate
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
        $systemLocale = ($env:LANG -split '_')[0]
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
            # Check for log rotation before writing
            if (Test-Path $script:config.LogPath) {
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

function Import-RedballConfig {
    <#
    .SYNOPSIS
        Loads Redball configuration from JSON.
    .DESCRIPTION
        Reads configuration values from disk and merges known keys into the in-memory
        configuration hashtable. If the file does not exist, current defaults are written.
    .PARAMETER Path
        Path to the Redball JSON configuration file.
    .EXAMPLE
        Import-RedballConfig -Path $ConfigPath
        Loads persisted settings and applies them to the current session.
    #>
    param([string]$Path = $ConfigPath)

    try {
        if (-not (Test-Path $Path)) {
            Save-RedballConfig -Path $Path
            return
        }

        $loaded = Get-Content -Path $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($property in $loaded.PSObject.Properties) {
            if ($script:config.ContainsKey($property.Name)) {
                $script:config[$property.Name] = $property.Value
            }
        }

        # Keep runtime state in sync with persisted settings
        $script:state.PreventDisplaySleep = $script:config.PreventDisplaySleep
        $script:state.UseHeartbeatKeypress = $script:config.UseHeartbeatKeypress
        $script:state.HeartbeatSeconds = $script:config.HeartbeatSeconds
        $script:state.BatteryAware = $script:config.BatteryAware
        $script:state.BatteryThreshold = $script:config.BatteryThreshold
        $script:state.NetworkAware = $script:config.NetworkAware
        $script:state.IdleDetection = $script:config.IdleDetection

        Write-RedballLog -Level 'INFO' -Message "Configuration loaded from: $Path"
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to load configuration from '$Path': $_"
    }
}

# Backward compatibility for callers that still invoke Load-RedballConfig.
Set-Alias -Name Load-RedballConfig -Value Import-RedballConfig -Scope Script

function Save-RedballConfig {
    <#
    .SYNOPSIS
        Saves Redball configuration to JSON.
    .DESCRIPTION
        Persists runtime settings to disk so they are restored on the next launch.
    .PARAMETER Path
        Path to the Redball JSON configuration file.
    .EXAMPLE
        Save-RedballConfig
        Writes the current configuration to the default config file.
    #>
    param([string]$Path = $ConfigPath)

    try {
        $script:config.PreventDisplaySleep = $script:state.PreventDisplaySleep
        $script:config.UseHeartbeatKeypress = $script:state.UseHeartbeatKeypress
        $script:config.HeartbeatSeconds = $script:state.HeartbeatSeconds
        $script:config.BatteryAware = $script:state.BatteryAware
        $script:config.BatteryThreshold = $script:state.BatteryThreshold
        $script:config.NetworkAware = $script:state.NetworkAware
        $script:config.IdleDetection = $script:state.IdleDetection

        $configDir = Split-Path -Path $Path -Parent
        if ($configDir -and -not (Test-Path $configDir)) {
            New-Item -ItemType Directory -Path $configDir -Force | Out-Null
        }

        $script:config | ConvertTo-Json -Depth 6 | Set-Content -Path $Path -Encoding UTF8
        Write-RedballLog -Level 'DEBUG' -Message "Configuration saved to: $Path"
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to save configuration to '$Path': $_"
    }
}

trap [System.Management.Automation.PipelineStoppedException] {
    Write-RedballLog -Level 'INFO' -Message 'Pipeline stopped - exiting gracefully'
    Exit-Application
    break
}

function Set-KeepAwakeState {
    <#
    .SYNOPSIS
        Sets the Windows execution state to prevent or allow sleep.
    .DESCRIPTION
        Uses the SetThreadExecutionState API to tell Windows whether to keep the system awake.
        Can optionally prevent display sleep as well.
    .PARAMETER Enable
        If true, prevents sleep. If false, allows normal sleep behavior.
    .EXAMPLE
        Set-KeepAwakeState -Enable $true
        Prevents the system from sleeping.
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [bool]$Enable
    )

    $action = if ($Enable) { 'Enable keep-awake execution state' } else { 'Restore default sleep execution state' }
    if (-not $PSCmdlet.ShouldProcess('Windows power execution state', $action)) {
        return
    }

    try {
        if ($Enable) {
            $flags = $ES_CONTINUOUS -bor $ES_SYSTEM_REQUIRED
            if ($script:state.PreventDisplaySleep) {
                $flags = $flags -bor $ES_DISPLAY_REQUIRED
            }

            [void][Win32.Power]::SetThreadExecutionState($flags)
        }
        else {
            [void][Win32.Power]::SetThreadExecutionState($ES_CONTINUOUS)
        }
    }
    catch {
        Write-Warning "Failed to set keep-awake state: $_"
    }
}

function Send-HeartbeatKey {
    <#
    .SYNOPSIS
        Sends an invisible F15 keypress to prevent idle detection.
    .DESCRIPTION
        Uses WScript.Shell to send an F15 keypress, which prevents Windows from 
        detecting user idle time without interfering with actual work.
        Only sends the key if the system has been idle for at least 1 minute.
    .EXAMPLE
        Send-HeartbeatKey
        Sends a single F15 keypress if system is idle.
    #>
    if (-not $script:state.Active) {
        return
    }

    if (-not $script:state.UseHeartbeatKeypress) {
        return
    }

    try {
        # Only send heartbeat if system has been idle for at least 1 minute
        # This prevents interfering with active user work
        $idleMinutes = Get-IdleTimeMinutes
        if ($idleMinutes -lt 1) {
            # User is active, don't send F15
            return
        }
        
        # Use cached WScript.Shell COM object to avoid creating a new one every tick
        if (-not $script:wshShell) {
            $script:wshShell = New-Object -ComObject WScript.Shell
        }
        $script:wshShell.SendKeys('{F15}')
    }
    catch {
        Write-RedballLog -Level 'DEBUG' -Message "Heartbeat key dispatch skipped: $_"
    }
}

function Get-CustomTrayIcon {
    <#
    .SYNOPSIS
        Generates a custom 3D red ball tray icon.
    .DESCRIPTION
        Creates a 32x32 bitmap with a 3D red ball using GDI+ gradients.
        Returns different colors for active, timed, and paused states.
    .PARAMETER State
        The state of the application: 'active', 'timed', or 'paused'.
    .EXAMPLE
        $icon = Get-CustomTrayIcon -State 'active'
        Creates a bright red ball icon.
    #>
    param(
        [Parameter(Mandatory)]
        [ValidateSet('active', 'timed', 'paused')]
        [string]$State
    )
    try {
        if ($script:state.PreviousIcon -and -not $script:state.PreviousIcon.Disposing) {
            try {
                $script:state.PreviousIcon.Dispose()
            }
            catch {
                Write-RedballLog -Level 'DEBUG' -Message "Previous icon dispose before redraw skipped: $_"
            }
        }
        $bitmap = New-Object System.Drawing.Bitmap 32, 32
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        
        # Color scheme based on state
        $colors = switch ($State) {
            'active' {
                @{  # Bright Red
                    Dark = [System.Drawing.Color]::FromArgb(139, 0, 0)
                    Base = [System.Drawing.Color]::FromArgb(220, 20, 60)
                    Light = [System.Drawing.Color]::FromArgb(255, 99, 71)
                    Highlight = [System.Drawing.Color]::FromArgb(255, 160, 122)
                }
            }
            'timed' {
                @{  # Orange/Red gradient
                    Dark = [System.Drawing.Color]::FromArgb(180, 50, 0)
                    Base = [System.Drawing.Color]::FromArgb(255, 140, 0)
                    Light = [System.Drawing.Color]::FromArgb(255, 180, 60)
                    Highlight = [System.Drawing.Color]::FromArgb(255, 215, 100)
                }
            }
            'paused' {
                @{  # Dark Red/Gray
                    Dark = [System.Drawing.Color]::FromArgb(80, 20, 20)
                    Base = [System.Drawing.Color]::FromArgb(120, 40, 40)
                    Light = [System.Drawing.Color]::FromArgb(160, 60, 60)
                    Highlight = [System.Drawing.Color]::FromArgb(180, 80, 80)
                }
            }
        }
        
        # Clear with transparent background
        $graphics.Clear([System.Drawing.Color]::Transparent)
        
        # Draw 3D sphere
        $ballRect = New-Object System.Drawing.Rectangle 2, 2, 28, 28
        
        # Create gradient for 3D effect
        $gradientPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $gradientPath.AddEllipse($ballRect)
        
        $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush $gradientPath
        $brush.CenterColor = $colors.Highlight
        $brush.SurroundColors = @($colors.Dark)
        $brush.CenterPoint = New-Object System.Drawing.PointF 10, 10
        $brush.FocusScales = New-Object System.Drawing.PointF 0.3, 0.3
        
        $graphics.FillEllipse($brush, $ballRect)
        
        # Add specular highlight
        $highlightRect = New-Object System.Drawing.Rectangle 6, 6, 10, 8
        $highlightBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $highlightRect,
            [System.Drawing.Color]::FromArgb(200, 255, 255, 255),
            [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
            45
        )
        $graphics.FillEllipse($highlightBrush, $highlightRect)
        
        # Add subtle shadow
        $shadowRect = New-Object System.Drawing.Rectangle 4, 24, 24, 6
        $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(40, 0, 0, 0))
        $graphics.FillEllipse($shadowBrush, $shadowRect)
        
        # Outer rim for definition
        $rimPen = New-Object System.Drawing.Pen($colors.Dark, 1)
        $graphics.DrawEllipse($rimPen, $ballRect)
        
        $graphics.Dispose()
        $brush.Dispose()
        $gradientPath.Dispose()
        $highlightBrush.Dispose()
        $shadowBrush.Dispose()
        $rimPen.Dispose()
        
        $icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
        $bitmap.Dispose()
        $script:state.PreviousIcon = $icon
        return $icon
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to create icon: $_"
        return $null
    }
}

function Get-StatusText {
    try {
        $mode = if ($script:state.Active) { 'Active' } else { 'Paused' }
        $display = if ($script:state.PreventDisplaySleep) { 'Display On' } else { 'Display Normal' }
        $heartbeat = if ($script:state.UseHeartbeatKeypress) { 'F15 On' } else { 'F15 Off' }
        if ($script:state.Until) {
            $timeLeft = $script:state.Until - (Get-Date)
            $minutesLeft = [math]::Ceiling($timeLeft.TotalMinutes)
            return "$mode | $display | $heartbeat | ${minutesLeft}min left"
        }
        return "$mode | $display | $heartbeat"
    }
    catch {
        return 'Status unavailable'
    }
}

function Update-RedballUI {
    <#
    .SYNOPSIS
        Updates the tray icon and menu items to reflect current state.
    .DESCRIPTION
        Updates the NotifyIcon tooltip, icon image, and all menu item states.
        Only updates when state has changed to reduce flicker.
    .EXAMPLE
        Update-RedballUI
        Refreshes the UI to match current application state.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()

    try {
        if ($script:state.IsShuttingDown) { return }
        
        # Determine current icon state
        $iconState = if ($script:state.Active) {
            if ($script:state.Until) { 'timed' } else { 'active' }
        }
        else {
            'paused'
        }
        
        # Generate new status text
        $detailText = Get-StatusText
        $baseText = if ($script:state.Active) { 'Redball (Active)' } else { 'Redball (Paused)' }
        $newTooltip = "$baseText`n$detailText"
        
        # Only update icon if state changed (performance optimization)
        if ($iconState -ne $script:state.LastIconState) {
            $customIcon = Get-CustomTrayIcon -State $iconState
            $icon = if ($customIcon) {
                $customIcon
            }
            else {
                switch ($iconState) {
                    'active' { [System.Drawing.SystemIcons]::Information }
                    'timed' { [System.Drawing.SystemIcons]::Warning }
                    'paused' { [System.Drawing.SystemIcons]::Error }
                }
            }
            $script:state.NotifyIcon.Icon = $icon
            $script:state.LastIconState = $iconState
        }
        
        # Only update tooltip if changed (reduces flicker)
        if ($newTooltip -ne $script:state.NotifyIcon.Text) {
            $script:state.NotifyIcon.Text = $newTooltip
        }
        
        # Update menu items (only if changed)
        $toggleText = if ($script:state.Active) { 'Pause Keep-Awake' } else { 'Resume Keep-Awake' }
        if ($script:state.ToggleMenuItem.Text -ne $toggleText) {
            $script:state.ToggleMenuItem.Text = $toggleText
        }
        
        if ($script:state.DisplayMenuItem.Checked -ne $script:state.PreventDisplaySleep) {
            $script:state.DisplayMenuItem.Checked = $script:state.PreventDisplaySleep
        }
        
        if ($script:state.HeartbeatMenuItem.Checked -ne $script:state.UseHeartbeatKeypress) {
            $script:state.HeartbeatMenuItem.Checked = $script:state.UseHeartbeatKeypress
        }
        
        if ($script:state.BatteryMenuItem -and $script:state.BatteryMenuItem.Checked -ne $script:state.BatteryAware) {
            $script:state.BatteryMenuItem.Checked = $script:state.BatteryAware
        }
        
        if ($script:state.NetworkMenuItem -and $script:state.NetworkMenuItem.Checked -ne $script:state.NetworkAware) {
            $script:state.NetworkMenuItem.Checked = $script:state.NetworkAware
        }
        
        if ($script:state.IdleMenuItem -and $script:state.IdleMenuItem.Checked -ne $script:state.IdleDetection) {
            $script:state.IdleMenuItem.Checked = $script:state.IdleDetection
        }
        
        $statusText = "Status: $detailText"
        if ($script:state.StatusMenuItem.Text -ne $statusText) {
            $script:state.StatusMenuItem.Text = $statusText
        }
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "UI update failed: $_"
    }
}

function Show-RedballSettings {
    <#
    .SYNOPSIS
        Displays a tabbed settings dialog for all Redball configuration options.
    .DESCRIPTION
        Opens a WinForms dialog with tabs for General, Power & Monitoring,
        Schedule, and Advanced settings. Each setting includes a description.
        Changes are saved to the config file when OK is clicked.
    .EXAMPLE
        Show-RedballSettings
        Opens the settings dialog.
    #>
    try {
        $form = New-Object System.Windows.Forms.Form
        $form.Text = "Redball Settings"
        $form.Size = New-Object System.Drawing.Size(520, 530)
        $form.StartPosition = 'CenterScreen'
        $form.FormBorderStyle = 'FixedDialog'
        $form.MaximizeBox = $false
        $form.MinimizeBox = $false
        $form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
        $form.BackColor = [System.Drawing.Color]::FromArgb(245, 245, 245)

        $tabs = New-Object System.Windows.Forms.TabControl
        $tabs.Dock = 'Fill'
        $tabs.Padding = New-Object System.Drawing.Point(12, 6)

        # --- Helper to add a setting row ---
        $script:settingsControls = @{}
        $yTracker = @{}

        function Add-SettingRow {
            param($Panel, $TabKey, $Key, $Label, $Description, $Type, $Value, $Options)
            if (-not $yTracker[$TabKey]) { $yTracker[$TabKey] = 10 }
            $y = $yTracker[$TabKey]

            $lbl = New-Object System.Windows.Forms.Label
            $lbl.Text = $Label
            $lbl.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
            $lbl.Location = New-Object System.Drawing.Point(14, $y)
            $lbl.AutoSize = $true
            $Panel.Controls.Add($lbl)
            $y += 20

            $desc = New-Object System.Windows.Forms.Label
            $desc.Text = $Description
            $desc.ForeColor = [System.Drawing.Color]::FromArgb(100, 100, 100)
            $desc.Location = New-Object System.Drawing.Point(14, $y)
            $desc.Size = New-Object System.Drawing.Size(440, 18)
            $Panel.Controls.Add($desc)
            $y += 22

            switch ($Type) {
                'bool' {
                    $chk = New-Object System.Windows.Forms.CheckBox
                    $chk.Text = 'Enabled'
                    $chk.Checked = [bool]$Value
                    $chk.Location = New-Object System.Drawing.Point(14, $y)
                    $chk.AutoSize = $true
                    $Panel.Controls.Add($chk)
                    $script:settingsControls[$Key] = $chk
                    $y += 28
                }
                'number' {
                    $nud = New-Object System.Windows.Forms.NumericUpDown
                    $nud.Location = New-Object System.Drawing.Point(14, $y)
                    $nud.Size = New-Object System.Drawing.Size(100, 25)
                    $nud.Minimum = if ($Options -and $null -ne $Options.Min) { $Options.Min } else { 0 }
                    $nud.Maximum = if ($Options -and $null -ne $Options.Max) { $Options.Max } else { 9999 }
                    $nud.Value = [math]::Max($nud.Minimum, [math]::Min($nud.Maximum, [decimal]$Value))
                    $Panel.Controls.Add($nud)
                    $script:settingsControls[$Key] = $nud
                    $y += 32
                }
                'text' {
                    $txt = New-Object System.Windows.Forms.TextBox
                    $txt.Text = [string]$Value
                    $txt.Location = New-Object System.Drawing.Point(14, $y)
                    $txt.Size = New-Object System.Drawing.Size(200, 25)
                    $Panel.Controls.Add($txt)
                    $script:settingsControls[$Key] = $txt
                    $y += 32
                }
                'dropdown' {
                    $cmb = New-Object System.Windows.Forms.ComboBox
                    $cmb.DropDownStyle = 'DropDownList'
                    $cmb.Location = New-Object System.Drawing.Point(14, $y)
                    $cmb.Size = New-Object System.Drawing.Size(200, 25)
                    if ($Options -and $Options.Items) {
                        $Options.Items | ForEach-Object { [void]$cmb.Items.Add($_) }
                    }
                    $cmb.SelectedItem = [string]$Value
                    if ($cmb.SelectedIndex -lt 0 -and $cmb.Items.Count -gt 0) { $cmb.SelectedIndex = 0 }
                    $Panel.Controls.Add($cmb)
                    $script:settingsControls[$Key] = $cmb
                    $y += 32
                }
                'time' {
                    $txt = New-Object System.Windows.Forms.TextBox
                    $txt.Text = [string]$Value
                    $txt.Location = New-Object System.Drawing.Point(14, $y)
                    $txt.Size = New-Object System.Drawing.Size(80, 25)
                    $Panel.Controls.Add($txt)
                    $hintLbl = New-Object System.Windows.Forms.Label
                    $hintLbl.Text = '(HH:mm format)'
                    $hintLbl.ForeColor = [System.Drawing.Color]::Gray
                    $hintLbl.Location = New-Object System.Drawing.Point(100, ($y + 3))
                    $hintLbl.AutoSize = $true
                    $Panel.Controls.Add($hintLbl)
                    $script:settingsControls[$Key] = $txt
                    $y += 32
                }
                'days' {
                    $clb = New-Object System.Windows.Forms.CheckedListBox
                    $clb.Location = New-Object System.Drawing.Point(14, $y)
                    $clb.Size = New-Object System.Drawing.Size(200, 112)
                    $clb.CheckOnClick = $true
                    $allDays = @('Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday')
                    foreach ($day in $allDays) {
                        $idx = $clb.Items.Add($day)
                        if ($Value -contains $day) {
                            $clb.SetItemChecked($idx, $true)
                        }
                    }
                    $Panel.Controls.Add($clb)
                    $script:settingsControls[$Key] = $clb
                    $y += 120
                }
            }
            $y += 6
            $yTracker[$TabKey] = $y
        }

        # ============ GENERAL TAB ============
        $tabGeneral = New-Object System.Windows.Forms.TabPage
        $tabGeneral.Text = 'General'
        $tabGeneral.AutoScroll = $true
        $panelGeneral = New-Object System.Windows.Forms.Panel
        $panelGeneral.Dock = 'Fill'
        $panelGeneral.AutoScroll = $true
        $tabGeneral.Controls.Add($panelGeneral)

        Add-SettingRow $panelGeneral 'General' 'DefaultDuration' 'Default Duration (minutes)' `
            'Default number of minutes for timed keep-awake mode.' 'number' $script:config.DefaultDuration @{Min=1;Max=720}
        Add-SettingRow $panelGeneral 'General' 'HeartbeatSeconds' 'Heartbeat Interval (seconds)' `
            'How often to send the F15 keypress and refresh the keep-awake state.' 'number' $script:config.HeartbeatSeconds @{Min=10;Max=300}
        Add-SettingRow $panelGeneral 'General' 'ShowBalloonOnStart' 'Show Notification on Start' `
            'Display a tray notification when Redball starts keeping your PC awake.' 'bool' $script:config.ShowBalloonOnStart
        Add-SettingRow $panelGeneral 'General' 'MinimizeOnStart' 'Start Minimized' `
            'Start Redball minimized to the system tray without showing a window.' 'bool' $script:config.MinimizeOnStart
        Add-SettingRow $panelGeneral 'General' 'AutoExitOnComplete' 'Exit When Timer Completes' `
            'Automatically close Redball when a timed keep-awake period finishes.' 'bool' $script:config.AutoExitOnComplete
        Add-SettingRow $panelGeneral 'General' 'Locale' 'Language' `
            'Display language for menus and notifications.' 'dropdown' $script:config.Locale @{Items=@('en','es','fr','de')}

        $tabs.TabPages.Add($tabGeneral)

        # ============ POWER & MONITORING TAB ============
        $tabPower = New-Object System.Windows.Forms.TabPage
        $tabPower.Text = 'Power && Monitoring'
        $tabPower.AutoScroll = $true
        $panelPower = New-Object System.Windows.Forms.Panel
        $panelPower.Dock = 'Fill'
        $panelPower.AutoScroll = $true
        $tabPower.Controls.Add($panelPower)

        Add-SettingRow $panelPower 'Power' 'PreventDisplaySleep' 'Prevent Display Sleep' `
            'Keep the display on in addition to preventing system sleep.' 'bool' $script:config.PreventDisplaySleep
        Add-SettingRow $panelPower 'Power' 'UseHeartbeatKeypress' 'Use F15 Heartbeat Keypress' `
            'Periodically send an invisible F15 key to prevent idle detection by apps like Teams.' 'bool' $script:config.UseHeartbeatKeypress
        Add-SettingRow $panelPower 'Power' 'BatteryAware' 'Battery-Aware Mode' `
            'Automatically pause keep-awake when battery drops below the threshold.' 'bool' $script:state.BatteryAware
        Add-SettingRow $panelPower 'Power' 'BatteryThreshold' 'Battery Threshold (%)' `
            'Battery percentage at which to auto-pause when on battery power.' 'number' $script:state.BatteryThreshold @{Min=5;Max=95}
        Add-SettingRow $panelPower 'Power' 'NetworkAware' 'Network-Aware Mode' `
            'Automatically pause keep-awake when the network connection is lost.' 'bool' $script:state.NetworkAware
        Add-SettingRow $panelPower 'Power' 'IdleDetection' 'Idle Detection (30 min)' `
            'Automatically pause when no mouse or keyboard input for 30 minutes.' 'bool' $script:state.IdleDetection
        Add-SettingRow $panelPower 'Power' 'PresentationModeDetection' 'Presentation Mode Detection' `
            'Auto-activate keep-awake when PowerPoint or Teams presenting is detected.' 'bool' $script:config.PresentationModeDetection

        $tabs.TabPages.Add($tabPower)

        # ============ SCHEDULE TAB ============
        $tabSchedule = New-Object System.Windows.Forms.TabPage
        $tabSchedule.Text = 'Schedule'
        $tabSchedule.AutoScroll = $true
        $panelSchedule = New-Object System.Windows.Forms.Panel
        $panelSchedule.Dock = 'Fill'
        $panelSchedule.AutoScroll = $true
        $tabSchedule.Controls.Add($panelSchedule)

        Add-SettingRow $panelSchedule 'Schedule' 'ScheduleEnabled' 'Enable Scheduled Operation' `
            'Automatically activate and deactivate keep-awake on a daily schedule.' 'bool' $script:config.ScheduleEnabled
        Add-SettingRow $panelSchedule 'Schedule' 'ScheduleStartTime' 'Start Time' `
            'Time of day to automatically start keeping the PC awake.' 'time' $script:config.ScheduleStartTime
        Add-SettingRow $panelSchedule 'Schedule' 'ScheduleStopTime' 'Stop Time' `
            'Time of day to automatically stop keeping the PC awake.' 'time' $script:config.ScheduleStopTime
        Add-SettingRow $panelSchedule 'Schedule' 'ScheduleDays' 'Active Days' `
            'Which days of the week the schedule should be active.' 'days' $script:config.ScheduleDays

        $tabs.TabPages.Add($tabSchedule)

        # ============ ADVANCED TAB ============
        $tabAdvanced = New-Object System.Windows.Forms.TabPage
        $tabAdvanced.Text = 'Advanced'
        $tabAdvanced.AutoScroll = $true
        $panelAdvanced = New-Object System.Windows.Forms.Panel
        $panelAdvanced.Dock = 'Fill'
        $panelAdvanced.AutoScroll = $true
        $tabAdvanced.Controls.Add($panelAdvanced)

        Add-SettingRow $panelAdvanced 'Advanced' 'MaxLogSizeMB' 'Max Log File Size (MB)' `
            'Log file is rotated when it exceeds this size. Old logs are kept as backups.' 'number' $script:config.MaxLogSizeMB @{Min=1;Max=100}
        Add-SettingRow $panelAdvanced 'Advanced' 'ProcessIsolation' 'Process Isolation' `
            'Run the keep-awake API call in a separate runspace for extra reliability.' 'bool' $script:config.ProcessIsolation
        Add-SettingRow $panelAdvanced 'Advanced' 'EnablePerformanceMetrics' 'Performance Metrics' `
            'Track internal performance metrics (CPU, memory) for diagnostics.' 'bool' $script:config.EnablePerformanceMetrics
        Add-SettingRow $panelAdvanced 'Advanced' 'EnableTelemetry' 'Anonymous Telemetry' `
            'Send anonymous usage statistics to help improve Redball.' 'bool' $script:config.EnableTelemetry
        Add-SettingRow $panelAdvanced 'Advanced' 'UpdateChannel' 'Update Channel' `
            'Which release channel to check for updates.' 'dropdown' $script:config.UpdateChannel @{Items=@('stable','beta')}
        Add-SettingRow $panelAdvanced 'Advanced' 'VerifyUpdateSignature' 'Verify Update Signatures' `
            'Require valid digital signatures on downloaded updates before installing.' 'bool' $script:config.VerifyUpdateSignature
        Add-SettingRow $panelAdvanced 'Advanced' 'UpdateRepoOwner' 'Update Repository Owner' `
            'GitHub account or organization that hosts Redball releases.' 'text' $script:config.UpdateRepoOwner
        Add-SettingRow $panelAdvanced 'Advanced' 'UpdateRepoName' 'Update Repository Name' `
            'GitHub repository name used for checking updates.' 'text' $script:config.UpdateRepoName

        $tabs.TabPages.Add($tabAdvanced)

        # ============ BUTTON PANEL ============
        $buttonPanel = New-Object System.Windows.Forms.Panel
        $buttonPanel.Dock = 'Bottom'
        $buttonPanel.Height = 50

        $okButton = New-Object System.Windows.Forms.Button
        $okButton.Text = 'OK'
        $okButton.Size = New-Object System.Drawing.Size(90, 30)
        $okButton.Location = New-Object System.Drawing.Point(310, 10)
        $okButton.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $okButton.FlatStyle = 'Flat'
        $okButton.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 215)
        $okButton.ForeColor = [System.Drawing.Color]::White
        $form.AcceptButton = $okButton

        $cancelButton = New-Object System.Windows.Forms.Button
        $cancelButton.Text = 'Cancel'
        $cancelButton.Size = New-Object System.Drawing.Size(90, 30)
        $cancelButton.Location = New-Object System.Drawing.Point(408, 10)
        $cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
        $cancelButton.FlatStyle = 'Flat'
        $form.CancelButton = $cancelButton

        $buttonPanel.Controls.Add($okButton)
        $buttonPanel.Controls.Add($cancelButton)

        $form.Controls.Add($tabs)
        $form.Controls.Add($buttonPanel)

        $result = $form.ShowDialog()

        if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
            # Apply General settings
            $script:config.DefaultDuration = [int]$script:settingsControls['DefaultDuration'].Value
            $script:config.HeartbeatSeconds = [int]$script:settingsControls['HeartbeatSeconds'].Value
            $script:config.ShowBalloonOnStart = $script:settingsControls['ShowBalloonOnStart'].Checked
            $script:config.MinimizeOnStart = $script:settingsControls['MinimizeOnStart'].Checked
            $script:config.AutoExitOnComplete = $script:settingsControls['AutoExitOnComplete'].Checked
            $script:config.Locale = $script:settingsControls['Locale'].SelectedItem

            # Apply Power & Monitoring settings
            $script:config.PreventDisplaySleep = $script:settingsControls['PreventDisplaySleep'].Checked
            $script:state.PreventDisplaySleep = $script:config.PreventDisplaySleep
            $script:config.UseHeartbeatKeypress = $script:settingsControls['UseHeartbeatKeypress'].Checked
            $script:state.UseHeartbeatKeypress = $script:config.UseHeartbeatKeypress
            $script:config.BatteryAware = $script:settingsControls['BatteryAware'].Checked
            $script:state.BatteryAware = $script:config.BatteryAware
            $script:config.BatteryThreshold = [int]$script:settingsControls['BatteryThreshold'].Value
            $script:state.BatteryThreshold = $script:config.BatteryThreshold
            $script:config.NetworkAware = $script:settingsControls['NetworkAware'].Checked
            $script:state.NetworkAware = $script:config.NetworkAware
            $script:config.IdleDetection = $script:settingsControls['IdleDetection'].Checked
            $script:state.IdleDetection = $script:config.IdleDetection
            $script:config.PresentationModeDetection = $script:settingsControls['PresentationModeDetection'].Checked

            # Apply Schedule settings
            $script:config.ScheduleEnabled = $script:settingsControls['ScheduleEnabled'].Checked
            $script:config.ScheduleStartTime = $script:settingsControls['ScheduleStartTime'].Text
            $script:config.ScheduleStopTime = $script:settingsControls['ScheduleStopTime'].Text
            $checkedDays = @()
            $daysControl = $script:settingsControls['ScheduleDays']
            for ($i = 0; $i -lt $daysControl.Items.Count; $i++) {
                if ($daysControl.GetItemChecked($i)) {
                    $checkedDays += $daysControl.Items[$i]
                }
            }
            $script:config.ScheduleDays = $checkedDays

            # Apply Advanced settings
            $script:config.MaxLogSizeMB = [int]$script:settingsControls['MaxLogSizeMB'].Value
            $script:config.ProcessIsolation = $script:settingsControls['ProcessIsolation'].Checked
            $script:config.EnablePerformanceMetrics = $script:settingsControls['EnablePerformanceMetrics'].Checked
            $script:config.EnableTelemetry = $script:settingsControls['EnableTelemetry'].Checked
            $script:config.UpdateChannel = $script:settingsControls['UpdateChannel'].SelectedItem
            $script:config.VerifyUpdateSignature = $script:settingsControls['VerifyUpdateSignature'].Checked
            $script:config.UpdateRepoOwner = $script:settingsControls['UpdateRepoOwner'].Text
            $script:config.UpdateRepoName = $script:settingsControls['UpdateRepoName'].Text

            # Sync heartbeat interval to state and running timer
            $script:state.HeartbeatSeconds = $script:config.HeartbeatSeconds
            if ($script:state.HeartbeatTimer) {
                $script:state.HeartbeatTimer.Interval = $script:state.HeartbeatSeconds * 1000
            }

            Save-RedballConfig -Path $ConfigPath
            Update-RedballUI
            Write-RedballLog -Level 'INFO' -Message 'Settings updated via dialog.'
        }

        $form.Dispose()
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Settings dialog error: $_"
    }
}

function Set-ActiveState {
    <#
    .SYNOPSIS
        Activates or deactivates the keep-awake functionality.
    .DESCRIPTION
        Sets the active state, updates the execution state via SetThreadExecutionState,
        and updates the UI. Can optionally show a balloon notification.
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
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [bool]$Active,
        [Nullable[datetime]]$Until,
        [bool]$ShowBalloon = $true
    )
    if ($script:state.IsShuttingDown) { return }

    $targetState = if ($Active) { 'active' } else { 'paused' }
    if (-not $PSCmdlet.ShouldProcess('Redball keep-awake state', "Set state to $targetState")) {
        return
    }

    try {
        $script:state.Active = $Active
        $script:state.Until = if ($Active) { $Until } else { $null }

        if ($script:config.ProcessIsolation) {
            if ($script:state.Active) {
                if (-not $script:state.KeepAwakeRunspaceInfo) {
                    $script:state.KeepAwakeRunspaceInfo = Start-KeepAwakeRunspace
                }

                if (-not $script:state.KeepAwakeRunspaceInfo) {
                    # Fallback to direct API path if isolated runspace cannot start.
                    Set-KeepAwakeState -Enable:$true
                }
            }
            else {
                if ($script:state.KeepAwakeRunspaceInfo) {
                    Stop-KeepAwakeRunspace -RunspaceInfo $script:state.KeepAwakeRunspaceInfo
                    $script:state.KeepAwakeRunspaceInfo = $null
                }
                Set-KeepAwakeState -Enable:$false
            }
        }
        else {
            Set-KeepAwakeState -Enable:$script:state.Active
        }

        Send-RedballTelemetry -TelemetryEvent 'StateChanged' -Data @{
            Active = $script:state.Active
            Timed = [bool]$script:state.Until
        }

        Update-RedballUI
        if ($ShowBalloon) {
            if ($script:state.NotifyIcon -and -not $script:state.NotifyIcon.IsDisposed) {
                try {
                    $script:state.NotifyIcon.ShowBalloonTip(1500)
                }
                catch {
                    Write-RedballLog -Level 'DEBUG' -Message "Balloon tip display skipped: $_"
                }
            }
        }
    }
    catch [System.Management.Automation.PipelineStoppedException] {
        Exit-Application
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Failed to set active state: $_"
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
    try {
        $stateData = @{
            Active = $script:state.Active
            PreventDisplaySleep = $script:state.PreventDisplaySleep
            UseHeartbeatKeypress = $script:state.UseHeartbeatKeypress
            Until = if ($script:state.Until) { $script:state.Until.ToString('o') } else { $null }
            BatteryAware = $script:state.BatteryAware
            BatteryThreshold = $script:state.BatteryThreshold
            SavedAt = (Get-Date).ToString('o')
        }
        $stateData | ConvertTo-Json | Set-Content -Path $Path -Encoding UTF8
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to save state: $_"
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
        $scriptPath = Join-Path $PSScriptRoot 'Redball.ps1'
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
        
        $shortcut.WorkingDirectory = $PSScriptRoot
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
    # Return cached result if still fresh (30-second TTL)
    if ($script:lastBatteryCheck -and ((Get-Date) - $script:lastBatteryCheck).TotalSeconds -lt 30) {
        return $script:lastBatteryResult
    }
    
    try {
        $battery = Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($battery) {
            $powerStatus = Get-CimInstance -ClassName BatteryStatus -Namespace 'root/wmi' -ErrorAction SilentlyContinue | Select-Object -First 1
            $isOnBattery = $powerStatus.PowerOnLine -eq $false
            $estimatedCharge = $battery.EstimatedChargeRemaining
            
            $script:lastBatteryResult = @{
                HasBattery = $true
                OnBattery = $isOnBattery
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
        Version = $script:VERSION
        Active = $script:state.Active
        Until = if ($script:state.Until) { $script:state.Until.ToString('o') } else { $null }
        PreventDisplaySleep = $script:state.PreventDisplaySleep
        UseHeartbeatKeypress = $script:state.UseHeartbeatKeypress
        BatteryAware = $script:state.BatteryAware
        OnBattery = $battery.OnBattery
        BatteryPercent = if ($battery.HasBattery) { $battery.ChargePercent } else { $null }
        HasBattery = $battery.HasBattery
        AutoPausedBattery = $script:state.AutoPausedBattery
        Uptime = if ($script:state.StartTime) { ([DateTime]::Now - $script:state.StartTime).ToString() } else { $null }
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
    catch {}
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
$idleMenuItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Idle Detection (30min)'
$idleMenuItem.CheckOnClick = $true
$idleMenuItem.Checked = $script:state.IdleDetection
$idleMenuItem.ShortcutKeyDisplayString = 'L'
$idleMenuItem.AccessibleName = 'Idle Detection Toggle'
$idleMenuItem.AccessibleDescription = 'Auto-pause when user idle for 30 minutes'
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
                IsConnected = $true
                Name = $adapter.Name
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

function Test-PresentationMode {
    try {
        # Check for PowerPoint presentation mode
        $powerPoint = Get-Process -Name "POWERPNT" -ErrorAction SilentlyContinue
        if ($powerPoint) {
            # PowerPoint is running
            return @{ IsPresenting = $true; Source = 'PowerPoint' }
        }
        
        # Check for Teams screenshare ( Teams.exe with high CPU or specific window title)
        $teams = Get-Process -Name "Teams" -ErrorAction SilentlyContinue
        if ($teams) {
            # Check if Teams window title contains "Sharing" or "Presenting"
            $teamsWindow = $teams.MainWindowTitle
            if ($teamsWindow -match "Sharing|Presenting|Screen sharing") {
                return @{ IsPresenting = $true; Source = 'Teams' }
            }
        }
        
        # Check Windows presentation settings (Windows 10/11)
        $presentationSettings = Get-ItemProperty -Path "HKCU:\Software\Microsoft\MobilePC\AdaptableSettings" -Name "PresentationMode" -ErrorAction SilentlyContinue
        if ($presentationSettings -and $presentationSettings.PresentationMode -eq 1) {
            return @{ IsPresenting = $true; Source = 'Windows Presentation Mode' }
        }
        
        return @{ IsPresenting = $false }
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
    StartTime = $null
    HeartbeatCount = 0
    LastMetricLog = $null
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
                } else { 'N/A' }
                
                $metrics = @{
                    Timestamp = $now.ToString('o')
                    Uptime = $uptime
                    Heartbeats = $script:performanceMetrics.HeartbeatCount
                    CPU = [math]::Round($process.TotalProcessorTime.TotalSeconds, 2)
                    MemoryMB = [math]::Round($process.WorkingSet64 / 1MB, 2)
                    Handles = $process.HandleCount
                    Data = $Data
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
    param([string]$CrashFlagPath = (Join-Path $PSScriptRoot 'Redball.crash.flag'))
    
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
    param([string]$CrashFlagPath = (Join-Path $PSScriptRoot 'Redball.crash.flag'))
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
    try {
        $owner = $script:config.UpdateRepoOwner
        $repo = $script:config.UpdateRepoName
        $uri = "https://api.github.com/repos/$owner/$repo/releases/latest"

        $headers = @{ 'User-Agent' = 'Redball-Updater' }
        $release = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get -TimeoutSec 15

        if (-not $release -or -not $release.tag_name) {
            return $null
        }

        return $release
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to fetch latest release info: $_"
        return $null
    }
}

function Test-RedballUpdateAvailable {
    try {
        $release = Get-RedballLatestRelease
        if (-not $release) {
            return @{
                UpdateAvailable = $false
                CurrentVersion = $script:VERSION
                LatestVersion = $null
                Release = $null
                Reason = 'Unable to query latest release'
            }
        }

        $current = [version]($script:VERSION -replace '^v', '')
        $latest = [version]($release.tag_name -replace '^v', '')

        return @{
            UpdateAvailable = ($latest -gt $current)
            CurrentVersion = $current.ToString()
            LatestVersion = $latest.ToString()
            Release = $release
            Reason = $null
        }
    }
    catch {
        Write-RedballLog -Level 'WARN' -Message "Failed to evaluate update status: $_"
        return @{
            UpdateAvailable = $false
            CurrentVersion = $script:VERSION
            LatestVersion = $null
            Release = $null
            Reason = "Version parse/evaluation failed: $_"
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
        $scriptPath = Join-Path $PSScriptRoot 'Redball.ps1'
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

        Write-RedballLog -Level 'INFO' -Message "Updated Redball from $($status.CurrentVersion) to $($status.LatestVersion). Backup: $backupPath"
        Write-Output "Update installed. Backup created: $backupPath"

        if ($RestartAfterUpdate) {
            Start-Process -FilePath 'powershell.exe' -ArgumentList "-ExecutionPolicy Bypass -File `"$scriptPath`""
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
        [string]$Path = (Join-Path $PSScriptRoot 'Redball.backup.json'),
        [switch]$Encrypt = $false
    )
    try {
        $settings = @{
            Config = $script:config
            State = @{
                BatteryAware = $script:state.BatteryAware
                BatteryThreshold = $script:state.BatteryThreshold
                NetworkAware = $script:state.NetworkAware
                IdleDetection = $script:state.IdleDetection
                IdleThresholdMinutes = $script:state.IdleThresholdMinutes
            }
            ExportedAt = (Get-Date).ToString('o')
            Version = $script:VERSION
        }
        
        $json = $settings | ConvertTo-Json -Depth 5
        
        if ($Encrypt) {
            # Simple obfuscation (not true encryption, but better than plain text)
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
        [string]$Path = (Join-Path $PSScriptRoot 'Redball.backup.json'),
        [switch]$Encrypted = $false
    )
    try {
        if (-not (Test-Path $Path)) {
            Write-RedballLog -Level 'WARN' -Message "Backup file not found: $Path"
            return $false
        }
        
        $content = Get-Content $Path -Raw -Encoding UTF8
        
        if ($Encrypted) {
            # Decode from base64
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

# --- Settings GUI Dialog ---

function Show-RedballSettingsDialog {
    try {
        $form = New-Object System.Windows.Forms.Form
        $form.Text = 'Redball Settings'
        $form.Size = New-Object System.Drawing.Size 500, 400
        $form.StartPosition = 'CenterScreen'
        $form.FormBorderStyle = 'FixedDialog'
        $form.MaximizeBox = $false
        $form.MinimizeBox = $false
        
        # Tab Control
        $tabControl = New-Object System.Windows.Forms.TabControl
        $tabControl.Location = New-Object System.Drawing.Point 10, 10
        $tabControl.Size = New-Object System.Drawing.Size 465, 300
        
        # General Tab
        $tabGeneral = New-Object System.Windows.Forms.TabPage
        $tabGeneral.Text = 'General'
        
        $lblHeartbeat = New-Object System.Windows.Forms.Label
        $lblHeartbeat.Location = New-Object System.Drawing.Point 10, 15
        $lblHeartbeat.Size = New-Object System.Drawing.Size 150, 20
        $lblHeartbeat.Text = 'Heartbeat Interval (sec):'
        
        $numHeartbeat = New-Object System.Windows.Forms.NumericUpDown
        $numHeartbeat.Location = New-Object System.Drawing.Point 170, 13
        $numHeartbeat.Size = New-Object System.Drawing.Size 80, 20
        $numHeartbeat.Minimum = 10
        $numHeartbeat.Maximum = 300
        $numHeartbeat.Value = $script:config.HeartbeatSeconds
        
        $chkDisplaySleep = New-Object System.Windows.Forms.CheckBox
        $chkDisplaySleep.Location = New-Object System.Drawing.Point 10, 45
        $chkDisplaySleep.Size = New-Object System.Drawing.Size 250, 20
        $chkDisplaySleep.Text = 'Prevent Display Sleep'
        $chkDisplaySleep.Checked = $script:config.PreventDisplaySleep
        
        $chkHeartbeat = New-Object System.Windows.Forms.CheckBox
        $chkHeartbeat.Location = New-Object System.Drawing.Point 10, 70
        $chkHeartbeat.Size = New-Object System.Drawing.Size 250, 20
        $chkHeartbeat.Text = 'Use F15 Heartbeat Keypress'
        $chkHeartbeat.Checked = $script:config.UseHeartbeatKeypress
        
        $chkStartup = New-Object System.Windows.Forms.CheckBox
        $chkStartup.Location = New-Object System.Drawing.Point 10, 95
        $chkStartup.Size = New-Object System.Drawing.Size 250, 20
        $chkStartup.Text = 'Start with Windows'
        $chkStartup.Checked = (Test-RedballStartup)
        
        $tabGeneral.Controls.AddRange(@($lblHeartbeat, $numHeartbeat, $chkDisplaySleep, $chkHeartbeat, $chkStartup))
        
        # Smart Features Tab
        $tabSmart = New-Object System.Windows.Forms.TabPage
        $tabSmart.Text = 'Smart Features'
        
        $chkBattery = New-Object System.Windows.Forms.CheckBox
        $chkBattery.Location = New-Object System.Drawing.Point 10, 15
        $chkBattery.Size = New-Object System.Drawing.Size 250, 20
        $chkBattery.Text = 'Battery-Aware Mode'
        $chkBattery.Checked = $script:state.BatteryAware
        
        $lblBatteryThreshold = New-Object System.Windows.Forms.Label
        $lblBatteryThreshold.Location = New-Object System.Drawing.Point 30, 40
        $lblBatteryThreshold.Size = New-Object System.Drawing.Size 150, 20
        $lblBatteryThreshold.Text = 'Pause below (%):'
        
        $numBatteryThreshold = New-Object System.Windows.Forms.NumericUpDown
        $numBatteryThreshold.Location = New-Object System.Drawing.Point 180, 38
        $numBatteryThreshold.Size = New-Object System.Drawing.Size 60, 20
        $numBatteryThreshold.Minimum = 5
        $numBatteryThreshold.Maximum = 50
        $numBatteryThreshold.Value = $script:state.BatteryThreshold
        
        $chkNetwork = New-Object System.Windows.Forms.CheckBox
        $chkNetwork.Location = New-Object System.Drawing.Point 10, 70
        $chkNetwork.Size = New-Object System.Drawing.Size 250, 20
        $chkNetwork.Text = 'Network-Aware Mode'
        $chkNetwork.Checked = $script:state.NetworkAware
        
        $chkIdle = New-Object System.Windows.Forms.CheckBox
        $chkIdle.Location = New-Object System.Drawing.Point 10, 95
        $chkIdle.Size = New-Object System.Drawing.Size 250, 20
        $chkIdle.Text = 'Idle Detection (30min)'
        $chkIdle.Checked = $script:state.IdleDetection
        
        $chkSchedule = New-Object System.Windows.Forms.CheckBox
        $chkSchedule.Location = New-Object System.Drawing.Point 10, 120
        $chkSchedule.Size = New-Object System.Drawing.Size 250, 20
        $chkSchedule.Text = 'Scheduled Operation'
        $chkSchedule.Checked = $script:config.ScheduleEnabled
        
        $tabSmart.Controls.AddRange(@($chkBattery, $lblBatteryThreshold, $numBatteryThreshold, $chkNetwork, $chkIdle, $chkSchedule))
        
        # Logging Tab
        $tabLogging = New-Object System.Windows.Forms.TabPage
        $tabLogging.Text = 'Logging'
        
        $lblLogPath = New-Object System.Windows.Forms.Label
        $lblLogPath.Location = New-Object System.Drawing.Point 10, 15
        $lblLogPath.Size = New-Object System.Drawing.Size 80, 20
        $lblLogPath.Text = 'Log Path:'
        
        $txtLogPath = New-Object System.Windows.Forms.TextBox
        $txtLogPath.Location = New-Object System.Drawing.Point 100, 13
        $txtLogPath.Size = New-Object System.Drawing.Size 300, 20
        $txtLogPath.Text = $script:config.LogPath
        
        $lblMaxSize = New-Object System.Windows.Forms.Label
        $lblMaxSize.Location = New-Object System.Drawing.Point 10, 45
        $lblMaxSize.Size = New-Object System.Drawing.Size 150, 20
        $lblMaxSize.Text = 'Max Log Size (MB):'
        
        $numMaxSize = New-Object System.Windows.Forms.NumericUpDown
        $numMaxSize.Location = New-Object System.Drawing.Point 170, 43
        $numMaxSize.Size = New-Object System.Drawing.Size 80, 20
        $numMaxSize.Minimum = 1
        $numMaxSize.Maximum = 100
        $numMaxSize.Value = $script:config.MaxLogSizeMB
        
        $chkMetrics = New-Object System.Windows.Forms.CheckBox
        $chkMetrics.Location = New-Object System.Drawing.Point 10, 75
        $chkMetrics.Size = New-Object System.Drawing.Size 250, 20
        $chkMetrics.Text = 'Enable Performance Metrics'
        $chkMetrics.Checked = $script:config.EnablePerformanceMetrics
        
        $tabLogging.Controls.AddRange(@($lblLogPath, $txtLogPath, $lblMaxSize, $numMaxSize, $chkMetrics))
        
        # Add tabs
        $tabControl.TabPages.Add($tabGeneral)
        $tabControl.TabPages.Add($tabSmart)
        $tabControl.TabPages.Add($tabLogging)
        $form.Controls.Add($tabControl)
        
        # Buttons
        $btnSave = New-Object System.Windows.Forms.Button
        $btnSave.Location = New-Object System.Drawing.Point 300, 320
        $btnSave.Size = New-Object System.Drawing.Size 80, 25
        $btnSave.Text = 'Save'
        $btnSave.DialogResult = [System.Windows.Forms.DialogResult]::OK
        
        $btnCancel = New-Object System.Windows.Forms.Button
        $btnCancel.Location = New-Object System.Drawing.Point 395, 320
        $btnCancel.Size = New-Object System.Drawing.Size 80, 25
        $btnCancel.Text = 'Cancel'
        $btnCancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
        
        $form.Controls.AddRange(@($btnSave, $btnCancel))
        $form.AcceptButton = $btnSave
        $form.CancelButton = $btnCancel
        
        # Show dialog
        $result = $form.ShowDialog()
        
        if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
            # Apply settings
            $script:config.HeartbeatSeconds = [int]$numHeartbeat.Value
            $script:config.PreventDisplaySleep = $chkDisplaySleep.Checked
            $script:config.UseHeartbeatKeypress = $chkHeartbeat.Checked
            $script:config.LogPath = $txtLogPath.Text
            $script:config.MaxLogSizeMB = [int]$numMaxSize.Value
            $script:config.EnablePerformanceMetrics = $chkMetrics.Checked
            
            # Smart features
            $script:state.BatteryAware = $chkBattery.Checked
            $script:state.BatteryThreshold = [int]$numBatteryThreshold.Value
            $script:state.NetworkAware = $chkNetwork.Checked
            $script:state.IdleDetection = $chkIdle.Checked
            $script:config.ScheduleEnabled = $chkSchedule.Checked
            
            # Handle startup
            if ($chkStartup.Checked -ne (Test-RedballStartup)) {
                if ($chkStartup.Checked) {
                    Install-RedballStartup | Out-Null
                } else {
                    Uninstall-RedballStartup | Out-Null
                }
            }
            
            # Save config
            Save-RedballConfig -Path $ConfigPath
            
            Write-RedballLog -Level 'INFO' -Message 'Settings updated via GUI dialog'
            Update-RedballUI
            
            return $true
        }
        
        return $false
    }
    catch {
        Write-RedballLog -Level 'ERROR' -Message "Settings dialog error: $_"
        return $false
    }
}

# --- Telemetry (Opt-in) ---

function Send-RedballTelemetry {
    param(
        [string]$TelemetryEvent,
        [hashtable]$Data = @{}
    )
    
    # Only send if user has opted in
    if (-not $script:config.EnableTelemetry) { return }
    
    try {
        $telemetry = @{
            Version = $script:VERSION
            Event = $TelemetryEvent
            Timestamp = (Get-Date).ToString('o')
            SessionId = $script:state.SessionId
            Data = $Data
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
                [Win32.Power]::SetThreadExecutionState(0x80000003)  # ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
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

# Clear crash flag on clean exit
$onProcessExit = {
    Clear-CrashFlag
}
[AppDomain]::CurrentDomain.add_ProcessExit($onProcessExit)

[System.Windows.Forms.Application]::Run($script:state.Context)
