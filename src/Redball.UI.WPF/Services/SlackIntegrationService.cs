using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Detects Slack huddle status via process window analysis and state detection.
/// Integrates with meeting detection to prevent keep-awake interruption during Slack calls.
/// </summary>
public class SlackIntegrationService
{
    private static readonly Lazy<SlackIntegrationService> _instance = new(() => new SlackIntegrationService());
    public static SlackIntegrationService Instance => _instance.Value;

    private SlackHuddleStatus _lastStatus = SlackHuddleStatus.Unknown;
    
    public event EventHandler<SlackHuddleStatusChangedEventArgs>? StatusChanged;

    public bool IsEnabled => ConfigService.Instance.Config.MeetingAware;
    public SlackHuddleStatus CurrentStatus => _lastStatus;
    public bool IsInHuddle => _lastStatus == SlackHuddleStatus.InHuddle || _lastStatus == SlackHuddleStatus.ScreenSharing;
    public bool IsScreenSharing => _lastStatus == SlackHuddleStatus.ScreenSharing;

    private SlackIntegrationService()
    {
        Logger.Verbose("SlackIntegrationService", "Initialized");
    }

    /// <summary>
    /// Checks Slack huddle status via process and window analysis.
    /// </summary>
    public async Task<SlackHuddleStatus> CheckStatusAsync()
    {
        if (!IsEnabled)
        {
            return SlackHuddleStatus.Unknown;
        }

        try
        {
            var slackProcess = GetSlackProcess();
            if (slackProcess == null)
            {
                UpdateStatus(SlackHuddleStatus.NotRunning);
                return SlackHuddleStatus.NotRunning;
            }

            var status = DetectHuddleStatus(slackProcess);
            UpdateStatus(status);
            return status;
        }
        catch (Exception ex)
        {
            Logger.Debug("SlackIntegrationService", $"Error checking status: {ex.Message}");
            return SlackHuddleStatus.Unknown;
        }
    }

    /// <summary>
    /// Quick check for huddle participation.
    /// </summary>
    public bool IsSlackInHuddle()
    {
        if (!IsEnabled) return false;
        
        try
        {
            var slackProcess = GetSlackProcess();
            if (slackProcess == null) return false;

            // Check window title for huddle indicators
            var title = GetWindowTitle(slackProcess);
            if (title != null)
            {
                var huddleIndicators = new[] { "huddle", "call", "meeting" };
                if (huddleIndicators.Any(ind => title.Contains(ind, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return _lastStatus == SlackHuddleStatus.InHuddle || 
                   _lastStatus == SlackHuddleStatus.ScreenSharing;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detects if user is screen sharing in Slack huddle.
    /// </summary>
    public bool IsScreenSharingInSlack()
    {
        if (!IsEnabled) return false;
        
        try
        {
            var slackProcess = GetSlackProcess();
            if (slackProcess == null) return false;

            var title = GetWindowTitle(slackProcess);
            if (title?.Contains("screen sharing", StringComparison.OrdinalIgnoreCase) == true ||
                title?.Contains("sharing", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            return _lastStatus == SlackHuddleStatus.ScreenSharing;
        }
        catch
        {
            return false;
        }
    }

    private SlackHuddleStatus DetectHuddleStatus(Process process)
    {
        try
        {
            var title = GetWindowTitle(process);
            if (title == null) return SlackHuddleStatus.Available;

            // Screen sharing takes precedence
            if (title.Contains("screen sharing", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("sharing", StringComparison.OrdinalIgnoreCase))
                return SlackHuddleStatus.ScreenSharing;

            // Huddle indicators
            if (title.Contains("huddle", StringComparison.OrdinalIgnoreCase))
                return SlackHuddleStatus.InHuddle;

            // Call indicators
            if (title.Contains("call", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("calling", StringComparison.OrdinalIgnoreCase))
                return SlackHuddleStatus.InHuddle;

            return SlackHuddleStatus.Available;
        }
        catch
        {
            return SlackHuddleStatus.Unknown;
        }
    }

    private Process? GetSlackProcess()
    {
        try
        {
            var slack = Process.GetProcessesByName("slack").FirstOrDefault();
            if (slack != null && !slack.HasExited) return slack;

            // Also check for Slack with different process names
            var slackExe = Process.GetProcessesByName("Slack").FirstOrDefault();
            if (slackExe != null && !slackExe.HasExited) return slackExe;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private string? GetWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateStatus(SlackHuddleStatus newStatus)
    {
        if (newStatus != _lastStatus)
        {
            var oldStatus = _lastStatus;
            _lastStatus = newStatus;
            
            Logger.Info("SlackIntegrationService", $"Status changed: {oldStatus} -> {newStatus}");
            
            StatusChanged?.Invoke(this, new SlackHuddleStatusChangedEventArgs
            {
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}

public enum SlackHuddleStatus
{
    Unknown,
    NotRunning,
    Available,
    InHuddle,
    ScreenSharing
}

public class SlackHuddleStatusChangedEventArgs : EventArgs
{
    public SlackHuddleStatus OldStatus { get; set; }
    public SlackHuddleStatus NewStatus { get; set; }
    public DateTime Timestamp { get; set; }
}
