using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Stage of the update lifecycle.
/// </summary>
public enum UpdateLifecycleStage
{
    CheckRequested,
    UpdateAvailable,
    DownloadStarted,
    DownloadProgress,
    DownloadComplete,
    VerificationStarted,
    VerificationComplete,
    InstallStarted,
    InstallProgress,
    InstallComplete,
    RestartRequested,
    RestartComplete,
    RollbackStarted,
    RollbackComplete,
    Failed
}

/// <summary>
/// Update event record.
/// </summary>
public class UpdateEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string UpdateVersion { get; set; } = "";
    public string FromVersion { get; set; } = "";
    public UpdateLifecycleStage Stage { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public TimeSpan? Duration { get; set; }
    public long? BytesTransferred { get; set; }
    public long? TotalBytes { get; set; }
}

/// <summary>
/// Complete update session tracking.
/// </summary>
public class UpdateSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string TargetVersion { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<UpdateEvent> Events { get; set; } = new();
    public bool IsSuccessful { get; set; }
    public bool IsRolledBack { get; set; }
    public TimeSpan? TotalDuration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : null;
    public string? FailureReason { get; set; }

    public UpdateEvent? GetLatestEvent(UpdateLifecycleStage stage)
    {
        return Events.LastOrDefault(e => e.Stage == stage);
    }

    public TimeSpan? GetStageDuration(UpdateLifecycleStage fromStage, UpdateLifecycleStage toStage)
    {
        var from = Events.FirstOrDefault(e => e.Stage == fromStage)?.Timestamp;
        var to = Events.FirstOrDefault(e => e.Stage == toStage)?.Timestamp;

        if (from.HasValue && to.HasValue)
            return to.Value - from.Value;

        return null;
    }
}

/// <summary>
/// Update metrics summary.
/// </summary>
public class UpdateMetrics
{
    public TimeSpan AverageDownloadTime { get; set; }
    public TimeSpan AverageInstallTime { get; set; }
    public TimeSpan AverageTotalTime { get; set; }
    public double SuccessRate { get; set; }
    public double RollbackRate { get; set; }
    public Dictionary<UpdateLifecycleStage, int> StageFailureCounts { get; set; } = new();
    public List<string> CommonFailureReasons { get; set; } = new();
}

/// <summary>
/// End-to-end update observability service.
/// Implements dist-2 from improve_me.txt: End-to-end update observability.
/// </summary>
public class UpdateObservabilityService
{
    private static readonly Lazy<UpdateObservabilityService> _instance = new(() => new UpdateObservabilityService());
    public static UpdateObservabilityService Instance => _instance.Value;

    private readonly List<UpdateSession> _sessions = new();
    private readonly object _lock = new();
    private UpdateSession? _activeSession;

    public event EventHandler<UpdateEvent>? UpdateEventRecorded;
    public event EventHandler<UpdateSession>? UpdateSessionCompleted;

    private UpdateObservabilityService()
    {
        Logger.Info("UpdateObservabilityService", "Update observability service initialized");
    }

    /// <summary>
    /// Starts a new update session.
    /// </summary>
    public UpdateSession StartSession(string fromVersion, string toVersion)
    {
        var session = new UpdateSession
        {
            SourceVersion = fromVersion,
            TargetVersion = toVersion,
            StartedAt = DateTime.Now
        };

        lock (_lock)
        {
            _sessions.Add(session);
            _activeSession = session;
        }

        // Record initial event
        RecordEvent(UpdateLifecycleStage.CheckRequested);

        Logger.Info("UpdateObservabilityService", $"Update session started: {fromVersion} -> {toVersion}");

        return session;
    }

