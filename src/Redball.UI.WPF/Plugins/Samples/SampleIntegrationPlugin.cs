using System;
using System.Threading.Tasks;
using Redball.UI.Plugins;

namespace Redball.Plugins.Samples;

/// <summary>
/// Sample integration plugin that monitors a hypothetical external service.
/// Demonstrates how to create integration plugins that connect with external tools.
/// </summary>
public class SampleIntegrationPlugin : IIntegrationPlugin
{
    private IPluginContext? _context;
    private Timer? _monitorTimer;
    private bool _isActive;
    private bool _lastMeetingState;

    // Plugin metadata
    public string PluginId => "com.armatec.redball.plugins.sample-integration";
    public string Name => "Sample External Service Integration";
    public string Version => "1.0.0";
    public string Description => "Demonstrates integration with external services. Monitors a sample service for meeting status.";
    public string Author => "ArMaTeC";
    public string? HomepageUrl => "https://github.com/ArMaTeC/Redball";
    public string MinRedballVersion => "3.0.0";
    public PluginCategory Category => PluginCategory.Integration;

    // IIntegrationPlugin specific
    public string ServiceName => "SampleExternalService";

    public async Task InitializeAsync(IPluginContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _context.Logger.Info($"Initializing {Name} plugin...");
        
        // Register with event aggregator for keep-awake coordination
        _context.Events.Subscribe<KeepAwakeStateChangedEventArgs>(PluginId, OnKeepAwakeStateChanged);
        
        _context.Logger.Info($"{Name} plugin initialized");
    }

    public async Task ActivateAsync()
    {
        if (_context == null) return;
        
        _isActive = true;
        _context.Logger.Info($"{Name} plugin activated");
        
        // Start monitoring the external service
        StartMonitoring();
    }

    public async Task DeactivateAsync()
    {
        _isActive = false;
        _monitorTimer?.Dispose();
        _monitorTimer = null;
        
        _context?.Logger.Info($"{Name} plugin deactivated");
    }

    public async Task ShutdownAsync()
    {
        if (_context == null) return;
        
        _context.Events.Unsubscribe<KeepAwakeStateChangedEventArgs>(PluginId);
        _monitorTimer?.Dispose();
        
        _context.Logger.Info($"{Name} plugin shutdown");
        _context = null;
    }

    // IIntegrationPlugin implementation
    public Task<bool> IsServiceRunningAsync()
    {
        // Simulate checking if external service is running
        // In real implementation, check process, registry, or API
        return Task.FromResult(true);
    }

    public async Task<bool> IsInMeetingAsync()
    {
        // Simulate checking meeting status
        // In real implementation, query the external service's API
        await Task.Delay(100); // Simulate API call
        return _lastMeetingState;
    }

    public async Task<MeetingInfo?> GetCurrentMeetingAsync()
    {
        if (!await IsInMeetingAsync())
            return null;

        return new MeetingInfo
        {
            Title = "Sample Meeting",
            StartedAt = DateTime.Now.AddMinutes(-30),
            Duration = TimeSpan.FromMinutes(30),
            IsScreenSharing = false
        };
    }

    private void StartMonitoring()
    {
        // Monitor the external service every 30 seconds
        _monitorTimer = new Timer(async _ =>
        {
            await CheckMeetingStatus();
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private async Task CheckMeetingStatus()
    {
        if (!_isActive || _context == null) return;

        try
        {
            var isInMeeting = await IsInMeetingAsync();
            
            // Simulate meeting detection (random for demo)
            var random = new Random();
            var detectedMeeting = random.Next(5) == 0; // 20% chance of meeting
            
            if (detectedMeeting != _lastMeetingState)
            {
                _lastMeetingState = detectedMeeting;
                
                if (detectedMeeting)
                {
                    _context.Logger.Info("Detected meeting start via external service");
                    
                    // Optionally auto-pause keep-awake or show notification
                    if (_context.Services.Notifications != null)
                    {
                        _context.Services.Notifications.ShowNotification(
                            "External Meeting Detected", 
                            "Sample service shows you're in a meeting",
                            NotificationLevel.Info);
                    }
                    
                    // Publish event for other plugins
                    _context.Events.Publish(new ExternalMeetingEventArgs
                    {
                        IsInMeeting = true,
                        ServiceName = ServiceName
                    });
                }
                else
                {
                    _context.Logger.Info("Detected meeting end via external service");
                    
                    _context.Events.Publish(new ExternalMeetingEventArgs
                    {
                        IsInMeeting = false,
                        ServiceName = ServiceName
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _context.Logger.Error("Error checking meeting status", ex);
        }
    }

    private void OnKeepAwakeStateChanged(KeepAwakeStateChangedEventArgs args)
    {
        _context?.Logger.Debug($"Keep-awake state changed: {args.IsActive}");
        
        // Could coordinate with external service here
        // e.g., sync status, update presence, etc.
    }
}

/// <summary>
/// Event args for external meeting detection
/// </summary>
public class ExternalMeetingEventArgs
{
    public bool IsInMeeting { get; set; }
    public string ServiceName { get; set; } = string.Empty;
}
