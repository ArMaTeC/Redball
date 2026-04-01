using System;
using System.IO;
using System.Threading.Tasks;
using Redball.UI.Plugins;

namespace Redball.Plugins.Samples;

/// <summary>
/// Sample plugin demonstrating the Redball plugin architecture.
/// This plugin provides a custom notification provider that logs
/// notifications to a file with timestamps.
/// </summary>
public class LoggingNotificationPlugin : IRedballPlugin
{
    private IPluginContext? _context;
    private bool _isActive;

    // Plugin metadata
    public string PluginId => "com.armatec.redball.plugins.logging-notification";
    public string Name => "Logging Notifications";
    public string Version => "1.0.0";
    public string Description => "Logs all notifications to a file with timestamps for debugging and auditing purposes.";
    public string Author => "ArMaTeC";
    public string? HomepageUrl => "https://github.com/ArMaTeC/Redball";
    public string MinRedballVersion => "3.0.0";
    public PluginCategory Category => PluginCategory.Output;

    public async Task InitializeAsync(IPluginContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        
        _context.Logger.Info($"Initializing {Name} plugin...");
        
        // Load configuration
        var logPath = _context.Config.GetString("LogFilePath", 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "Redball", "notification-log.txt"));
        
        // Ensure log directory exists
        var logDir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
        
        // Write initialization marker
        await File.AppendAllTextAsync(logPath, 
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Plugin initialized: {Name} v{Version}{Environment.NewLine}");
        
        _context.Logger.Info($"{Name} plugin initialized successfully");
    }

    public async Task ActivateAsync()
    {
        if (_context == null) return;
        
        _isActive = true;
        _context.Logger.Info($"{Name} plugin activated");
        
        // Log activation
        var logPath = GetLogPath();
        await File.AppendAllTextAsync(logPath, 
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Plugin activated{Environment.NewLine}");
        
        // Subscribe to events or register with services here
    }

    public async Task DeactivateAsync()
    {
        if (_context == null) return;
        
        _isActive = false;
        _context.Logger.Info($"{Name} plugin deactivated");
        
        // Log deactivation
        var logPath = GetLogPath();
        await File.AppendAllTextAsync(logPath, 
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Plugin deactivated{Environment.NewLine}");
    }

    public async Task ShutdownAsync()
    {
        if (_context == null) return;
        
        _context.Logger.Info($"Shutting down {Name} plugin...");
        
        // Log shutdown
        var logPath = GetLogPath();
        await File.AppendAllTextAsync(logPath, 
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Plugin shutdown{Environment.NewLine}");
        
        _context = null;
    }

    /// <summary>
    /// Log a notification message (custom method for this plugin type)
    /// </summary>
    public async Task LogNotificationAsync(string title, string message, string level = "Info")
    {
        if (!_isActive || _context == null) return;
        
        var logPath = GetLogPath();
        var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {title}: {message}{Environment.NewLine}";
        
        await File.AppendAllTextAsync(logPath, entry);
        
        // Also show a toast via the notification service
        _context.Services.Notifications?.ShowToast($"Logged: {title}");
    }

    private string GetLogPath()
    {
        return _context?.Config.GetString("LogFilePath", 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "Redball", "notification-log.txt")) 
            ?? Path.Combine(Path.GetTempPath(), "redball-notifications.txt");
    }
}
