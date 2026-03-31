using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Redball.UI.Plugins;

/// <summary>
/// Base interface that all Redball plugins must implement.
/// Provides lifecycle management and core plugin functionality.
/// </summary>
public interface IRedballPlugin
{
    /// <summary>
    /// Unique identifier for the plugin (reverse domain notation recommended)
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// Display name of the plugin
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Plugin version (semver)
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Brief description of plugin functionality
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Plugin author/organization
    /// </summary>
    string Author { get; }

    /// <summary>
    /// URL to plugin homepage or documentation
    /// </summary>
    string? HomepageUrl { get; }

    /// <summary>
    /// Minimum Redball version required
    /// </summary>
    string MinRedballVersion { get; }

    /// <summary>
    /// Plugin category for organization
    /// </summary>
    PluginCategory Category { get; }

    /// <summary>
    /// Called when plugin is first loaded. Initialize resources here.
    /// </summary>
    Task InitializeAsync(IPluginContext context);

    /// <summary>
    /// Called when plugin is being unloaded. Cleanup resources here.
    /// </summary>
    Task ShutdownAsync();

    /// <summary>
    /// Called when plugin is activated (enabled by user)
    /// </summary>
    Task ActivateAsync();

    /// <summary>
    /// Called when plugin is deactivated (disabled by user)
    /// </summary>
    Task DeactivateAsync();
}

/// <summary>
/// Context provided to plugins for interacting with Redball core
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// Logger for plugin-specific messages
    /// </summary>
    IPluginLogger Logger { get; }

    /// <summary>
    /// Access to Redball configuration (read-only for safety)
    /// </summary>
    IPluginConfigAccessor Config { get; }

    /// <summary>
    /// Service provider for accessing Redball services
    /// </summary>
    IPluginServiceProvider Services { get; }

    /// <summary>
    /// Event aggregator for subscribing to/publishing events
    /// </summary>
    IPluginEventAggregator Events { get; }

    /// <summary>
    /// Storage provider for plugin-specific data
    /// </summary>
    IPluginStorage Storage { get; }

    /// <summary>
    /// UI factory for creating plugin UI components
    /// </summary>
    IPluginUIFactory UI { get; }
}

/// <summary>
/// Plugin categories for organization in UI
/// </summary>
public enum PluginCategory
{
    Input,
    Output,
    Integration,
    Automation,
    UI,
    Analytics,
    Security,
    Other
}

/// <summary>
/// Logger interface for plugins
/// </summary>
public interface IPluginLogger
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

/// <summary>
/// Read-only config accessor for plugins
/// </summary>
public interface IPluginConfigAccessor
{
    T? GetValue<T>(string key);
    bool GetBool(string key, bool defaultValue = false);
    int GetInt(string key, int defaultValue = 0);
    string GetString(string key, string defaultValue = "");
}

/// <summary>
/// Service provider for accessing Redball services
/// </summary>
public interface IPluginServiceProvider
{
    /// <summary>
    /// Get a service by type. Returns null if not available.
    /// </summary>
    T? GetService<T>() where T : class;

    /// <summary>
    /// Get the keep-awake service for controlling sleep prevention
    /// </summary>
    IKeepAwakePluginService? KeepAwake { get; }

    /// <summary>
    /// Get the notification service for showing notifications
    /// </summary>
    INotificationPluginService? Notifications { get; }

    /// <summary>
    /// Get the scheduler service for scheduling tasks
    /// </summary>
    ISchedulerPluginService? Scheduler { get; }
}

/// <summary>
/// Keep-awake service interface exposed to plugins
/// </summary>
public interface IKeepAwakePluginService
{
    bool IsActive { get; }
    TimeSpan CurrentSessionDuration { get; }
    
    void RequestKeepAwake(string reason, string pluginId);
    void ReleaseKeepAwake(string pluginId);
    
    event EventHandler<KeepAwakeStateChangedEventArgs>? StateChanged;
}

public class KeepAwakeStateChangedEventArgs : EventArgs
{
    public bool IsActive { get; set; }
    public string? ChangedByPlugin { get; set; }
}

/// <summary>
/// Notification service interface exposed to plugins
/// </summary>
public interface INotificationPluginService
{
    void ShowNotification(string title, string message, NotificationLevel level = NotificationLevel.Info);
    void ShowToast(string message, int durationMs = 3000);
}

