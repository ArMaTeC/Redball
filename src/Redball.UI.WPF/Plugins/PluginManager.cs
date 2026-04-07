using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Redball.UI.Services;

namespace Redball.UI.Plugins;

/// <summary>
/// Manages plugin discovery, loading, lifecycle, and coordination.
/// Provides centralized plugin management for the Redball application.
/// </summary>
public class PluginManager : IDisposable
{
    private static readonly Lazy<PluginManager> _instance = new(() => new PluginManager());
    public static PluginManager Instance => _instance.Value;

    private readonly string _pluginDirectory;
    private readonly Dictionary<string, LoadedPlugin> _loadedPlugins;
    private readonly PluginContext _context;
    private readonly PluginEventAggregator _eventAggregator;
    private bool _isInitialized;

    public event EventHandler<PluginLoadedEventArgs>? PluginLoaded;
    public event EventHandler<PluginUnloadedEventArgs>? PluginUnloaded;
    public event EventHandler<PluginErrorEventArgs>? PluginError;

    public IReadOnlyCollection<LoadedPlugin> LoadedPlugins => _loadedPlugins.Values.ToList().AsReadOnly();
    public IEnumerable<LoadedPlugin> ActivePlugins => _loadedPlugins.Values.Where(p => p.IsActive);

    private PluginManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _pluginDirectory = Path.Combine(appData, "Redball", "Plugins");
        _loadedPlugins = new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);
        _eventAggregator = new PluginEventAggregator();
        _context = new PluginContext(_eventAggregator);
        
        EnsurePluginDirectoryExists();
    }

    /// <summary>
    /// Initializes the plugin manager and discovers available plugins.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        Logger.Info("PluginManager", "Initializing plugin manager...");

        try
        {
            // Discover plugins in plugin directory
            await DiscoverPluginsAsync();
            
            _isInitialized = true;
            Logger.Info("PluginManager", $"Plugin manager initialized. Found {_loadedPlugins.Count} plugins.");
        }
        catch (Exception ex)
        {
            Logger.Error("PluginManager", "Failed to initialize plugin manager", ex);
            throw;
        }
    }

    /// <summary>
    /// Discovers and loads all plugins in the plugin directory.
    /// </summary>
    public async Task DiscoverPluginsAsync()
    {
        if (!Directory.Exists(_pluginDirectory))
        {
            Logger.Warning("PluginManager", $"Plugin directory does not exist: {_pluginDirectory}");
            return;
        }

        // Find all DLL files in plugin directory
        var pluginFiles = Directory.GetFiles(_pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        
        foreach (var file in pluginFiles)
        {
            try
            {
                await LoadPluginFromFileAsync(file);
            }
            catch (Exception ex)
            {
                Logger.Warning("PluginManager", $"Failed to load plugin from {file}: {ex.Message}");
                PluginError?.Invoke(this, new PluginErrorEventArgs
                {
                    PluginFile = file,
                    Error = ex,
                    Operation = PluginOperation.Load
                });
            }
        }

        // Also look in subdirectories for organized plugins
        var subdirectories = Directory.GetDirectories(_pluginDirectory);
        foreach (var dir in subdirectories)
        {
            var dllFiles = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
            foreach (var file in dllFiles)
            {
                try
                {
                    await LoadPluginFromFileAsync(file);
                }
                catch (Exception ex)
                {
                    Logger.Warning("PluginManager", $"Failed to load plugin from {file}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Loads a plugin from a DLL file.
    /// </summary>
    public async Task<LoadedPlugin?> LoadPluginFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Plugin file not found", filePath);

        // Skip if already loaded
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (_loadedPlugins.ContainsKey(fileName))
        {
            Logger.Debug("PluginManager", $"Plugin {fileName} already loaded");
            return _loadedPlugins[fileName];
        }

        try
        {
            // Load the assembly
            var assembly = Assembly.LoadFrom(filePath);
            
            // Find types implementing IRedballPlugin
            var pluginTypes = assembly.GetTypes()
                .Where(t => typeof(IRedballPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();

            if (!pluginTypes.Any())
            {
                Logger.Debug("PluginManager", $"No plugin implementations found in {filePath}");
                return null;
            }

            var loadedPlugin = new LoadedPlugin
            {
                FilePath = filePath,
                Assembly = assembly,
                LoadTime = DateTime.UtcNow
            };

            // Instantiate and initialize each plugin type
            foreach (var pluginType in pluginTypes)
            {
                try
                {
                    var instance = (IRedballPlugin?)Activator.CreateInstance(pluginType);
                    if (instance == null) continue;

                    // Validate minimum version requirement
                    if (!IsVersionCompatible(instance.MinRedballVersion))
                    {
                        Logger.Warning("PluginManager", 
                            $"Plugin {instance.Name} requires Redball {instance.MinRedballVersion}, " +
                            $"but current version is {GetCurrentRedballVersion()}");
                        continue;
                    }

                    // Initialize the plugin
                    await instance.InitializeAsync(_context);
                    
                    loadedPlugin.Instances.Add(instance);
                    
                    Logger.Info("PluginManager", $"Loaded plugin: {instance.Name} v{instance.Version} by {instance.Author}");
                }
                catch (Exception ex)
                {
                    Logger.Error("PluginManager", $"Failed to instantiate plugin type {pluginType.Name}", ex);
                }
            }

            if (loadedPlugin.Instances.Any())
            {
                _loadedPlugins[fileName] = loadedPlugin;
                
                PluginLoaded?.Invoke(this, new PluginLoadedEventArgs
                {
                    Plugin = loadedPlugin,
                    FilePath = filePath
                });

                return loadedPlugin;
            }

            return null;
        }
        catch (ReflectionTypeLoadException ex)
        {
            Logger.Error("PluginManager", $"Failed to load plugin assembly {filePath}", ex);
            throw new PluginLoadException($"Failed to load plugin: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Unloads a plugin by its ID.
    /// </summary>
    public async Task<bool> UnloadPluginAsync(string pluginId)
    {
        var plugin = _loadedPlugins.Values
            .FirstOrDefault(p => p.Instances.Any(i => i.PluginId == pluginId));

        if (plugin == null)
            return false;

        try
        {
            // Deactivate and shutdown all instances
            foreach (var instance in plugin.Instances)
            {
                if (plugin.IsActive)
                {
                    await instance.DeactivateAsync();
                }
                await instance.ShutdownAsync();
            }

            var fileName = Path.GetFileNameWithoutExtension(plugin.FilePath);
            _loadedPlugins.Remove(fileName);

            PluginUnloaded?.Invoke(this, new PluginUnloadedEventArgs
            {
                PluginId = pluginId,
                FilePath = plugin.FilePath
            });

            Logger.Info("PluginManager", $"Unloaded plugin: {pluginId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("PluginManager", $"Failed to unload plugin {pluginId}", ex);
            return false;
        }
    }

    /// <summary>
    /// Activates a plugin by its ID.
    /// </summary>
    public async Task<bool> ActivatePluginAsync(string pluginId)
    {
        var plugin = _loadedPlugins.Values
            .FirstOrDefault(p => p.Instances.Any(i => i.PluginId == pluginId));

        if (plugin == null || plugin.IsActive)
            return false;

        try
        {
            foreach (var instance in plugin.Instances)
            {
                await instance.ActivateAsync();
            }
            
            plugin.IsActive = true;
            Logger.Info("PluginManager", $"Activated plugin: {pluginId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("PluginManager", $"Failed to activate plugin {pluginId}", ex);
            return false;
        }
    }

    /// <summary>
    /// Deactivates a plugin by its ID.
    /// </summary>
    public async Task<bool> DeactivatePluginAsync(string pluginId)
    {
        var plugin = _loadedPlugins.Values
            .FirstOrDefault(p => p.Instances.Any(i => i.PluginId == pluginId));

        if (plugin == null || !plugin.IsActive)
            return false;

        try
        {
            foreach (var instance in plugin.Instances)
            {
                await instance.DeactivateAsync();
            }
            
            plugin.IsActive = false;
            Logger.Info("PluginManager", $"Deactivated plugin: {pluginId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("PluginManager", $"Failed to deactivate plugin {pluginId}", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets a loaded plugin by its ID.
    /// </summary>
    public IRedballPlugin? GetPlugin(string pluginId)
    {
        return _loadedPlugins.Values
            .SelectMany(p => p.Instances)
            .FirstOrDefault(i => i.PluginId == pluginId);
    }

    /// <summary>
    /// Gets all plugins of a specific type.
    /// </summary>
    public IEnumerable<T> GetPluginsOfType<T>() where T : class, IRedballPlugin
    {
        return _loadedPlugins.Values
            .SelectMany(p => p.Instances)
            .OfType<T>();
    }

    private void EnsurePluginDirectoryExists()
    {
        if (!Directory.Exists(_pluginDirectory))
        {
            Directory.CreateDirectory(_pluginDirectory);
            Logger.Info("PluginManager", $"Created plugin directory: {_pluginDirectory}");
        }
    }

    private bool IsVersionCompatible(string minVersion)
    {
        try
        {
            var current = Version.Parse(GetCurrentRedballVersion());
            var required = Version.Parse(minVersion);
            return current >= required;
        }
        catch (Exception ex)
        {
            Logger.Debug("PluginManager", $"Version compatibility check failed: {ex.Message}");
            return false;
        }
    }

    private string GetCurrentRedballVersion()
    {
        // Get version from assembly
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString(3) ?? "3.0.0";
    }

    public void Dispose()
    {
        // Unload all plugins
        foreach (var plugin in _loadedPlugins.Values.ToList())
        {
            foreach (var instance in plugin.Instances)
            {
                try
                {
                    instance.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    Logger.Error("PluginManager", $"Error during plugin shutdown: {instance.PluginId}", ex);
                }
            }
        }
        
        _loadedPlugins.Clear();
        _isInitialized = false;
    }
}

/// <summary>
/// Represents a loaded plugin with its instances and metadata.
/// </summary>
public class LoadedPlugin
{
    public string FilePath { get; set; } = string.Empty;
    public Assembly Assembly { get; set; } = null!;
    public List<IRedballPlugin> Instances { get; set; } = new();
    public DateTime LoadTime { get; set; }
    public bool IsActive { get; set; }

    public string? PrimaryPluginId => Instances.FirstOrDefault()?.PluginId;
    public string? Name => Instances.FirstOrDefault()?.Name;
    public string? Version => Instances.FirstOrDefault()?.Version;
}

/// <summary>
/// Custom exception for plugin loading errors.
/// </summary>
public class PluginLoadException : Exception
{
    public PluginLoadException(string message) : base(message) { }
    public PluginLoadException(string message, Exception inner) : base(message, inner) { }
}

// Event args
public class PluginLoadedEventArgs : EventArgs
{
    public LoadedPlugin Plugin { get; set; } = null!;
    public string FilePath { get; set; } = string.Empty;
}

public class PluginUnloadedEventArgs : EventArgs
{
    public string PluginId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public class PluginErrorEventArgs : EventArgs
{
    public string? PluginFile { get; set; }
    public string? PluginId { get; set; }
    public Exception Error { get; set; } = null!;
    public PluginOperation Operation { get; set; }
}

public enum PluginOperation
{
    Load,
    Unload,
    Activate,
    Deactivate,
    Execute
}
