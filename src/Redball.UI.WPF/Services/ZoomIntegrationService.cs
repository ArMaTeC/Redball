using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Detects Zoom meeting status via process analysis and log file parsing.
/// Integrates with meeting detection to prevent keep-awake interruption during Zoom calls.
/// </summary>
public class ZoomIntegrationService
{
    private static readonly Lazy<ZoomIntegrationService> _instance = new(() => new ZoomIntegrationService());
    public static ZoomIntegrationService Instance => _instance.Value;

    private readonly string _zoomLogPath;
    private DateTime _lastLogCheck = DateTime.MinValue;
    private ZoomStatus _lastStatus = ZoomStatus.Unknown;
    
    public event EventHandler<ZoomStatusChangedEventArgs>? StatusChanged;

    public bool IsEnabled => ConfigService.Instance.Config.MeetingAware;
    public ZoomStatus CurrentStatus => _lastStatus;
    public bool IsInMeeting => _lastStatus == ZoomStatus.InMeeting || 
                                 _lastStatus == ZoomStatus.ScreenSharing || 
                                 _lastStatus == ZoomStatus.Recording;
    public bool IsScreenSharing => _lastStatus == ZoomStatus.ScreenSharing;
    public bool IsRecording => _lastStatus == ZoomStatus.Recording;

    private ZoomIntegrationService()
    {
        // Zoom logs location
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _zoomLogPath = Path.Combine(appData, "Zoom", "logs", "zoom.txt");
        
        Logger.Verbose("ZoomIntegrationService", $"Zoom log path: {_zoomLogPath}");
    }

    /// <summary>
    /// Checks Zoom meeting status via process and log analysis.
    /// </summary>
    public async Task<ZoomStatus> CheckStatusAsync()
    {
        if (!IsEnabled)
        {
            return ZoomStatus.Unknown;
        }

        try
        {
            var zoomProcess = GetZoomProcess();
            if (zoomProcess == null)
            {
                UpdateStatus(ZoomStatus.NotRunning);
                return ZoomStatus.NotRunning;
            }

            // Check logs for detailed status
            var status = await GetStatusFromLogsAsync();
            
            // Fallback to process detection
            if (status == ZoomStatus.Unknown)
            {
                status = DetectStatusFromProcess(zoomProcess);
            }

            UpdateStatus(status);
            return status;
        }
        catch (Exception ex)
        {
            Logger.Debug("ZoomIntegrationService", $"Error checking status: {ex.Message}");
            return ZoomStatus.Unknown;
        }
    }