public enum NotificationLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Scheduler service interface exposed to plugins
/// </summary>
public interface ISchedulerPluginService
{
    void ScheduleOneTime(string taskId, DateTime executeAt, Func<Task> action);
    void ScheduleRecurring(string taskId, TimeSpan interval, Func<Task> action);
    void CancelTask(string taskId);
}

/// <summary>
/// Event aggregator for plugin communication
/// </summary>
public interface IPluginEventAggregator
{
    void Subscribe<T>(string pluginId, Action<T> handler) where T : class;
    void Unsubscribe<T>(string pluginId) where T : class;
    void Publish<T>(T eventData) where T : class;
}

/// <summary>
/// Storage provider for plugin-specific data
/// </summary>
public interface IPluginStorage
{
    void Set<T>(string key, T value);
    T? Get<T>(string key);
    void Remove(string key);
    bool Exists(string key);
    void Clear();
}

/// <summary>
/// UI factory for plugin UI components
/// </summary>
public interface IPluginUIFactory
{
    /// <summary>
    /// Create a settings panel for plugin preferences
    /// </summary>
    IPluginSettingsPanel? CreateSettingsPanel();

    /// <summary>
    /// Register a menu item in the main menu
    /// </summary>
    void RegisterMenuItem(string menuPath, string label, Action onClick, string? icon = null);

    /// <summary>
    /// Register a command for the command palette
    /// </summary>
    void RegisterCommand(string id, string name, string description, Action onExecute, string? shortcut = null);
}

/// <summary>
/// Settings panel interface for plugins
/// </summary>
public interface IPluginSettingsPanel
{
    object? CreateUI(); // Returns WPF FrameworkElement or platform-specific UI
    void LoadSettings();
    void SaveSettings();
}

/// <summary>
/// Specialized interfaces for different plugin types
/// </summary>

/// <summary>
/// Plugins that provide custom input methods (alternative to TypeThing)
/// </summary>
public interface IInputPlugin : IRedballPlugin
{
    /// <summary>
    /// Input method name shown to users
    /// </summary>
    string InputMethodName { get; }

    /// <summary>
    /// Check if this input method is available on current system
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Send text input
    /// </summary>
    Task SendTextAsync(string text, InputOptions options);

    /// <summary>
    /// Send key press
    /// </summary>
    Task SendKeyAsync(string key, KeyModifiers modifiers = KeyModifiers.None);
}

public class InputOptions
{
    public int MinDelayMs { get; set; } = 30;
    public int MaxDelayMs { get; set; } = 120;
    public bool AddRandomPauses { get; set; } = true;
    public bool TypeNewlines { get; set; } = true;
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Windows = 8
}

/// <summary>
/// Plugins that integrate with external services/meeting platforms
/// </summary>
public interface IIntegrationPlugin : IRedballPlugin
{
    /// <summary>
    /// Name of the service being integrated
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Check if the integrated service is currently running/active
    /// </summary>
    Task<bool> IsServiceRunningAsync();

    /// <summary>
    /// Check if user is currently in a meeting/call
    /// </summary>
    Task<bool> IsInMeetingAsync();

    /// <summary>
    /// Get current meeting info if available
    /// </summary>
    Task<MeetingInfo?> GetCurrentMeetingAsync();
}

public class MeetingInfo
{
    public string? Title { get; set; }
    public DateTime? StartedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool IsScreenSharing { get; set; }
}

/// <summary>
/// Plugins that provide custom automation/scripting
/// </summary>
public interface IAutomationPlugin : IRedballPlugin
{
    /// <summary>
    /// Scripting language supported (e.g., "lua", "python", "javascript")
    /// </summary>
    string? ScriptingLanguage { get; }

    /// <summary>
    /// Execute a script
    /// </summary>
    Task<object?> ExecuteScriptAsync(string script, Dictionary<string, object>? parameters = null);

    /// <summary>
    /// Register an automation action
    /// </summary>
    void RegisterAction(string actionId, string actionName, string description, Func<Task> action);
}

/// <summary>
/// Plugins that provide custom UI widgets/overlays
/// </summary>
public interface IUIPlugin : IRedballPlugin
{
    /// <summary>
    /// Create a widget for the mini-widget overlay
    /// </summary>
    object? CreateWidget(); // Returns WPF FrameworkElement or platform-specific UI

    /// <summary>
    /// Widget size hint
    /// </summary>
    (int width, int height) WidgetSize { get; }

    /// <summary>
    /// Whether widget should be shown in mini-widget overlay
    /// </summary>
    bool ShowInMiniWidget { get; }
}
