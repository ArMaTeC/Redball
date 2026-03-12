using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Redball.UI.Services;

/// <summary>
/// Manages loading, saving, and validating Redball configuration from Redball.json.
/// </summary>
public class ConfigService
{
    private static readonly string DefaultConfigPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Redball.json");

    private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService());
    public static ConfigService Instance => _instance.Value;

    public RedballConfig Config { get; private set; } = new();
    public string ConfigPath { get; private set; } = "";
    public bool IsDirty { get; set; }

    private ConfigService() { }

    public bool Load(string? path = null)
    {
        ConfigPath = ResolveConfigPath(path);
        if (string.IsNullOrEmpty(ConfigPath) || !File.Exists(ConfigPath))
        {
            Config = new RedballConfig();
            return false;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            Config = JsonSerializer.Deserialize<RedballConfig>(json, options) ?? new RedballConfig();
            IsDirty = false;
            return true;
        }
        catch
        {
            Config = new RedballConfig();
            return false;
        }
    }

    public bool Save(string? path = null)
    {
        var savePath = path ?? ConfigPath;
        if (string.IsNullOrEmpty(savePath)) return false;

        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            };
            var json = JsonSerializer.Serialize(Config, options);
            File.WriteAllText(savePath, json);
            IsDirty = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Config.HeartbeatSeconds < 10 || Config.HeartbeatSeconds > 300)
            errors.Add("HeartbeatSeconds must be between 10 and 300");

        if (Config.DefaultDuration < 1 || Config.DefaultDuration > 720)
            errors.Add("DefaultDuration must be between 1 and 720 minutes");

        if (Config.BatteryThreshold < 5 || Config.BatteryThreshold > 95)
            errors.Add("BatteryThreshold must be between 5 and 95%");

        if (Config.TypeThingMinDelayMs >= Config.TypeThingMaxDelayMs)
            errors.Add("TypeThingMinDelayMs must be less than TypeThingMaxDelayMs");

        if (Config.TypeThingStartDelaySec < 0 || Config.TypeThingStartDelaySec > 30)
            errors.Add("TypeThingStartDelaySec must be between 0 and 30");

        if (Config.MaxLogSizeMB < 1 || Config.MaxLogSizeMB > 100)
            errors.Add("MaxLogSizeMB must be between 1 and 100");

        return errors;
    }

    private static string ResolveConfigPath(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return path;

        // Try multiple locations
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Redball.json"),
            Path.GetFullPath(DefaultConfigPath),
            Path.Combine(Environment.CurrentDirectory, "Redball.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "Redball.json"),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch { }
        }

        // Default to base directory even if file doesn't exist yet
        return Path.Combine(AppContext.BaseDirectory, "Redball.json");
    }
}

/// <summary>
/// Strongly-typed Redball configuration matching Redball.json schema.
/// </summary>
public class RedballConfig
{
    public int HeartbeatSeconds { get; set; } = 59;
    public bool PreventDisplaySleep { get; set; } = true;
    public bool UseHeartbeatKeypress { get; set; } = true;
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
    public string UpdateRepoOwner { get; set; } = "karl-lawrence";
    public string UpdateRepoName { get; set; } = "Redball";
    public string UpdateChannel { get; set; } = "stable";
    public bool VerifyUpdateSignature { get; set; }
    public bool TypeThingEnabled { get; set; } = true;
    public int TypeThingMinDelayMs { get; set; } = 30;
    public int TypeThingMaxDelayMs { get; set; } = 120;
    public int TypeThingStartDelaySec { get; set; } = 3;
    public string TypeThingStartHotkey { get; set; } = "Ctrl+Shift+V";
    public string TypeThingStopHotkey { get; set; } = "Ctrl+Shift+X";
    public string TypeThingTheme { get; set; } = "dark";
    public bool TypeThingAddRandomPauses { get; set; } = true;
    public int TypeThingRandomPauseChance { get; set; } = 5;
    public int TypeThingRandomPauseMaxMs { get; set; } = 500;
    public bool TypeThingTypeNewlines { get; set; } = true;
    public bool TypeThingNotifications { get; set; } = true;
    public bool VerboseLogging { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool ShowNotifications { get; set; } = true;
    public NotificationMode NotificationMode { get; set; } = NotificationMode.All;
    public int IdleThreshold { get; set; } = 30;
    public bool PresentationMode { get; set; }
    public bool ScheduledOperation { get; set; }
    public string Theme { get; set; } = "Dark";
}

public enum NotificationMode
{
    All,
    Important,
    Errors,
    Silent
}
