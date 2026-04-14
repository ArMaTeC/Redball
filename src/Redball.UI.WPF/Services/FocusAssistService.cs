using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Windows Focus Assist integration service
/// Detects and responds to Windows Focus Assist modes (priority only, alarms only, off)
/// </summary>
public class FocusAssistService : IDisposable
{
    private static readonly Lazy<FocusAssistService> _instance = new(() => new FocusAssistService());
    public static FocusAssistService Instance => _instance.Value;

    public event EventHandler<FocusAssistChangedEventArgs>? FocusAssistChanged;

    private System.Threading.Timer? _pollingTimer;
    private bool _disposed;

    public bool IsEnabled => ConfigService.Instance.Config.FocusAssistIntegration;
    public FocusAssistState CurrentState { get; private set; }
    public bool IsFocusModeActive => CurrentState == FocusAssistState.PriorityOnly ||
                                      CurrentState == FocusAssistState.AlarmsOnly;

    private FocusAssistService()
    {
        CurrentState = FocusAssistState.Off;

        // Monitor registry for Focus Assist changes
        try
        {
            WatchFocusAssistRegistry();
        }
        catch (Exception ex)
        {
            Logger.Warning("FocusAssistService", $"Could not set up registry watcher: {ex.Message}");
        }

        // Initial check
        _ = CheckFocusAssistStateAsync();

        Logger.Verbose("FocusAssistService", "Initialized");
    }

    /// <summary>
    /// Checks current Windows Focus Assist state
    /// </summary>
    public async Task<FocusAssistState> CheckFocusAssistStateAsync()
    {
        if (!IsEnabled)
        {
            return FocusAssistState.Unknown;
        }

        try
        {
            // Read from Windows registry
            // HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings");

            if (key == null)
            {
                return FocusAssistState.Unknown;
            }

            // Check for Focus Assist settings
            // NOC_GLOBAL_SETTING_TOASTS_ENABLED indicates the state
            var toastSetting = key.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED");
            var prioritySetting = key.GetValue("NOC_GLOBAL_SETTING_ALLOW_CRITICAL_TOASTS_ABOVE_LOCK");

            FocusAssistState newState;

            if (toastSetting is int toastValue)
            {
                // 0 = Off, 1 = Priority only, 2 = Alarms only
                newState = toastValue switch
                {
                    0 => FocusAssistState.Off,
                    1 => FocusAssistState.PriorityOnly,
                    2 => FocusAssistState.AlarmsOnly,
                    _ => FocusAssistState.Unknown
                };
            }
            else
            {
                newState = FocusAssistState.Off;
            }

            // Update state if changed
            if (newState != CurrentState)
            {
                var oldState = CurrentState;
                CurrentState = newState;

                FocusAssistChanged?.Invoke(this, new FocusAssistChangedEventArgs
                {
                    OldState = oldState,
                    NewState = newState,
                    ChangedAt = DateTime.UtcNow
                });

                Logger.Info("FocusAssistService", $"Focus Assist changed: {oldState} -> {newState}");
            }

            return CurrentState;
        }
        catch (Exception ex)
        {
            Logger.Debug("FocusAssistService", $"Error checking Focus Assist: {ex.Message}");
            return FocusAssistState.Unknown;
        }
    }

    /// <summary>
    /// Returns true if notifications should be suppressed based on Focus Assist
    /// </summary>
    public bool ShouldSuppressNotifications()
    {
        return IsFocusModeActive;
    }

    /// <summary>
    /// Returns true if critical notifications are allowed
    /// </summary>
    public bool AreCriticalNotificationsAllowed()
    {
        return CurrentState != FocusAssistState.AlarmsOnly;
    }

    private void WatchFocusAssistRegistry()
    {
        // Windows doesn't provide a direct API for Focus Assist notifications
        // We poll periodically to detect changes
        _pollingTimer = new System.Threading.Timer(
            async _ => await CheckFocusAssistStateAsync(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30)
        );
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _pollingTimer?.Dispose();
        _pollingTimer = null;
    }
}

public enum FocusAssistState
{
    Unknown,
    Off,
    PriorityOnly,
    AlarmsOnly
}

public class FocusAssistChangedEventArgs : EventArgs
{
    public FocusAssistState OldState { get; set; }
    public FocusAssistState NewState { get; set; }
    public DateTime ChangedAt { get; set; }
}
