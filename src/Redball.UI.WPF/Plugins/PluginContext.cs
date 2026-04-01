using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Redball.UI.Services;

namespace Redball.UI.Plugins;

/// <summary>
/// Implementation of IPluginContext providing access to Redball core services.
/// </summary>
internal class PluginContext : IPluginContext
{
    private readonly PluginEventAggregator _eventAggregator;

    public IPluginLogger Logger { get; }
    public IPluginConfigAccessor Config { get; }
    public IPluginServiceProvider Services { get; }
    public IPluginEventAggregator Events => _eventAggregator;
    public IPluginStorage Storage { get; }
    public IPluginUIFactory UI { get; }

    public PluginContext(PluginEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        
        Logger = new PluginLogger();
        Config = new PluginConfigAccessor();
        Services = new PluginServiceProvider();
        Storage = new PluginStorage();
        UI = new PluginUIFactory();
    }
}

/// <summary>
/// Logger implementation for plugins.
/// </summary>
internal class PluginLogger : IPluginLogger
{
    public void Debug(string message)
    {
        Logger.Debug("Plugin", message);
    }

    public void Info(string message)
    {
        Logger.Info("Plugin", message);
    }

    public void Warning(string message)
    {
        Logger.Warning("Plugin", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        Logger.Error("Plugin", message, exception);
    }
}

/// <summary>
/// Read-only config accessor implementation.
/// </summary>
internal class PluginConfigAccessor : IPluginConfigAccessor
{
    public T? GetValue<T>(string key)
    {
        // Access config through reflection or internal API
        // For now, return default values
        return default;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        return GetValue<bool?>(key) ?? defaultValue;
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        return GetValue<int?>(key) ?? defaultValue;
    }

    public string GetString(string key, string defaultValue = "")
    {
        return GetValue<string>(key) ?? defaultValue;
    }
}

/// <summary>
/// Service provider implementation exposing Redball services to plugins.
/// </summary>
internal class PluginServiceProvider : IPluginServiceProvider
{
    public IKeepAwakePluginService? KeepAwake { get; private set; }
    public INotificationPluginService? Notifications { get; private set; }
    public ISchedulerPluginService? Scheduler { get; private set; }

    public PluginServiceProvider()
    {
        KeepAwake = new KeepAwakePluginServiceAdapter();
        Notifications = new NotificationPluginServiceAdapter();
        Scheduler = new SchedulerPluginServiceAdapter();
    }

    public T? GetService<T>() where T : class
    {
        if (typeof(T) == typeof(IKeepAwakePluginService))
            return KeepAwake as T;
        if (typeof(T) == typeof(INotificationPluginService))
            return Notifications as T;
        if (typeof(T) == typeof(ISchedulerPluginService))
            return Scheduler as T;
        
        return null;
    }
}

/// <summary>
/// Adapter wrapping KeepAwakeService for plugin access.
/// </summary>
internal class KeepAwakePluginServiceAdapter : IKeepAwakePluginService
{
    private readonly KeepAwakeService _inner;
    private readonly Dictionary<string, bool> _pluginRequests = new();

    public bool IsActive => _inner.IsActive;
    public TimeSpan CurrentSessionDuration => _inner.CurrentSessionDuration;

    public event EventHandler<KeepAwakeStateChangedEventArgs>? StateChanged;

    public KeepAwakePluginServiceAdapter()
    {
        _inner = KeepAwakeService.Instance;
        _inner.ActiveStateChanged += OnInnerStateChanged;
    }

    private void OnInnerStateChanged(object? sender, bool isActive)
    {
        StateChanged?.Invoke(this, new KeepAwakeStateChangedEventArgs
        {
            IsActive = isActive
        });
    }

    public void RequestKeepAwake(string reason, string pluginId)
    {
        _pluginRequests[pluginId] = true;
        if (!_inner.IsActive)
        {
            _inner.SetActive(true);
        }
    }

    public void ReleaseKeepAwake(string pluginId)
    {
        _pluginRequests.Remove(pluginId);
        
        // Only deactivate if no other plugins are requesting
        if (!_pluginRequests.Any() && _inner.IsActive)
        {
            _inner.SetActive(false);
        }
    }
}

/// <summary>
/// Adapter for notification service.
/// </summary>
internal class NotificationPluginServiceAdapter : INotificationPluginService
{
    public void ShowNotification(string title, string message, NotificationLevel level = NotificationLevel.Info)
    {
        // Use balloon notifications
        Logger.Info("Plugin.Notification", $"[{level}] {title}: {message}");
    }

