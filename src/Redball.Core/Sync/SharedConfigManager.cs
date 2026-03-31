using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Redball.Core.Sync;

/// <summary>
/// Cross-platform configuration manager that provides a shared configuration format
/// across Windows, macOS, and Linux implementations of Redball.
/// </summary>
public class SharedConfigManager
{
    private static readonly Lazy<SharedConfigManager> _instance = new(() => new SharedConfigManager());
    public static SharedConfigManager Instance => _instance.Value;

    private readonly string _configDirectory;
    private readonly string _sharedConfigPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public event EventHandler<SharedConfigChangedEventArgs>? ConfigChanged;

    private SharedConfigManager()
    {
        // Use a cross-platform location for shared config
        _configDirectory = GetCrossPlatformConfigDirectory();
        _sharedConfigPath = Path.Combine(_configDirectory, "redball-shared.json");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        EnsureConfigDirectoryExists();
        
        Logger.Debug("SharedConfigManager", $"Initialized at: {_sharedConfigPath}");
    }

    /// <summary>
    /// Gets the cross-platform config directory path.
    /// </summary>
    private string GetCrossPlatformConfigDirectory()
    {
        // Use platform-appropriate config directories
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        
        // On macOS/Linux, this typically maps to ~/.config
        // On Windows, it's %APPDATA%
        var configDir = Path.Combine(appData, "Redball");
        
        // For macOS, prefer ~/Library/Application Support/Redball
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configDir = Path.Combine(home, "Library", "Application Support", "Redball");
        }
        // For Linux, use ~/.config/redball
        else if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(xdgConfig))
            {
                configDir = Path.Combine(xdgConfig, "redball");
            }
            else
            {
                configDir = Path.Combine(home, ".config", "redball");
            }
        }

        return configDir;
    }

    /// <summary>
    /// Loads the shared configuration from disk.
    /// </summary>
    public SharedConfig LoadSharedConfig()
    {
        try
        {
            if (!File.Exists(_sharedConfigPath))
            {
                Logger.Info("SharedConfigManager", "No shared config found, creating default");
                var defaultConfig = CreateDefaultConfig();
                SaveSharedConfig(defaultConfig);
                return defaultConfig;
            }

            var json = File.ReadAllText(_sharedConfigPath);
            var config = JsonSerializer.Deserialize<SharedConfig>(json, _jsonOptions);
            
            if (config == null)
            {
                Logger.Warning("SharedConfigManager", "Failed to deserialize config, using defaults");
                return CreateDefaultConfig();
            }

            // Validate and repair if needed
            config = ValidateAndRepair(config);
            
            Logger.Debug("SharedConfigManager", "Shared config loaded successfully");
            return config;
        }
        catch (Exception ex)
        {
            Logger.Error("SharedConfigManager", "Error loading shared config", ex);
            return CreateDefaultConfig();
        }
    }

    /// <summary>
    /// Saves the shared configuration to disk.
    /// </summary>
    public async Task SaveSharedConfigAsync(SharedConfig config)
    {
        try
        {
            config.LastModified = DateTime.UtcNow;
            config.Version = GetCurrentConfigVersion();
            
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            
            // Write atomically
            var tempPath = _sharedConfigPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            
            if (File.Exists(_sharedConfigPath))
            {
                File.Replace(tempPath, _sharedConfigPath, null);
            }
            else
            {
                File.Move(tempPath, _sharedConfigPath);
            }

            ConfigChanged?.Invoke(this, new SharedConfigChangedEventArgs
            {
                Config = config,
                ChangedAt = DateTime.UtcNow
            });

            Logger.Debug("SharedConfigManager", "Shared config saved");
        }
        catch (Exception ex)
        {
            Logger.Error("SharedConfigManager", "Error saving shared config", ex);
            throw;
        }
    }

    /// <summary>
    /// Synchronizes local platform-specific config with shared config.
    /// </summary>
    public async Task<SyncResult> SyncWithLocalConfigAsync(RedballConfig localConfig)
    {
        var sharedConfig = LoadSharedConfig();
        var result = new SyncResult();

        try
        {
            // Determine which config is newer
            if (sharedConfig.LastModified > localConfig.LastConfigLoad)
            {
                // Shared config is newer, import to local
                ImportSharedToLocal(sharedConfig, localConfig);
                result.Direction = SyncDirection.SharedToLocal;
                result.Changes = GetConfigDifferences(sharedConfig, localConfig);
            }
            else if (localConfig.LastConfigLoad > sharedConfig.LastModified)
            {
                // Local config is newer, export to shared
                ExportLocalToShared(localConfig, sharedConfig);
                await SaveSharedConfigAsync(sharedConfig);
                result.Direction = SyncDirection.LocalToShared;
                result.Changes = GetConfigDifferences(sharedConfig, localConfig);
            }
            else
            {
                // Configs are in sync
                result.Direction = SyncDirection.AlreadyInSync;
            }

            result.Success = true;
            result.Timestamp = DateTime.UtcNow;

            Logger.Info("SharedConfigManager", $"Config sync completed: {result.Direction}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            Logger.Error("SharedConfigManager", "Config sync failed", ex);
        }

        return result;
    }

    /// <summary>
    /// Exports configuration for transfer to another device/platform.
    /// </summary>
    public async Task<string> ExportForTransferAsync()
    {
        var config = LoadSharedConfig();
        
        // Create export package with metadata
        var exportPackage = new ConfigExportPackage
        {
            ExportedAt = DateTime.UtcNow,
            SourcePlatform = GetCurrentPlatform(),
            SourceVersion = GetCurrentVersion(),
            Config = config
        };

        return JsonSerializer.Serialize(exportPackage, _jsonOptions);
    }

    /// <summary>
    /// Imports configuration from another device/platform.
    /// </summary>
    public async Task<ImportResult> ImportFromTransferAsync(string json)
    {
        try
        {
            var package = JsonSerializer.Deserialize<ConfigExportPackage>(json, _jsonOptions);
            
            if (package == null)
            {
                return new ImportResult { Success = false, Error = "Invalid import data" };
            }

            // Validate version compatibility
            if (!IsVersionCompatible(package.SourceVersion))
            {
                return new ImportResult 
                { 
                    Success = false, 
                    Error = $"Incompatible version: {package.SourceVersion}" 
                };
            }

            // Apply platform-specific adjustments
            var config = AdaptConfigForPlatform(package.Config, package.SourcePlatform);
            
            await SaveSharedConfigAsync(config);
            
            // Sync to local config - note: ConfigService is in Redball.UI.WPF
            // This should be called from the UI layer after import
            // ImportSharedToLocal(config, ConfigService.Instance.Config);

            return new ImportResult 
            { 
                Success = true, 
                SourcePlatform = package.SourcePlatform,
                ImportedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new ImportResult { Success = false, Error = ex.Message };
        }
    }

    private void EnsureConfigDirectoryExists()
    {
        if (!Directory.Exists(_configDirectory))
        {
            Directory.CreateDirectory(_configDirectory);
        }
    }

    private SharedConfig CreateDefaultConfig()
    {
        return new SharedConfig
        {
            Version = GetCurrentConfigVersion(),
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow,
            
            // Universal settings (work across all platforms)
            UniversalSettings = new UniversalSettings
            {
                DefaultDuration = 60,
                BatteryAware = true,
                BatteryThreshold = 20,
                NetworkAware = true,
                MeetingAware = true,
                IdleDetection = true,
                Theme = "System",
                Notifications = true,
                SoundNotifications = false,
                MinimizeToTray = true,
                ConfirmOnExit = true
            },

            // Platform-specific overrides (null = use universal)
            PlatformOverrides = new PlatformSpecificSettings()
        };
    }

    private SharedConfig ValidateAndRepair(SharedConfig config)
    {
        if (config.UniversalSettings == null)
        {
            config.UniversalSettings = new UniversalSettings();
        }

        // Ensure reasonable defaults
        if (config.UniversalSettings.DefaultDuration < 1)
            config.UniversalSettings.DefaultDuration = 60;
        
        if (config.UniversalSettings.BatteryThreshold < 5 || config.UniversalSettings.BatteryThreshold > 95)
            config.UniversalSettings.BatteryThreshold = 20;

        return config;
    }

    private void ImportSharedToLocal(SharedConfig shared, RedballConfig local)
    {
        // Copy universal settings to local config
        local.DefaultDuration = shared.UniversalSettings.DefaultDuration;
        local.BatteryAware = shared.UniversalSettings.BatteryAware;
        local.BatteryThreshold = shared.UniversalSettings.BatteryThreshold;
        local.NetworkAware = shared.UniversalSettings.NetworkAware;
        local.MeetingAware = shared.UniversalSettings.MeetingAware;
        local.IdleDetection = shared.UniversalSettings.IdleDetection;
        local.Theme = shared.UniversalSettings.Theme;
        local.ShowNotifications = shared.UniversalSettings.Notifications;
        local.SoundNotifications = shared.UniversalSettings.SoundNotifications;
        local.MinimizeToTray = shared.UniversalSettings.MinimizeToTray;
        local.ConfirmOnExit = shared.UniversalSettings.ConfirmOnExit;

        // Apply platform-specific overrides if present
        var platform = GetCurrentPlatform();
        var overrides = platform switch
        {
            "macos" => shared.PlatformOverrides?.MacOS as object,
            "linux" => shared.PlatformOverrides?.Linux as object,
            _ => shared.PlatformOverrides?.Windows as object
        };

        if (overrides != null)
        {
            // Apply overrides
        }
    }

    private void ExportLocalToShared(RedballConfig local, SharedConfig shared)
    {
        // Copy local settings to universal config
        shared.UniversalSettings.DefaultDuration = local.DefaultDuration;
        shared.UniversalSettings.BatteryAware = local.BatteryAware;
        shared.UniversalSettings.BatteryThreshold = local.BatteryThreshold;
        shared.UniversalSettings.NetworkAware = local.NetworkAware;
        shared.UniversalSettings.MeetingAware = local.MeetingAware;
        shared.UniversalSettings.IdleDetection = local.IdleDetection;
        shared.UniversalSettings.Theme = local.Theme;
        shared.UniversalSettings.Notifications = local.ShowNotifications;
        shared.UniversalSettings.SoundNotifications = local.SoundNotifications;
        shared.UniversalSettings.MinimizeToTray = local.MinimizeToTray;
        shared.UniversalSettings.ConfirmOnExit = local.ConfirmOnExit;
    }

    private List<ConfigChange> GetConfigDifferences(SharedConfig shared, RedballConfig local)
    {
        var changes = new List<ConfigChange>();

        if (shared.UniversalSettings.DefaultDuration != local.DefaultDuration)
            changes.Add(new ConfigChange { Property = "DefaultDuration", SharedValue = shared.UniversalSettings.DefaultDuration, LocalValue = local.DefaultDuration });

        if (shared.UniversalSettings.BatteryAware != local.BatteryAware)
            changes.Add(new ConfigChange { Property = "BatteryAware", SharedValue = shared.UniversalSettings.BatteryAware, LocalValue = local.BatteryAware });

        // Add more property comparisons as needed

        return changes;
    }

    private SharedConfig AdaptConfigForPlatform(SharedConfig config, string sourcePlatform)
    {
        var adapted = config;
        
        // Platform-specific adaptations
        if (sourcePlatform == "windows" && GetCurrentPlatform() == "macos")
        {
            // Adapt Windows-specific settings to macOS equivalents
        }
        
        return adapted;
    }

    private string GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return "unknown";
    }

    private string GetCurrentVersion()
    {
        return typeof(SharedConfigManager).Assembly.GetName().Version?.ToString(3) ?? "3.0.0";
    }

    private int GetCurrentConfigVersion() => 1;

    private bool IsVersionCompatible(string version)
    {
        try
        {
            var current = Version.Parse(GetCurrentVersion());
            var source = Version.Parse(version);
            return current.Major == source.Major; // Same major version
        }
        catch
        {
            return false;
        }
    }

    public void SaveSharedConfig(SharedConfig config)
    {
        _ = SaveSharedConfigAsync(config);
    }
}

