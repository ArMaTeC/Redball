using System.Collections.Generic;

namespace Redball.UI.Services;

/// <summary>
/// Strongly-typed Redball configuration matching Redball.json schema.
/// </summary>
public class RedballConfig
{
    public int HeartbeatSeconds { get; set; } = 59;
    public bool PreventDisplaySleep { get; set; } = true;
    public bool UseHeartbeatKeypress { get; set; } = true;
    public string HeartbeatInputMode { get; set; } = "F15";
    public int DefaultDuration { get; set; } = 60;
    public string LogPath { get; set; } = "Redball.log";
    public int MaxLogSizeMB { get; set; } = 10;
    public bool ShowBalloonOnStart { get; set; } = true;
    public string Locale { get; set; } = "en";
    public bool MinimizeOnStart { get; set; }
    public bool BatteryAware { get; set; }
    public int BatteryThreshold { get; set; } = 20;
    public bool NetworkAware { get; set; }
    public bool IdleDetection { get; set; }
    public bool AutoExitOnComplete { get; set; }
    public bool ScheduleEnabled { get; set; }
    public string ScheduleStartTime { get; set; } = "09:00";
    public string ScheduleStopTime { get; set; } = "18:00";
    public List<string> ScheduleDays { get; set; } = new() { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
    public bool PresentationModeDetection { get; set; }
    public bool ProcessIsolation { get; set; }
    public bool EnablePerformanceMetrics { get; set; }
    public bool EnableTelemetry { get; set; }
    public string UpdateRepoOwner { get; set; } = "ArMaTeC";
    public string UpdateRepoName { get; set; } = "Redball";
    public string UpdateChannel { get; set; } = "stable";
    public bool VerifyUpdateSignature { get; set; }
    public bool AutoUpdateCheckEnabled { get; set; } = true;
    public int AutoUpdateCheckIntervalMinutes { get; set; } = 120;
    public bool TypeThingEnabled { get; set; } = true;
    public int TypeThingMinDelayMs { get; set; } = 30;
    public int TypeThingMaxDelayMs { get; set; } = 120;
    public int TypeThingStartDelaySec { get; set; } = 3;
    public string TypeThingStartHotkey { get; set; } = "Ctrl+Shift+V";
    public string TypeThingStopHotkey { get; set; } = "Ctrl+Shift+X";
    public string TypeThingTheme { get; set; } = "dark";
    public bool UseLowLevelHotkey { get; set; } = false;
    public bool TypeThingAddRandomPauses { get; set; } = true;
    public int TypeThingRandomPauseChance { get; set; } = 5;
    public int TypeThingRandomPauseMaxMs { get; set; } = 500;
    public bool TypeThingTypeNewlines { get; set; } = true;
    public bool TypeThingNotifications { get; set; } = true;
    public string TypeThingInputMode { get; set; } = "SendInput";
    public bool TypeThingHidSafeMode { get; set; }
    public bool VerboseLogging { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool ShowNotifications { get; set; } = true;
    public bool SoundNotifications { get; set; }
    public NotificationMode NotificationMode { get; set; } = NotificationMode.All;
    public int IdleThreshold { get; set; } = 30;
    public bool PresentationMode { get; set; }
    public bool ScheduledOperation { get; set; }
    public bool ConfirmOnExit { get; set; } = true;
    public bool FirstRun { get; set; } = true;
    public string Theme { get; set; } = "Dark";

    // Pomodoro
    public bool PomodoroEnabled { get; set; }
    public int PomodoroFocusMinutes { get; set; } = 25;
    public int PomodoroBreakMinutes { get; set; } = 5;
    public int PomodoroLongBreakMinutes { get; set; } = 15;
    public int PomodoroLongBreakInterval { get; set; } = 4;
    public bool PomodoroAutoStart { get; set; } = true;
    public bool PomodoroKeepAwakeDuringBreak { get; set; }

    // Process Watcher
    public bool ProcessWatcherEnabled { get; set; }
    public string ProcessWatcherTarget { get; set; } = "";

    // Session Lock
    public bool PauseOnScreenLock { get; set; }

    // VPN
    public bool VpnAutoKeepAwake { get; set; }

    // App-specific rules
    public bool AppRulesEnabled { get; set; }
    public string KeepAwakeApps { get; set; } = "";
    public string PauseApps { get; set; } = "";

    // Power Plan
    public bool PowerPlanAutoSwitch { get; set; }

    // WiFi-based profiles (format: "WiFiName=ProfileName" per line)
    public bool WifiProfileSwitchEnabled { get; set; }
    public string WifiProfileMappings { get; set; } = "";

    // Scheduled Restart
    public bool RestartReminderEnabled { get; set; }
    public int RestartReminderDays { get; set; } = 7;
    public bool AutoRestartEnabled { get; set; }

    // Thermal Protection
    public bool ThermalProtectionEnabled { get; set; }
    public int ThermalThreshold { get; set; } = 85;

    // Text-to-Speech
    public bool TypeThingTtsEnabled { get; set; }

    // Local Web API
    public bool WebApiEnabled { get; set; }
    public int WebApiPort { get; set; } = 48080;

    // Mini Widget position (-1 = not set, use default)
    public double MiniWidgetLeft { get; set; } = -1;
    public double MiniWidgetTop { get; set; } = -1;
    public bool MiniWidgetAlwaysOnTop { get; set; } = true;
    public int MiniWidgetOpacityPercent { get; set; } = 92;
    public bool MiniWidgetShowStatusIcons { get; set; } = true;
    public bool MiniWidgetShowQuickActions { get; set; } = true;
    public bool MiniWidgetDoubleClickOpensDashboard { get; set; } = true;
    public bool MiniWidgetOpenOnStartup { get; set; }
    public bool MiniWidgetLockPosition { get; set; }
    public bool MiniWidgetSnapToScreenEdges { get; set; } = true;
    public bool MiniWidgetEnableKeyboardShortcuts { get; set; } = true;
    public int MiniWidgetCustomQuickMinutes { get; set; } = 30;
    public bool MiniWidgetConfirmCloseWhenActive { get; set; } = true;
    public string MiniWidgetPreset { get; set; } = "Custom";

    // Config encryption (DPAPI, current-user scope)
    public bool EncryptConfig { get; set; }

    // Integrity signature (SHA256 of the JSON without this property)
    public string? ConfigSignature { get; set; }
    
    // Salt used for unique device hashing if needed
    public string? ConfigSalt { get; set; }

    // Custom Commands for Palette
    public List<CustomCommandMetadata> CustomCommands { get; set; } = new();
}

public class CustomCommandMetadata
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Command { get; set; } = ""; // Path or URL
    public string Arguments { get; set; } = "";
    public string Icon { get; set; } = "\uE71D"; // Default Segoe Icon (Generic)
}
