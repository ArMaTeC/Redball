using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Redball.UI.Services;

public enum HeartbeatInputMode
{
    Disabled = 0,
    F13 = 1,
    F14 = 2,
    F15 = 3,
    F16 = 4
}

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

    private ConfigService() 
    { 
        Logger.Verbose("ConfigService", "Instance created (lazy initialization)");
    }

    public bool Load(string? path = null)
    {
        ConfigPath = ResolveConfigPath(path);
        Logger.Info("ConfigService", $"Loading configuration from: {ConfigPath}");
        
        if (string.IsNullOrEmpty(ConfigPath))
        {
            Logger.Warning("ConfigService", "Config path is null or empty, using defaults");
            Config = new RedballConfig();
            return false;
        }

        if (!File.Exists(ConfigPath))
        {
            Logger.Info("ConfigService", $"Config file not found at: {ConfigPath}, creating with defaults");
            Config = new RedballConfig();
            // Save defaults to disk so user has a config file to edit
            Save(ConfigPath);
            return true;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            Logger.Debug("ConfigService", $"Config file read: {json.Length} bytes");
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            
            Config = JsonSerializer.Deserialize<RedballConfig>(json, options) ?? new RedballConfig();
            NormalizeConfig();
            IsDirty = false;
            
            Logger.Info("ConfigService", $"Configuration loaded successfully: Heartbeat={Config.HeartbeatSeconds}s, Theme={Config.Theme}");
            Logger.Debug("ConfigService", $"Config details: Duration={Config.DefaultDuration}min, BatteryAware={Config.BatteryAware}, NetworkAware={Config.NetworkAware}");
            
            return true;
        }
        catch (JsonException jsonEx)
        {
            Logger.Error("ConfigService", "Failed to parse configuration JSON", jsonEx);
            Config = new RedballConfig();
            return false;
        }
        catch (IOException ioEx)
        {
            Logger.Error("ConfigService", "IO error reading configuration file", ioEx);
            Config = new RedballConfig();
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigService", "Unexpected error loading configuration", ex);
            Config = new RedballConfig();
            return false;
        }
    }

    public bool Save(string? path = null)
    {
        var savePath = path ?? ConfigPath;
        if (string.IsNullOrEmpty(savePath))
        {
            Logger.Error("ConfigService", "Cannot save: save path is null or empty");
            return false;
        }

        Logger.Info("ConfigService", $"Saving configuration to: {savePath}");

        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            };
            
            var json = JsonSerializer.Serialize(Config, options);
            Logger.Debug("ConfigService", $"Serialized config: {json.Length} bytes");
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Logger.Debug("ConfigService", $"Created directory: {dir}");
            }
            
            File.WriteAllText(savePath, json);
            IsDirty = false;
            
            Logger.Info("ConfigService", "Configuration saved successfully");
            return true;
        }
        catch (JsonException jsonEx)
        {
            Logger.Error("ConfigService", "Failed to serialize configuration", jsonEx);
            return false;
        }
        catch (UnauthorizedAccessException authEx)
        {
            Logger.Error("ConfigService", "Access denied writing configuration file", authEx);
            return false;
        }
        catch (IOException ioEx)
        {
            Logger.Error("ConfigService", "IO error writing configuration file", ioEx);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigService", "Unexpected error saving configuration", ex);
            return false;
        }
    }

    public List<string> Validate()
    {
        var errors = new List<string>();
        Logger.Debug("ConfigService", "Validating configuration...");

        if (Config.HeartbeatSeconds < 10 || Config.HeartbeatSeconds > 300)
        {
            errors.Add("HeartbeatSeconds must be between 10 and 300");
            Logger.Warning("ConfigService", $"Validation: HeartbeatSeconds ({Config.HeartbeatSeconds}) out of range");
        }

        if (Config.DefaultDuration < 1 || Config.DefaultDuration > 720)
        {
            errors.Add("DefaultDuration must be between 1 and 720 minutes");
            Logger.Warning("ConfigService", $"Validation: DefaultDuration ({Config.DefaultDuration}) out of range");
        }

        if (Config.BatteryThreshold < 5 || Config.BatteryThreshold > 95)
        {
            errors.Add("BatteryThreshold must be between 5 and 95%");
            Logger.Warning("ConfigService", $"Validation: BatteryThreshold ({Config.BatteryThreshold}) out of range");
        }

        if (Config.TypeThingMinDelayMs >= Config.TypeThingMaxDelayMs)
        {
            errors.Add("TypeThingMinDelayMs must be less than TypeThingMaxDelayMs");
            Logger.Warning("ConfigService", $"Validation: TypeThing delays invalid ({Config.TypeThingMinDelayMs} >= {Config.TypeThingMaxDelayMs})");
        }

        if (Config.TypeThingStartDelaySec < 0 || Config.TypeThingStartDelaySec > 30)
        {
            errors.Add("TypeThingStartDelaySec must be between 0 and 30");
            Logger.Warning("ConfigService", $"Validation: TypeThingStartDelaySec ({Config.TypeThingStartDelaySec}) out of range");
        }

        if (Config.MaxLogSizeMB < 1 || Config.MaxLogSizeMB > 100)
        {
            errors.Add("MaxLogSizeMB must be between 1 and 100");
            Logger.Warning("ConfigService", $"Validation: MaxLogSizeMB ({Config.MaxLogSizeMB}) out of range");
        }

        if (errors.Count == 0)
        {
            Logger.Debug("ConfigService", "Configuration validation passed");
        }
        else
        {
            Logger.Warning("ConfigService", $"Configuration validation found {errors.Count} errors");
        }

        return errors;
    }

    /// <summary>
    /// Exports current configuration to a backup file.
    /// </summary>
    public bool Export(string exportPath)
    {
        Logger.Info("ConfigService", $"Exporting config to: {exportPath}");
        try
        {
            var backup = new
            {
                ExportedAt = DateTime.Now,
                Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                Config
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(backup, options);
            File.WriteAllText(exportPath, json);
            Logger.Info("ConfigService", "Config exported successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigService", "Failed to export config", ex);
            return false;
        }
    }

    /// <summary>
    /// Imports configuration from a backup file, replacing current config.
    /// </summary>
    public bool Import(string importPath)
    {
        Logger.Info("ConfigService", $"Importing config from: {importPath}");
        try
        {
            if (!File.Exists(importPath))
            {
                Logger.Warning("ConfigService", "Import file not found");
                return false;
            }

            var json = File.ReadAllText(importPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            // Try parsing as backup format first (has wrapper object)
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Config", out var configElement))
                {
                    var configJson = configElement.GetRawText();
                    Config = JsonSerializer.Deserialize<RedballConfig>(configJson, options) ?? new RedballConfig();
                    NormalizeConfig();
                    IsDirty = true;
                    Logger.Info("ConfigService", "Config imported from backup format");
                    return true;
                }
            }
            catch { }

            // Fall back to plain config format
            Config = JsonSerializer.Deserialize<RedballConfig>(json, options) ?? new RedballConfig();
            NormalizeConfig();
            IsDirty = true;
            Logger.Info("ConfigService", "Config imported from plain format");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ConfigService", "Failed to import config", ex);
            return false;
        }
    }

    private static string ResolveConfigPath(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            Logger.Debug("ConfigService", $"Using provided config path: {path}");
            return path;
        }

        // Try multiple locations
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Redball.json"),
            Path.GetFullPath(DefaultConfigPath),
            Path.Combine(Environment.CurrentDirectory, "Redball.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "Redball.json"),
        };

        Logger.Debug("ConfigService", "Searching for config file in candidate locations...");
        foreach (var candidate in candidates)
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    Logger.Debug("ConfigService", $"Found config at: {fullPath}");
                    return fullPath;
                }
            }
            catch (Exception ex)
            {
                Logger.Verbose("ConfigService", $"Error checking candidate '{candidate}': {ex.Message}");
            }
        }

        // Default to base directory even if file doesn't exist yet
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "Redball.json");
        Logger.Debug("ConfigService", $"No existing config found, using default path: {defaultPath}");
        return defaultPath;
    }

    private void NormalizeConfig()
    {
        if (string.Equals(Config.UpdateRepoOwner, "karl-lawrence", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Config.UpdateRepoName, "Redball", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warning("ConfigService", "Detected legacy update repository configuration, migrating to ArMaTeC/Redball");
            Config.UpdateRepoOwner = "ArMaTeC";
            Config.UpdateRepoName = "Redball";
            IsDirty = true;
        }

        if (string.IsNullOrWhiteSpace(Config.UpdateRepoOwner))
        {
            Logger.Warning("ConfigService", "UpdateRepoOwner was empty, defaulting to ArMaTeC");
            Config.UpdateRepoOwner = "ArMaTeC";
            IsDirty = true;
        }

        if (string.IsNullOrWhiteSpace(Config.UpdateRepoName))
        {
            Logger.Warning("ConfigService", "UpdateRepoName was empty, defaulting to Redball");
            Config.UpdateRepoName = "Redball";
            IsDirty = true;
        }

        if (string.IsNullOrWhiteSpace(Config.HeartbeatInputMode))
        {
            Config.HeartbeatInputMode = Config.UseHeartbeatKeypress ? "F15" : "Disabled";
            IsDirty = true;
        }
        else if (!Enum.TryParse<HeartbeatInputMode>(Config.HeartbeatInputMode, true, out _))
        {
            Logger.Warning("ConfigService", $"HeartbeatInputMode '{Config.HeartbeatInputMode}' was invalid, defaulting to F15");
            Config.HeartbeatInputMode = "F15";
            IsDirty = true;
        }

        var useHeartbeatKeypress = !string.Equals(Config.HeartbeatInputMode, "Disabled", StringComparison.OrdinalIgnoreCase);
        if (Config.UseHeartbeatKeypress != useHeartbeatKeypress)
        {
            Config.UseHeartbeatKeypress = useHeartbeatKeypress;
            IsDirty = true;
        }
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
    public bool FirstRun { get; set; } = true;
    public string Theme { get; set; } = "Dark";
}

public enum NotificationMode
{
    All,
    Important,
    Errors,
    Silent
}