    /// <summary>
    /// Quick check for meeting participation.
    /// </summary>
    public bool IsZoomInMeeting()
    {
        if (!IsEnabled) return false;
        
        try
        {
            var zoomProcess = GetZoomProcess();
            if (zoomProcess == null) return false;

            // Check window title for meeting indicators
            var title = GetWindowTitle(zoomProcess);
            if (title != null)
            {
                var meetingIndicators = new[] { "zoom meeting", "zoom call", "zoom webinar" };
                if (meetingIndicators.Any(ind => title.Contains(ind, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return _lastStatus == ZoomStatus.InMeeting || 
                   _lastStatus == ZoomStatus.ScreenSharing || 
                   _lastStatus == ZoomStatus.Recording;
        }
        catch (Exception ex)
        {
            Logger.Debug("ZoomIntegrationService", $"Error checking if in Zoom meeting: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Detects if user is screen sharing in Zoom.
    /// </summary>
    public bool IsScreenSharingInZoom()
    {
        if (!IsEnabled) return false;
        
        try
        {
            return _lastStatus == ZoomStatus.ScreenSharing;
        }
        catch (Exception ex)
        {
            Logger.Debug("ZoomIntegrationService", $"Error checking screen share status: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Detects if Zoom meeting is being recorded.
    /// </summary>
    public bool IsRecordingInZoom()
    {
        if (!IsEnabled) return false;
        
        try
        {
            return _lastStatus == ZoomStatus.Recording;
        }
        catch (Exception ex)
        {
            Logger.Debug("ZoomIntegrationService", $"Error checking recording status: {ex.Message}");
            return false;
        }
    }

    private async Task<ZoomStatus> GetStatusFromLogsAsync()
    {
        try
        {
            if (!File.Exists(_zoomLogPath))
            {
                return ZoomStatus.Unknown;
            }

            var fileInfo = new FileInfo(_zoomLogPath);
            if (fileInfo.LastWriteTime <= _lastLogCheck)
            {
                return _lastStatus;
            }

            _lastLogCheck = fileInfo.LastWriteTime;

            var lines = await ReadLastLinesAsync(_zoomLogPath, 50);
            var status = ParseStatusFromLogs(lines);
            return status;
        }
        catch (Exception ex)
        {
            Logger.Debug("ZoomIntegrationService", $"Log parsing error: {ex.Message}");
            return ZoomStatus.Unknown;
        }
    }

    private ZoomStatus ParseStatusFromLogs(string[] lines)
    {
        foreach (var line in lines.Reverse())
        {
            // Check for meeting start/end
            if (line.Contains("Join meeting", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Meeting started", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Enter meeting", StringComparison.OrdinalIgnoreCase))
            {
                return ZoomStatus.InMeeting;
            }

            if (line.Contains("Leave meeting", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Meeting ended", StringComparison.OrdinalIgnoreCase))
            {
                return ZoomStatus.Available;
            }

            // Screen sharing
            if (line.Contains("Start share", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Screen sharing started", StringComparison.OrdinalIgnoreCase))
            {
                return ZoomStatus.ScreenSharing;
            }

            if (line.Contains("Stop share", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Screen sharing stopped", StringComparison.OrdinalIgnoreCase))
            {
                return ZoomStatus.InMeeting; // Back to meeting only
            }

            // Recording
            if (line.Contains("Start recording", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Recording started", StringComparison.OrdinalIgnoreCase))
            {
                return ZoomStatus.Recording;
            }

            if (line.Contains("Stop recording", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Recording stopped", StringComparison.OrdinalIgnoreCase))
            {
                return ZoomStatus.InMeeting; // Still in meeting
            }
        }

        return ZoomStatus.Unknown;
    }

    private ZoomStatus DetectStatusFromProcess(Process process)
    {
        try
        {
            var title = GetWindowTitle(process);
            if (title == null) return ZoomStatus.Available;

            // Check for sharing
            if (title.Contains("sharing", StringComparison.OrdinalIgnoreCase))
                return ZoomStatus.ScreenSharing;

            // Check for recording indicator
            if (title.Contains("recording", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("rec", StringComparison.OrdinalIgnoreCase))
                return ZoomStatus.Recording;

            // Check for meeting
            if (title.Contains("zoom meeting", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("zoom call", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("zoom webinar", StringComparison.OrdinalIgnoreCase))
                return ZoomStatus.InMeeting;

            // Check for waiting/available
            if (title.Contains("zoom", StringComparison.OrdinalIgnoreCase))
                return ZoomStatus.Available;

            return ZoomStatus.Unknown;
        }
        catch (Exception ex)
        {
            Logger.Debug("ZoomIntegrationService", $"Error detecting status from process: {ex.Message}");
            return ZoomStatus.Unknown;
        }
    }

    private Process? GetZoomProcess()
    {
        try
        {
            var zoom = Process.GetProcessesByName("Zoom").FirstOrDefault();
            if (zoom != null && !zoom.HasExited) return zoom;

            var zoomExe = Process.GetProcessesByName("zoom").FirstOrDefault();
            if (zoomExe != null && !zoomExe.HasExited) return zoomExe;

            return null;
        }
        catch (Exception ex)
        {
            Logger.Debug("ZoomIntegrationService", $"Error getting Zoom process: {ex.Message}");
            return null;
        }
    }

    private string? GetWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (Exception ex)
        {
            Logger.Debug("ZoomIntegrationService", $"Error getting window title: {ex.Message}");
            return null;
        }
    }

    private async Task<string[]> ReadLastLinesAsync(string path, int count)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            
            var lines = new System.Collections.Generic.List<string>();
            string? line;
            
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lines.Add(line);
                if (lines.Count > count)
                {
                    lines.RemoveAt(0);
                }
            }
            
            return lines.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Debug("ZoomIntegrationService", $"Error reading Zoom log file: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private void UpdateStatus(ZoomStatus newStatus)
    {
        if (newStatus != _lastStatus)
        {
            var oldStatus = _lastStatus;
            _lastStatus = newStatus;
            
            Logger.Info("ZoomIntegrationService", $"Status changed: {oldStatus} -> {newStatus}");
            
            StatusChanged?.Invoke(this, new ZoomStatusChangedEventArgs
            {
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}

public enum ZoomStatus
{
    Unknown,
    NotRunning,
    Available,
    InMeeting,
    ScreenSharing,
    Recording
}

public class ZoomStatusChangedEventArgs : EventArgs
{
    public ZoomStatus OldStatus { get; set; }
    public ZoomStatus NewStatus { get; set; }
    public DateTime Timestamp { get; set; }
}