    /// <summary>
    /// Records an update event.
    /// </summary>
    public void RecordEvent(UpdateLifecycleStage stage, string? errorMessage = null, Dictionary<string, string>? metadata = null)
    {
        if (_activeSession == null) return;

        var updateEvent = new UpdateEvent
        {
            UpdateVersion = _activeSession.TargetVersion,
            FromVersion = _activeSession.SourceVersion,
            Stage = stage,
            Timestamp = DateTime.Now,
            ErrorMessage = errorMessage,
            Metadata = metadata ?? new Dictionary<string, string>()
        };

        lock (_lock)
        {
            _activeSession.Events.Add(updateEvent);
        }

        Logger.Info("UpdateObservabilityService", $"Update event: {stage} for {_activeSession.TargetVersion}");

        if (errorMessage != null)
        {
            Logger.Error("UpdateObservabilityService", $"Update error at {stage}: {errorMessage}");
        }

        UpdateEventRecorded?.Invoke(this, updateEvent);

        // Auto-complete session on terminal stages
        if (stage == UpdateLifecycleStage.RestartComplete || stage == UpdateLifecycleStage.RollbackComplete || stage == UpdateLifecycleStage.Failed)
        {
            CompleteSession(stage == UpdateLifecycleStage.Failed);
        }
    }

    /// <summary>
    /// Records download progress.
    /// </summary>
    public void RecordDownloadProgress(long bytesTransferred, long totalBytes)
    {
        if (_activeSession == null) return;

        var evt = new UpdateEvent
        {
            UpdateVersion = _activeSession.TargetVersion,
            FromVersion = _activeSession.SourceVersion,
            Stage = UpdateLifecycleStage.DownloadProgress,
            Timestamp = DateTime.Now,
            BytesTransferred = bytesTransferred,
            TotalBytes = totalBytes,
            Metadata = new Dictionary<string, string>
            {
                ["progress_percent"] = totalBytes > 0
                    ? ((double)bytesTransferred / totalBytes * 100).ToString("F1")
                    : "0"
            }
        };

        lock (_lock)
        {
            // Replace last progress event if exists
            var lastProgress = _activeSession.Events.LastOrDefault(e => e.Stage == UpdateLifecycleStage.DownloadProgress);
            if (lastProgress != null)
            {
                _activeSession.Events.Remove(lastProgress);
            }
            _activeSession.Events.Add(evt);
        }

        UpdateEventRecorded?.Invoke(this, evt);
    }

    /// <summary>
    /// Records install progress.
    /// </summary>
    public void RecordInstallProgress(int percentageComplete)
    {
        if (_activeSession == null) return;

        var evt = new UpdateEvent
        {
            UpdateVersion = _activeSession.TargetVersion,
            FromVersion = _activeSession.SourceVersion,
            Stage = UpdateLifecycleStage.InstallProgress,
            Timestamp = DateTime.Now,
            Metadata = new Dictionary<string, string>
            {
                ["progress_percent"] = percentageComplete.ToString()
            }
        };

        lock (_lock)
        {
            var lastProgress = _activeSession.Events.LastOrDefault(e => e.Stage == UpdateLifecycleStage.InstallProgress);
            if (lastProgress != null)
            {
                _activeSession.Events.Remove(lastProgress);
            }
            _activeSession.Events.Add(evt);
        }

        UpdateEventRecorded?.Invoke(this, evt);
    }

    /// <summary>
    /// Records verification result.
    /// </summary>
    public void RecordVerificationResult(bool signatureValid, bool hashValid, string? failureReason = null)
    {
        var metadata = new Dictionary<string, string>
        {
            ["signature_valid"] = signatureValid.ToString(),
            ["hash_valid"] = hashValid.ToString()
        };

        if (!signatureValid || !hashValid)
        {
            metadata["failure_reason"] = failureReason ?? "Verification failed";
        }

        RecordEvent(UpdateLifecycleStage.VerificationComplete, failureReason, metadata);
    }

