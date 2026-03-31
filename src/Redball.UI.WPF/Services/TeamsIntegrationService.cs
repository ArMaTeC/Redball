using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Detects Microsoft Teams presence status via Teams log files and process state.
/// Integrates with meeting detection to prevent keep-awake interruption during Teams calls.
/// </summary>
public class TeamsIntegrationService
{
    private static readonly Lazy<TeamsIntegrationService> _instance = new(() => new TeamsIntegrationService());
    public static TeamsIntegrationService Instance => _instance.Value;

    private readonly string _teamsLogPath;
    private DateTime _lastLogCheck = DateTime.MinValue;
    private TeamsStatus _lastStatus = TeamsStatus.Unknown;
    
    public event EventHandler<TeamsStatusChangedEventArgs>? StatusChanged;

    public bool IsEnabled => ConfigService.Instance.Config.MeetingAware;
    public TeamsStatus CurrentStatus => _lastStatus;
    public bool IsInCall => _lastStatus == TeamsStatus.InCall || _lastStatus == TeamsStatus.InMeeting;
    public bool IsPresenting => _lastStatus == TeamsStatus.Presenting;

    private TeamsIntegrationService()
    {
        // Teams logs location
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _teamsLogPath = Path.Combine(localAppData, "Microsoft", "Teams", "logs.txt");
        
        Logger.Verbose("TeamsIntegrationService", $"Teams log path: {_teamsLogPath}");
    }

    /// <summary>
    /// Checks Teams status by reading log files and process state.
    /// </summary>
    public async Task<TeamsStatus> CheckStatusAsync()
    {
        if (!IsEnabled)
        {
            return TeamsStatus.Unknown;
        }

        try
        {
            // Check if Teams is running
            var teamsProcess = GetTeamsProcess();
            if (teamsProcess == null)
            {
                UpdateStatus(TeamsStatus.NotRunning);
                return TeamsStatus.NotRunning;
            }

            // Read Teams logs for status
            var status = await GetStatusFromLogsAsync();
            
            // Fallback to process detection if logs unavailable
            if (status == TeamsStatus.Unknown)
            {
                status = DetectStatusFromProcess(teamsProcess);
            }

            UpdateStatus(status);
            return status;
        }
        catch (Exception ex)
        {
            Logger.Debug("TeamsIntegrationService", $"Error checking status: {ex.Message}");
            return TeamsStatus.Unknown;
        }
    }

