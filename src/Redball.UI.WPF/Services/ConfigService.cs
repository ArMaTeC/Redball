using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Manages loading, saving, and validating Redball configuration from Redball.json.
/// Config is persisted to %LocalAppData%\Redball\UserData\Redball.json — a subfolder
/// the MSI installer does NOT manage, so it survives upgrades/reinstalls.
/// On first run after an update, configs found in legacy locations are auto-migrated.
/// </summary>
public class ConfigService : IConfigService
{
    // UserData subfolder is NOT managed by the MSI (INSTALLFOLDER = %LocalAppData%\Redball)
    // so its contents survive MajorUpgrade cycles that remove/reinstall the install directory.
    private static readonly string UserDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Redball", "UserData");
    private static readonly string LocalAppDataConfigPath = Path.Combine(UserDataDir, "Redball.json");
    private static readonly string BackupConfigPath = Path.Combine(UserDataDir, "Redball.json.bak");
    // Legacy paths for migration from older versions
    private static readonly string LegacyArMaTeCConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArMaTeC", "Redball", "Redball.json");
    private static readonly string LegacyLocalAppDataConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Redball", "Redball.json");
    private static readonly string AppDataConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Redball", "Redball.json");
    private const string RegistryConfigSubKey = @"Software\Redball\UserData";
    private const string RegistryConfigValueName = "ConfigPayload";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService());
    public static ConfigService Instance => _instance.Value;

    public RedballConfig Config { get; internal set; } = new();
    public string ConfigPath { get; private set; } = "";
    public bool IsDirty { get; set; }
    
    /// <summary>
    /// When true, disables automatic discovery scans of AppData folders and prevents
    /// accidental persistence to LocalAppData during unit tests.
    /// </summary>
    public bool IsTestMode { get; set; }

    private static readonly object _syncLock = new();

    private ConfigService() 
    { 
        Logger.Verbose("ConfigService", "Instance created (lazy initialization)");
    }

    private RedballConfig? TryLoadFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryConfigSubKey);
            var payload = key?.GetValue(RegistryConfigValueName) as string;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            string json;
            if (payload.StartsWith(EncryptedHeader, StringComparison.Ordinal))
            {
                json = DpapiDecrypt(payload[EncryptedHeader.Length..]);
            }
            else
            {
                json = payload;
            }

            var config = JsonSerializer.Deserialize<RedballConfig>(json, ReadOptions);
            if (config != null)
            {
                Logger.Info("ConfigService", "Configuration loaded from registry");
            }
            return config;
        }
        catch (Exception ex)
        {
            Logger.Warning("ConfigService", $"Failed to load config from registry: {ex.Message}");
            return null;
        }
    }



    public bool Load(string? path = null)
    {
        lock (_syncLock)
        {
            // Reset to default instance state first to ensure no cross-test pollution
            Config = new RedballConfig();
            IsDirty = false;

        RedballConfig? bestConfig = null;
        string? bestPath = null;

        if (!string.IsNullOrEmpty(path))
        {
            // Explicit path requested (common in tests/overrides).
            // We ONLY use this path and bypass discovery/migration scans
            // to ensure strict isolation and predictable behavior.
            Logger.Info("ConfigService", $"Loading from explicit path: {path}");
            bestConfig = TryLoadFromFile(path) ?? TryRecoverFromFile(path);
            bestPath = path;
            ConfigPath = path;
        }
        else if (IsTestMode)
        {
            // In test mode with no path, we just stick to defaults.
            // DO NOT scan AppData as it breaks isolation.
            Logger.Warning("ConfigService", "Load() called without path in TestMode; using memory-only defaults.");
             ConfigPath = Path.Combine(Path.GetTempPath(), "redball_test_fallback.json");
        }
        else
        {
            var registryConfig = TryLoadFromRegistry();
            if (registryConfig != null)
            {
                bestConfig = registryConfig;
                bestPath = $"HKCU\\{RegistryConfigSubKey} ({RegistryConfigValueName})";
                ConfigPath = LocalAppDataConfigPath;
            }
            else
            {
                // Fallback: file discovery scan
                var candidates = GetConfigPathCandidates(null);
                DateTime latestTime = DateTime.MinValue;

                foreach (var candidate in candidates)
                {
                    if (!File.Exists(candidate)) continue;
                    
                    try
                    {
                        var cfgCandidate = TryLoadFromFile(candidate) ?? TryRecoverFromFile(candidate);
                        
                        if (cfgCandidate != null)
                        {
                            var writeTime = File.GetLastWriteTime(candidate);
                            bool isBetter = false;

                            if (bestConfig == null)
                            {
                                isBetter = true;
                            }
                            else if (!cfgCandidate.FirstRun && bestConfig.FirstRun)
                            {
                                isBetter = true;
                            }
                            else if (cfgCandidate.FirstRun == bestConfig.FirstRun)
                            {
                                if (writeTime > latestTime) isBetter = true;
                            }

                            if (isBetter)
                            {
                                bestConfig = cfgCandidate;
                                bestPath = candidate;
                                latestTime = writeTime;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("ConfigService", $"Failed to scan candidate {candidate}: {ex.Message}");
                    }
                }
            }
        }

        if (bestConfig == null)
        {
            Logger.Info("ConfigService", "No config file loaded; using defaults");
            Config = new RedballConfig();
            if (!IsTestMode) ConfigPath = LocalAppDataConfigPath;
        }
        else
        {
            Config = bestConfig;
            Logger.Info("ConfigService", $"Config finalized from: {bestPath} (FirstRun={Config.FirstRun})");
            
            // Migration check: only happens in real launch
            if (!IsTestMode && !string.IsNullOrEmpty(bestPath) && !string.Equals(bestPath, LocalAppDataConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("ConfigService", $"Migration required: {bestPath} -> {LocalAppDataConfigPath}");
                IsDirty = true;
                ConfigPath = LocalAppDataConfigPath;
            }
        }

        // Always run self-healing
        SanitizeConfig();
        NormalizeConfig();

        // Persist only if necessary and NOT in test mode (unless explicitly requested via Save)
        if (!IsTestMode && (IsDirty || !File.Exists(ConfigPath)))
        {
            SaveNoLock();
        }

        Logger.Info("ConfigService", $"Configuration ready: FirstRun={Config.FirstRun}, HEARTBEAT={Config.HeartbeatSeconds}s, THEME={Config.Theme}");
        return true;
        }
    }




    // Magic header prefixed to DPAPI-encrypted config files
    private const string EncryptedHeader = "RBENC:";

    /// <summary>
    /// Attempts standard JSON deserialization from a file.
    /// Automatically detects and decrypts DPAPI-encrypted files (prefixed with RBENC:).
    /// </summary>
    private RedballConfig? TryLoadFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var raw = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(raw))
            {
                Logger.Warning("ConfigService", $"Config file is empty: {filePath}");
                return null;
            }

            string json;
            if (raw.StartsWith(EncryptedHeader, StringComparison.Ordinal))
            {
                Logger.Debug("ConfigService", $"Detected DPAPI-encrypted config: {filePath}");
                json = DpapiDecrypt(raw[EncryptedHeader.Length..]);
            }
            else
            {
                json = raw;
            }

            Logger.Debug("ConfigService", $"Attempting to deserialize from: {filePath}");
            return JsonSerializer.Deserialize<RedballConfig>(json, ReadOptions);
        }
        catch (CryptographicException cryptoEx)
        {
            Logger.Warning("ConfigService", $"Failed to decrypt config from {filePath}: {cryptoEx.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning("ConfigService", $"Failed to deserialize from {filePath}: {ex.Message}");
            return null;
        }
    }


    /// <summary>
    /// If standard deserialization fails (corrupt JSON), attempt property-by-property recovery.
    /// Reads each JSON property individually and maps it onto a fresh defaults instance,
    /// so one corrupt value doesn't wipe out all the others.
    /// </summary>
    private RedballConfig? TryRecoverFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json)) return null;

            Logger.Warning("ConfigService", $"Attempting property-level recovery from: {filePath}");

            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var defaults = new RedballConfig();
            var recoveredConfig = new RedballConfig();
            var recovered = 0;
            var failed = 0;

            foreach (var prop in typeof(RedballConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;

                try
                {
                    // Try to find this property in the JSON (case-insensitive)
                    JsonElement element = default;
                    var found = false;
                    foreach (var jsonProp in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(jsonProp.Name, prop.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            element = jsonProp.Value;
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        var value = JsonSerializer.Deserialize(element.GetRawText(), prop.PropertyType, ReadOptions);
                        if (value != null)
                        {
                            prop.SetValue(recoveredConfig, value);
                            recovered++;
                            continue;
                        }
                    }

                    // Not found or null — use default
                    failed++;
                    prop.SetValue(recoveredConfig, prop.GetValue(defaults));
                }
                catch
                {
                    failed++;
                    try { prop.SetValue(recoveredConfig, prop.GetValue(defaults)); } catch { }
                }
            }

            Logger.Info("ConfigService", $"Property-level recovery: {recovered} recovered, {failed} failed (used defaults)");
            return recovered > 0 ? recoveredConfig : null;
        }
        catch (Exception ex)
        {
            Logger.Warning("ConfigService", $"Property-level recovery failed for {filePath}: {ex.Message}");
            return null;
        }
    }


    public bool Save(string? path = null)
    {
        lock (_syncLock)
        {
            return SaveNoLock(path);
        }
    }

    private bool SaveNoLock(string? path = null)
    {
        var savePath = path ?? ConfigPath;
        // Force all saves to LocalAppData unless an explicit path was provided
        if (string.IsNullOrEmpty(savePath))
        {
            savePath = LocalAppDataConfigPath;
            ConfigPath = savePath;
        }

        Logger.Info("ConfigService", $"Saving configuration to: {savePath}");

        try
        {
            var json = JsonSerializer.Serialize(Config, WriteOptions);
            Logger.Debug("ConfigService", $"Serialized config: {json.Length} bytes");

            // Ensure directory exists
            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Logger.Debug("ConfigService", $"Created directory: {dir}");
            }

            // Create backup of existing config before overwriting
            try
            {
                if (File.Exists(savePath))
                {
                    File.Copy(savePath, savePath + ".bak", true);
                    Logger.Debug("ConfigService", "Backup created before save");
                }
            }
            catch (Exception bakEx)
            {
                Logger.Debug("ConfigService", $"Backup before save skipped: {bakEx.Message}");
            }

            // Optionally encrypt with DPAPI (current-user scope)
            var payload = json;
            if (Config.EncryptConfig)
            {
                payload = EncryptedHeader + DpapiEncrypt(json);
                Logger.Debug("ConfigService", "Config encrypted with DPAPI");
            }

            if (!IsTestMode)
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RegistryConfigSubKey);
                    key?.SetValue(RegistryConfigValueName, payload, RegistryValueKind.String);
                    Logger.Debug("ConfigService", "Configuration persisted to registry");
                }
                catch (Exception regEx)
                {
                    Logger.Warning("ConfigService", $"Failed to save config to registry: {regEx.Message}");
                }
            }

            // Write to temp file first, then rename for atomic save
            var tempPath = savePath + ".tmp";
            File.WriteAllText(tempPath, payload);
            File.Move(tempPath, savePath, true);
            
            // Save a resilient backup to Roaming AppData so it survives MSI MajorUpgrades
            // that erroneously wipe out the LocalAppData UserData folder during old-version uninstall.
            try
            {
                var roamingDir = Path.GetDirectoryName(AppDataConfigPath);
                if (!string.IsNullOrEmpty(roamingDir) && !Directory.Exists(roamingDir))
                {
                    Directory.CreateDirectory(roamingDir);
                }
                var roamingTemp = AppDataConfigPath + ".tmp";
                File.WriteAllText(roamingTemp, payload);
                File.Move(roamingTemp, AppDataConfigPath, true);
            }
            catch (Exception roamEx)
            {
                Logger.Debug("ConfigService", $"Failed to save roaming backup: {roamEx.Message}");
            }
            
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
                Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                Config
            };

            var json = JsonSerializer.Serialize(backup, WriteOptions);
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

            // Try parsing as backup format first (has wrapper object)
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Config", out var configElement))
                {
                    var configJson = configElement.GetRawText();
                    Config = JsonSerializer.Deserialize<RedballConfig>(configJson, ReadOptions) ?? new RedballConfig();
                    SanitizeConfig();
                    NormalizeConfig();
                    IsDirty = true;
                    Logger.Info("ConfigService", "Config imported from backup format");
                    return true;
                }
            }
            catch { }

            // Fall back to plain config format
            Config = JsonSerializer.Deserialize<RedballConfig>(json, ReadOptions) ?? new RedballConfig();
            SanitizeConfig();
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

    /// <summary>
    /// Gets an ordered list of potential config file paths to try.
    /// </summary>
    private static List<string> GetConfigPathCandidates(string? explicitPath)
    {
        var candidates = new List<string>();

        // 1. Explicit path if provided
        if (!string.IsNullOrEmpty(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        // 2. Stable Canonical path (Primary)
        candidates.Add(LocalAppDataConfigPath);

        // 3. Stable Roaming Backup (Extra Safety)
        candidates.Add(AppDataConfigPath);

        // 4. Local Backup (.bak)
        candidates.Add(BackupConfigPath);

        // 5. Legacy Paths (Migration)
        candidates.Add(LegacyArMaTeCConfigPath);
        candidates.Add(LegacyLocalAppDataConfigPath);

        // NOTE: We intentionally do NOT include AppContext.BaseDirectory or
        // Environment.CurrentDirectory here. The Redball.json in the install
        // folder is a template/default file, not user configuration. Loading
        // it would overwrite the user's real settings with factory defaults.

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void EnsureUserDataDir()
    {
        if (!Directory.Exists(UserDataDir))
        {
            Directory.CreateDirectory(UserDataDir);
            Logger.Debug("ConfigService", $"Created directory: {UserDataDir}");
        }
    }

    /// <summary>
    /// Self-healing: ensures every config property has a valid, non-corrupt value.
    /// Compares against a defaults instance and fixes any zero/null/out-of-range values
    /// that shouldn't be zero/null. This runs after every load so missing properties
    /// (e.g. from an older config file) get filled in automatically.
    /// </summary>
    private void SanitizeConfig()
    {
        var d = new RedballConfig(); // fresh defaults
        var healed = 0;

        // Numeric ranges — clamp or reset to default
        if (Config.HeartbeatSeconds < 10 || Config.HeartbeatSeconds > 300)
        { Config.HeartbeatSeconds = d.HeartbeatSeconds; healed++; }

        if (Config.DefaultDuration < 1 || Config.DefaultDuration > 720)
        { Config.DefaultDuration = d.DefaultDuration; healed++; }

        if (Config.BatteryThreshold < 5 || Config.BatteryThreshold > 95)
        { Config.BatteryThreshold = d.BatteryThreshold; healed++; }

        if (Config.MaxLogSizeMB < 1 || Config.MaxLogSizeMB > 100)
        { Config.MaxLogSizeMB = d.MaxLogSizeMB; healed++; }

        if (Config.IdleThreshold < 1 || Config.IdleThreshold > 600)
        { Config.IdleThreshold = d.IdleThreshold; healed++; }

        // TypeThing settings
        if (Config.TypeThingMinDelayMs < 1)
        { Config.TypeThingMinDelayMs = d.TypeThingMinDelayMs; healed++; }

        if (Config.TypeThingMaxDelayMs < Config.TypeThingMinDelayMs)
        { Config.TypeThingMaxDelayMs = Math.Max(d.TypeThingMaxDelayMs, Config.TypeThingMinDelayMs + 1); healed++; }

        if (Config.TypeThingStartDelaySec < 0 || Config.TypeThingStartDelaySec > 30)
        { Config.TypeThingStartDelaySec = d.TypeThingStartDelaySec; healed++; }

        if (Config.TypeThingRandomPauseChance < 0 || Config.TypeThingRandomPauseChance > 100)
        { Config.TypeThingRandomPauseChance = d.TypeThingRandomPauseChance; healed++; }

        if (Config.TypeThingRandomPauseMaxMs < 0)
        { Config.TypeThingRandomPauseMaxMs = d.TypeThingRandomPauseMaxMs; healed++; }

        // Auto-update settings
        if (Config.AutoUpdateCheckIntervalMinutes < 30 || Config.AutoUpdateCheckIntervalMinutes > 1440)
        { Config.AutoUpdateCheckIntervalMinutes = d.AutoUpdateCheckIntervalMinutes; healed++; }

        // Pomodoro settings
        if (Config.PomodoroFocusMinutes < 1 || Config.PomodoroFocusMinutes > 120)
        { Config.PomodoroFocusMinutes = d.PomodoroFocusMinutes; healed++; }

        if (Config.PomodoroBreakMinutes < 1 || Config.PomodoroBreakMinutes > 60)
        { Config.PomodoroBreakMinutes = d.PomodoroBreakMinutes; healed++; }

        if (Config.PomodoroLongBreakMinutes < 1 || Config.PomodoroLongBreakMinutes > 120)
        { Config.PomodoroLongBreakMinutes = d.PomodoroLongBreakMinutes; healed++; }

        if (Config.PomodoroLongBreakInterval < 1 || Config.PomodoroLongBreakInterval > 20)
        { Config.PomodoroLongBreakInterval = d.PomodoroLongBreakInterval; healed++; }

        // Thermal
        if (Config.ThermalThreshold < 50 || Config.ThermalThreshold > 105)
        { Config.ThermalThreshold = d.ThermalThreshold; healed++; }

        // Restart reminder
        if (Config.RestartReminderDays < 1 || Config.RestartReminderDays > 90)
        { Config.RestartReminderDays = d.RestartReminderDays; healed++; }

        // Web API port
        if (Config.WebApiPort < 1024 || Config.WebApiPort > 65535)
        { Config.WebApiPort = d.WebApiPort; healed++; }

        // String properties — fill nulls/empties with defaults
        if (string.IsNullOrWhiteSpace(Config.LogPath))
        { Config.LogPath = d.LogPath; healed++; }

        if (string.IsNullOrWhiteSpace(Config.Locale))
        { Config.Locale = d.Locale; healed++; }

        if (string.IsNullOrWhiteSpace(Config.ScheduleStartTime))
        { Config.ScheduleStartTime = d.ScheduleStartTime; healed++; }

        if (string.IsNullOrWhiteSpace(Config.ScheduleStopTime))
        { Config.ScheduleStopTime = d.ScheduleStopTime; healed++; }

        if (string.IsNullOrWhiteSpace(Config.UpdateChannel))
        { Config.UpdateChannel = d.UpdateChannel; healed++; }

        if (string.IsNullOrWhiteSpace(Config.TypeThingInputMode))
        { Config.TypeThingInputMode = d.TypeThingInputMode; healed++; }

        if (string.IsNullOrWhiteSpace(Config.Theme))
        { Config.Theme = d.Theme; healed++; }

        if (Config.ScheduleDays == null || Config.ScheduleDays.Count == 0)
        { Config.ScheduleDays = d.ScheduleDays; healed++; }

        // Null-safe string properties that are allowed to be empty but not null
        Config.TypeThingStartHotkey ??= d.TypeThingStartHotkey;
        Config.TypeThingStopHotkey ??= d.TypeThingStopHotkey;
        Config.TypeThingTheme ??= d.TypeThingTheme;
        Config.ProcessWatcherTarget ??= d.ProcessWatcherTarget;
        Config.KeepAwakeApps ??= d.KeepAwakeApps;
        Config.PauseApps ??= d.PauseApps;
        Config.WifiProfileMappings ??= d.WifiProfileMappings;

        if (healed > 0)
        {
            Logger.Info("ConfigService", $"SanitizeConfig: healed {healed} invalid/missing values");
            IsDirty = true;
        }
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

    /// <summary>
    /// Encrypts a plaintext string using DPAPI (CurrentUser scope).
    /// Returns a Base64-encoded ciphertext.
    /// </summary>
    private static string DpapiEncrypt(string plaintext)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(plaintextBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypts a Base64-encoded DPAPI ciphertext back to plaintext.
    /// Throws CryptographicException if decryption fails (wrong user, corrupt data).
    /// </summary>
    private static string DpapiDecrypt(string base64Ciphertext)
    {
        var encrypted = Convert.FromBase64String(base64Ciphertext);
        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}