    /// <summary>
    /// Completes the current session.
    /// </summary>
    public void CompleteSession(bool failed = false, string? failureReason = null, bool rolledBack = false)
    {
        if (_activeSession == null) return;

        lock (_lock)
        {
            _activeSession.CompletedAt = DateTime.Now;
            _activeSession.IsSuccessful = !failed && !rolledBack;
            _activeSession.IsRolledBack = rolledBack;
            _activeSession.FailureReason = failureReason;
        }

        if (failed)
        {
            Logger.Error("UpdateObservabilityService", $"Update failed: {_activeSession.FailureReason}");
        }
        else if (rolledBack)
        {
            Logger.Warning("UpdateObservabilityService", $"Update rolled back: {_activeSession.TargetVersion}");
        }
        else
        {
            var duration = _activeSession.TotalDuration;
            Logger.Info("UpdateObservabilityService",
                $"Update successful: {_activeSession.TargetVersion} in {duration?.TotalSeconds:F1}s");
        }

        // Send telemetry
        SendUpdateTelemetry(_activeSession);

        UpdateSessionCompleted?.Invoke(this, _activeSession);

        _activeSession = null;
    }

    /// <summary>
    /// Gets the active session.
    /// </summary>
    public UpdateSession? GetActiveSession()
    {
        return _activeSession;
    }

    /// <summary>
    /// Gets all sessions.
    /// </summary>
    public IReadOnlyList<UpdateSession> GetSessions(TimeSpan? timeWindow = null)
    {
        var cutoff = DateTime.Now - (timeWindow ?? TimeSpan.FromDays(30));

        lock (_lock)
        {
            return _sessions.Where(s => s.StartedAt > cutoff).ToList();
        }
    }

    /// <summary>
    /// Gets metrics for a version.
    /// </summary>
    public UpdateMetrics GetMetrics(string? version = null, TimeSpan? timeWindow = null)
    {
        var sessions = GetSessions(timeWindow);

        if (!string.IsNullOrEmpty(version))
        {
            sessions = sessions.Where(s => s.TargetVersion == version).ToList();
        }

        var completedSessions = sessions.Where(s => s.CompletedAt.HasValue).ToList();
        var successfulSessions = completedSessions.Where(s => s.IsSuccessful).ToList();

        var metrics = new UpdateMetrics();

        if (completedSessions.Any())
        {
            // Calculate average times
            var downloadTimes = completedSessions
                .Select(s => s.GetStageDuration(UpdateLifecycleStage.DownloadStarted, UpdateLifecycleStage.DownloadComplete))
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            if (downloadTimes.Any())
            {
                metrics.AverageDownloadTime = TimeSpan.FromMilliseconds(
                    downloadTimes.Average(d => d.TotalMilliseconds));
            }

            var installTimes = completedSessions
                .Select(s => s.GetStageDuration(UpdateLifecycleStage.InstallStarted, UpdateLifecycleStage.InstallComplete))
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            if (installTimes.Any())
            {
                metrics.AverageInstallTime = TimeSpan.FromMilliseconds(
                    installTimes.Average(d => d.TotalMilliseconds));
            }

            var totalTimes = completedSessions
                .Select(s => s.TotalDuration)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            if (totalTimes.Any())
            {
                metrics.AverageTotalTime = TimeSpan.FromMilliseconds(
                    totalTimes.Average(d => d.TotalMilliseconds));
            }

            // Success rate
            metrics.SuccessRate = (double)successfulSessions.Count / completedSessions.Count * 100;

            // Rollback rate
            var rolledBack = completedSessions.Count(s => s.IsRolledBack);
            metrics.RollbackRate = (double)rolledBack / completedSessions.Count * 100;

            // Stage failure counts
            var failures = sessions.Where(s => !s.IsSuccessful && !s.IsRolledBack).ToList();
            metrics.StageFailureCounts = failures
                .SelectMany(s => s.Events.Where(e => e.ErrorMessage != null))
                .GroupBy(e => e.Stage)
                .ToDictionary(g => g.Key, g => g.Count());

            // Common failure reasons
            metrics.CommonFailureReasons = failures
                .Where(s => s.FailureReason != null)
                .GroupBy(s => s.FailureReason!)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList();
        }

        return metrics;
    }