    /// <summary>
    /// Quick check for call/meeting status without full log parsing.
    /// </summary>
    public bool IsTeamsInMeeting()
    {
        if (!IsEnabled) return false;
        
        try
        {
            // Fast check: process existence + window title
            var teamsProcess = GetTeamsProcess();
            if (teamsProcess == null) return false;

            // Check window title for meeting indicators
            var title = GetWindowTitle(teamsProcess);
            if (title != null)
            {
                var meetingIndicators = new[] { "Microsoft Teams Call", "Meeting", "Calling" };
                if (meetingIndicators.Any(ind => title.Contains(ind, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            // Check for elevated CPU (screen sharing indicator)
            try
            {
                teamsProcess.Refresh();
                if (teamsProcess.TotalProcessorTime > TimeSpan.FromSeconds(10))
                {
                    // High CPU may indicate active call
                }
            }
            catch { }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detects if user is screen sharing in Teams.
    /// </summary>
    public bool IsScreenSharing()
    {
        if (!IsEnabled) return false;
        
        try
        {
            // Screen sharing detection via window analysis
            var teamsProcess = GetTeamsProcess();
            if (teamsProcess == null) return false;

            // Check for sharing indicator in window title
            var title = GetWindowTitle(teamsProcess);
            if (title?.Contains("Sharing", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            return _lastStatus == TeamsStatus.Presenting;
        }
        catch
        {
            return false;
        }
    }

    private async Task<TeamsStatus> GetStatusFromLogsAsync()
    {
        try
        {
            if (!File.Exists(_teamsLogPath))
            {
                return TeamsStatus.Unknown;
            }

            var fileInfo = new FileInfo(_teamsLogPath);
            if (fileInfo.LastWriteTime <= _lastLogCheck)
            {
                // No new log entries
                return _lastStatus;
            }

            _lastLogCheck = fileInfo.LastWriteTime;

            // Read last 100 lines of log
            var lines = await ReadLastLinesAsync(_teamsLogPath, 100);
            
            // Parse for status indicators
            var status = ParseStatusFromLogs(lines);
            return status;
        }
        catch (Exception ex)
        {
            Logger.Debug("TeamsIntegrationService", $"Log parsing error: {ex.Message}");
            return TeamsStatus.Unknown;
        }
    }

    private TeamsStatus ParseStatusFromLogs(string[] lines)
    {
        // Look for status indicators in recent log entries
        // Teams logs status changes with specific patterns
        
        foreach (var line in lines.Reverse())
        {
            // Check for call/meeting indicators
            if (line.Contains("call-state-changed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("callStateChanged", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("connected", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.InCall;
                if (line.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.Available;
            }

            // Check for meeting indicators
            if (line.Contains("meeting", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("join", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("start", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.InMeeting;
                if (line.Contains("leave", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("end", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.Available;
            }

            // Check for presenting/sharing
            if (line.Contains("screen-sharing", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("sharing", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("started", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.Presenting;
                if (line.Contains("stopped", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.InMeeting; // Back to meeting only
            }

            // Status availability
            if (line.Contains("status-changed", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains("available", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.Available;
                if (line.Contains("busy", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.Busy;
                if (line.Contains("away", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.Away;
                if (line.Contains("dnd", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("do-not-disturb", StringComparison.OrdinalIgnoreCase))
                    return TeamsStatus.DoNotDisturb;
            }
        }

        return TeamsStatus.Unknown;
    }

    private TeamsStatus DetectStatusFromProcess(Process process)
    {
        try
        {
            // Check window title for status indicators
            var title = GetWindowTitle(process);
            if (title == null) return TeamsStatus.Unknown;

            // Call indicators
            if (title.Contains("Call", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Calling", StringComparison.OrdinalIgnoreCase))
                return TeamsStatus.InCall;

            // Meeting indicators
            if (title.Contains("Meeting", StringComparison.OrdinalIgnoreCase))
                return TeamsStatus.InMeeting;

            // Sharing indicators
            if (title.Contains("Sharing", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Presenting", StringComparison.OrdinalIgnoreCase))
                return TeamsStatus.Presenting;

            return TeamsStatus.Available;
        }
        catch
        {
            return TeamsStatus.Unknown;
        }
    }

    private Process? GetTeamsProcess()
    {
        try
        {
            // Teams can run as "Teams" or "Teams.exe"
            var teams = Process.GetProcessesByName("Teams").FirstOrDefault();
            if (teams != null && !teams.HasExited) return teams;

            // Check for new Teams (Windows 11 version)
            var msTeams = Process.GetProcessesByName("ms-teams").FirstOrDefault();
            if (msTeams != null && !msTeams.HasExited) return msTeams;

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

    private async Task<string[]> ReadLastLinesAsync(string path, int count)
    {
        try
        {
            // Use FileStream with FileShare.ReadWrite to avoid locking
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
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void UpdateStatus(TeamsStatus newStatus)
    {
        if (newStatus != _lastStatus)
        {
            var oldStatus = _lastStatus;
            _lastStatus = newStatus;
            
            Logger.Info("TeamsIntegrationService", $"Status changed: {oldStatus} -> {newStatus}");
            
            StatusChanged?.Invoke(this, new TeamsStatusChangedEventArgs
            {
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}

public enum TeamsStatus
{
    Unknown,
    NotRunning,
    Available,
    Busy,
    Away,
    DoNotDisturb,
    InCall,
    InMeeting,
    Presenting
}

public class TeamsStatusChangedEventArgs : EventArgs
{
    public TeamsStatus OldStatus { get; set; }
    public TeamsStatus NewStatus { get; set; }
    public DateTime Timestamp { get; set; }
}
