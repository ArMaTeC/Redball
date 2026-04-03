using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Redball.UI.Services;

/// <summary>
/// Manages loading and executing Redball plugins from DLLs in the Plugins folder.
/// Plugins must implement IRedballPlugin and be placed in %LocalAppData%\Redball\Plugins\.
/// </summary>
public class PluginService
{
    private static readonly Lazy<PluginService> _instance = new(() => new PluginService());
    public static PluginService Instance => _instance.Value;

    private readonly List<IRedballPlugin> _plugins = new();
    private readonly string _pluginsDir;

    public IReadOnlyList<IRedballPlugin> LoadedPlugins => _plugins;

    private PluginService()
    {
        _pluginsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball", "Plugins");
        if (!Directory.Exists(_pluginsDir))
            Directory.CreateDirectory(_pluginsDir);

        Logger.Verbose("PluginService", $"Plugins directory: {_pluginsDir}");
    }

    /// <summary>
    /// Scans the plugins directory for DLLs containing IRedballPlugin implementations and loads them.
    /// </summary>
    public void LoadPlugins()
    {
        _plugins.Clear();

        try
        {
            var dlls = Directory.GetFiles(_pluginsDir, "*.dll");
            Logger.Info("PluginService", $"Found {dlls.Length} plugin DLL(s)");

            foreach (var dll in dlls)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    var pluginTypes = assembly.GetTypes()
                        .Where(t => typeof(IRedballPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                    foreach (var type in pluginTypes)
                    {
                        var plugin = (IRedballPlugin)Activator.CreateInstance(type)!;
                        plugin.OnLoad();
                        _plugins.Add(plugin);
                        Logger.Info("PluginService", $"Loaded plugin: {plugin.Name} — {plugin.Description}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("PluginService", $"Failed to load plugin from {Path.GetFileName(dll)}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("PluginService", "Failed to scan plugins directory", ex);
        }
    }

    public void NotifyActivate()
    {
        foreach (var plugin in _plugins)
        {
            try { plugin.OnActivate(); }
            catch (Exception ex) { Logger.Error("PluginService", $"Plugin {plugin.Name} OnActivate failed", ex); }
        }
    }

    public void NotifyPause()
    {
        foreach (var plugin in _plugins)
        {
            try { plugin.OnPause(); }
            catch (Exception ex) { Logger.Error("PluginService", $"Plugin {plugin.Name} OnPause failed", ex); }
        }
    }

    public void NotifyTimerExpire()
    {
        foreach (var plugin in _plugins)
        {
            try { plugin.OnTimerExpire(); }
            catch (Exception ex) { Logger.Error("PluginService", $"Plugin {plugin.Name} OnTimerExpire failed", ex); }
        }
    }

    public void UnloadAll()
    {
        foreach (var plugin in _plugins)
        {
            try { plugin.OnUnload(); }
            catch (Exception ex) { Logger.Error("PluginService", $"Plugin {plugin.Name} OnUnload failed", ex); }
        }
        _plugins.Clear();
        Logger.Info("PluginService", "All plugins unloaded");
    }

    public string GetStatusText()
    {
        if (_plugins.Count == 0) return "No plugins loaded";
        return string.Join(", ", _plugins.Select(p => p.Name));
    }
}