    public void ShowToast(string message, int durationMs = 3000)
    {
        Logger.Info("Plugin.Notification", $"Toast: {message}");
    }
}

/// <summary>
/// Adapter for scheduler service.
/// </summary>
internal class SchedulerPluginServiceAdapter : ISchedulerPluginService
{
    private readonly Dictionary<string, Timer> _timers = new();

    public void ScheduleOneTime(string taskId, DateTime executeAt, Func<Task> action)
    {
        CancelTask(taskId);

        var delay = executeAt - DateTime.Now;
        if (delay <= TimeSpan.Zero)
        {
            // Execute immediately
            _ = action();
            return;
        }

        var timer = new Timer(async _ =>
        {
            await action();
            CancelTask(taskId);
        }, null, delay, Timeout.InfiniteTimeSpan);

        _timers[taskId] = timer;
    }

    public void ScheduleRecurring(string taskId, TimeSpan interval, Func<Task> action)
    {
        CancelTask(taskId);

        var timer = new Timer(async _ =>
        {
            await action();
        }, null, interval, interval);

        _timers[taskId] = timer;
    }

    public void CancelTask(string taskId)
    {
        if (_timers.TryGetValue(taskId, out var timer))
        {
            timer.Dispose();
            _timers.Remove(taskId);
        }
    }
}

/// <summary>
/// Event aggregator implementation for plugin communication.
/// </summary>
internal class PluginEventAggregator : IPluginEventAggregator
{
    private readonly Dictionary<Type, Dictionary<string, Delegate>> _handlers = new();

    public void Subscribe<T>(string pluginId, Action<T> handler) where T : class
    {
        var type = typeof(T);
        if (!_handlers.ContainsKey(type))
        {
            _handlers[type] = new Dictionary<string, Delegate>();
        }
        _handlers[type][pluginId] = handler;
    }

    public void Unsubscribe<T>(string pluginId) where T : class
    {
        var type = typeof(T);
        if (_handlers.TryGetValue(type, out var handlers))
        {
            handlers.Remove(pluginId);
        }
    }

    public void Publish<T>(T eventData) where T : class
    {
        var type = typeof(T);
        if (_handlers.TryGetValue(type, out var handlers))
        {
            foreach (var handler in handlers.Values)
            {
                try
                {
                    ((Action<T>)handler)(eventData);
                }
                catch (Exception ex)
                {
                    Logger.Error("PluginEventAggregator", $"Error publishing event to handler", ex);
                }
            }
        }
    }
}

/// <summary>
/// Storage provider for plugin-specific data.
/// </summary>
internal class PluginStorage : IPluginStorage
{
    private readonly string _storagePath;
    private readonly Dictionary<string, object> _cache = new();

    public PluginStorage()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _storagePath = Path.Combine(appData, "Redball", "PluginData");
        
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }

    public void Set<T>(string key, T value)
    {
        _cache[key] = value!;
        SaveToDisk(key, value);
    }

    public T? Get<T>(string key)
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            return (T)cached;
        }

        return LoadFromDisk<T>(key);
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        var filePath = GetFilePath(key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    public bool Exists(string key)
    {
        if (_cache.ContainsKey(key))
            return true;

        return File.Exists(GetFilePath(key));
    }

    public void Clear()
    {
        _cache.Clear();
        // Optionally clear disk storage
    }

    private string GetFilePath(string key)
    {
        // Sanitize key for filename
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storagePath, $"{safeKey}.json");
    }

    private void SaveToDisk<T>(string key, T value)
    {
        try
        {
            var filePath = GetFilePath(key);
            var json = JsonSerializer.Serialize(value);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error("PluginStorage", $"Failed to save {key}", ex);
        }
    }

    private T? LoadFromDisk<T>(string key)
    {
        try
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
                return default;

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            Logger.Error("PluginStorage", $"Failed to load {key}", ex);
            return default;
        }
    }
}

/// <summary>
/// UI factory implementation for creating plugin UI components.
/// </summary>
internal class PluginUIFactory : IPluginUIFactory
{
    public IPluginSettingsPanel? CreateSettingsPanel()
    {
        // Return a default settings panel implementation
        return null;
    }

    public void RegisterMenuItem(string menuPath, string label, Action onClick, string? icon = null)
    {
        Logger.Info("PluginUIFactory", $"Registering menu item: {menuPath}/{label}");
    }

    public void RegisterCommand(string id, string name, string description, Action onExecute, string? shortcut = null)
    {
        Logger.Info("PluginUIFactory", $"Registering command: {id} - {name}");
    }
}