/// <summary>
/// Shared configuration format that works across all platforms.
/// </summary>
public class SharedConfig
{
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    
    public UniversalSettings UniversalSettings { get; set; } = new();
    public PlatformSpecificSettings PlatformOverrides { get; set; } = new();
}

public class UniversalSettings
{
    // Core keep-awake settings
    public int DefaultDuration { get; set; } = 60;
    public bool BatteryAware { get; set; } = true;
    public int BatteryThreshold { get; set; } = 20;
    public bool NetworkAware { get; set; } = true;
    public bool MeetingAware { get; set; } = true;
    public bool IdleDetection { get; set; } = true;
    
    // UI settings
    public string Theme { get; set; } = "System";
    public bool Notifications { get; set; } = true;
    public bool SoundNotifications { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool ConfirmOnExit { get; set; } = true;
    
    // Feature flags
    public bool EnableSmartSchedule { get; set; } = false;
    public bool EnableAdvancedAnalytics { get; set; } = false;
}

public class PlatformSpecificSettings
{
    public WindowsSettings? Windows { get; set; }
    public MacOSSettings? MacOS { get; set; }
    public LinuxSettings? Linux { get; set; }
}

public class WindowsSettings
{
    public bool UseHIDMode { get; set; } = false;
    public string? HIDDriverPath { get; set; }
    public bool FocusAssistIntegration { get; set; } = false;
}

public class MacOSSettings
{
    public bool UseCGEventMode { get; set; } = true;
    public bool MenuBarIconOnly { get; set; } = false;
}

public class LinuxSettings
{
    public string? PreferredBackend { get; set; } // x11, wayland, systemd
    public bool UseAppIndicator { get; set; } = true;
}

public class ConfigExportPackage
{
    public DateTime ExportedAt { get; set; }
    public string SourcePlatform { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public SharedConfig Config { get; set; } = new();
}

public class SyncResult
{
    public bool Success { get; set; }
    public SyncDirection Direction { get; set; }
    public List<ConfigChange> Changes { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public string? Error { get; set; }
}

public class ImportResult
{
    public bool Success { get; set; }
    public string? SourcePlatform { get; set; }
    public DateTime ImportedAt { get; set; }
    public string? Error { get; set; }
}

public class ConfigChange
{
    public string Property { get; set; } = string.Empty;
    public object? SharedValue { get; set; }
    public object? LocalValue { get; set; }
}

public enum SyncDirection
{
    SharedToLocal,
    LocalToShared,
    AlreadyInSync,
    Conflict
}

public class SharedConfigChangedEventArgs : EventArgs
{
    public SharedConfig Config { get; set; } = new();
    public DateTime ChangedAt { get; set; }
}

/// <summary>
/// Placeholder RedballConfig class for Redball.Core compatibility.
/// Note: Full implementation is in Redball.UI.WPF
/// </summary>
public class RedballConfig
{
    public int DefaultDuration { get; set; }
    public bool BatteryAware { get; set; }
    public int BatteryThreshold { get; set; }
    public bool NetworkAware { get; set; }
    public bool MeetingAware { get; set; }
    public bool IdleDetection { get; set; }
    public string Theme { get; set; } = "System";
    public bool ShowNotifications { get; set; }
    public bool SoundNotifications { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool ConfirmOnExit { get; set; }
    public bool AutoExitOnComplete { get; set; }
    public DateTime LastConfigLoad { get; set; }
}