    /// <summary>
    /// Gets current update progress.
    /// </summary>
    public UpdateProgress? GetCurrentProgress()
    {
        if (_activeSession == null) return null;

        var lastDownload = _activeSession.Events.LastOrDefault(e => e.Stage == UpdateLifecycleStage.DownloadProgress);
        var lastInstall = _activeSession.Events.LastOrDefault(e => e.Stage == UpdateLifecycleStage.InstallProgress);

        return new UpdateProgress
        {
            TargetVersion = _activeSession.TargetVersion,
            CurrentStage = _activeSession.Events.LastOrDefault()?.Stage ?? UpdateLifecycleStage.CheckRequested,
            DownloadProgress = lastDownload?.Metadata.TryGetValue("progress_percent", out var dp) == true
                ? double.Parse(dp)
                : 0,
            InstallProgress = lastInstall?.Metadata.TryGetValue("progress_percent", out var ip) == true
                ? double.Parse(ip)
                : 0,
            BytesTransferred = lastDownload?.BytesTransferred ?? 0,
            TotalBytes = lastDownload?.TotalBytes ?? 0,
            ElapsedTime = DateTime.Now - _activeSession.StartedAt
        };
    }

    /// <summary>
    /// Exports observability data.
    /// </summary>
    public bool ExportData(string filePath, TimeSpan? timeWindow = null)
    {
        try
        {
            var sessions = GetSessions(timeWindow);

            var export = new
            {
                ExportedAt = DateTime.Now,
                TotalSessions = sessions.Count,
                Metrics = GetMetrics(timeWindow: timeWindow),
                Sessions = sessions.Select(s => new
                {
                    s.SessionId,
                    s.SourceVersion,
                    s.TargetVersion,
                    s.StartedAt,
                    s.CompletedAt,
                    s.IsSuccessful,
                    s.IsRolledBack,
                    s.FailureReason,
                    TotalDuration = s.TotalDuration?.TotalSeconds,
                    Events = s.Events.Select(e => new
                    {
                        e.Stage,
                        e.Timestamp,
                        e.ErrorMessage,
                        e.BytesTransferred,
                        e.TotalBytes,
                        e.Metadata
                    })
                })
            };

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateObservabilityService", "Failed to export data", ex);
            return false;
        }
    }

    private void SendUpdateTelemetry(UpdateSession session)
    {
        try
        {
            var duration = session.TotalDuration;
            var success = session.IsSuccessful;
            var rolledBack = session.IsRolledBack;

            // Track with AnalyticsService
            AnalyticsService.Instance.TrackFeature("update.completed", JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["version"] = session.TargetVersion,
                ["from_version"] = session.SourceVersion,
                ["success"] = success.ToString(),
                ["rolled_back"] = rolledBack.ToString(),
                ["duration_seconds"] = duration?.TotalSeconds.ToString("F0") ?? "0"
            }));

            // Track failures separately for alerting
            if (!success && !rolledBack)
            {
                AnalyticsService.Instance.TrackFeature("update.failed", JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["version"] = session.TargetVersion,
                    ["reason"] = session.FailureReason ?? "unknown"
                }));
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("UpdateObservabilityService", $"Failed to send telemetry: {ex.Message}");
        }
    }
}

/// <summary>
/// Current update progress information.
/// </summary>
public class UpdateProgress
{
    public string TargetVersion { get; set; } = "";
    public UpdateLifecycleStage CurrentStage { get; set; }
    public double DownloadProgress { get; set; }
    public double InstallProgress { get; set; }
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public double TotalProgress => CurrentStage switch
    {
        UpdateLifecycleStage.DownloadStarted or UpdateLifecycleStage.DownloadProgress => DownloadProgress * 0.4,
        UpdateLifecycleStage.DownloadComplete or UpdateLifecycleStage.VerificationStarted or UpdateLifecycleStage.VerificationComplete => 40,
        UpdateLifecycleStage.InstallStarted or UpdateLifecycleStage.InstallProgress => 40 + InstallProgress * 0.5,
        UpdateLifecycleStage.InstallComplete => 90,
        UpdateLifecycleStage.RestartRequested or UpdateLifecycleStage.RestartComplete => 100,
        _ => 0
    };
}
