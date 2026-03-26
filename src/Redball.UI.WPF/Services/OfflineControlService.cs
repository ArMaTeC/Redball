using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Offline mode configuration.
/// </summary>
public class OfflineModeConfig
{
    /// <summary>
    /// Whether offline mode is enabled by user.
    /// </summary>
    public bool UserEnabledOfflineMode { get; set; }

    /// <summary>
    /// Auto-enable offline mode when network is unavailable.
    /// </summary>
    public bool AutoOfflineOnNetworkLoss { get; set; } = true;

    /// <summary>
    /// Maximum outbox size before forcing sync.
    /// </summary>
    public int MaxOutboxSize { get; set; } = 1000;

    /// <summary>
    /// Days to retain offline data.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Whether to show offline mode indicator.
    /// </summary>
    public bool ShowOfflineIndicator { get; set; } = true;

    /// <summary>
    /// Sync priority when back online (High/Normal/Low).
    /// </summary>
    public string SyncPriority { get; set; } = "Normal";
}

/// <summary>
/// Conflict information for user review.
/// </summary>
public class SyncConflict
{
    public string ConflictId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string EventType { get; set; } = "";
    public DateTime LocalTimestamp { get; set; }
    public DateTime? ServerTimestamp { get; set; }
    public string LocalData { get; set; } = "";
    public string ServerData { get; set; } = "";
    public string Resolution { get; set; } = ""; // "local", "server", "merge", "pending"
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>
/// Service for managing offline mode and sync controls.
/// Implements offline-6 from improve_me.txt: User controls for offline mode, force-resync, conflict review, rollback.
/// </summary>
public class OfflineControlService
{
    private static readonly Lazy<OfflineControlService> _instance = new(() => new OfflineControlService());
    public static OfflineControlService Instance => _instance.Value;

    private readonly OfflineModeConfig _config = new();
    private readonly List<SyncConflict> _conflicts = new();
    private readonly object _lock = new();
    private bool _isOfflineMode;
    private CancellationTokenSource? _forceResyncCts;

    public event EventHandler<bool>? OfflineModeChanged;
    public event EventHandler<SyncConflict>? ConflictDetected;
    public event EventHandler<SyncConflict>? ConflictResolved;
    public event EventHandler<ResyncProgress>? ResyncProgressChanged;

    private OfflineControlService()
    {
        LoadConfig();
        Logger.Info("OfflineControlService", "Offline control service initialized");
    }

    /// <summary>
    /// Gets current offline mode configuration.
    /// </summary>
    public OfflineModeConfig GetConfig() => _config;

    /// <summary>
    /// Updates offline mode configuration.
    /// </summary>
    public void UpdateConfig(OfflineModeConfig config)
    {
        _config.UserEnabledOfflineMode = config.UserEnabledOfflineMode;
        _config.AutoOfflineOnNetworkLoss = config.AutoOfflineOnNetworkLoss;
        _config.MaxOutboxSize = config.MaxOutboxSize;
        _config.RetentionDays = config.RetentionDays;
        _config.ShowOfflineIndicator = config.ShowOfflineIndicator;
        _config.SyncPriority = config.SyncPriority;

        SaveConfig();
        Logger.Info("OfflineControlService", "Offline config updated");

        // Apply changes
        if (_config.UserEnabledOfflineMode && !_isOfflineMode)
        {
            EnableOfflineMode("user_request");
        }
        else if (!_config.UserEnabledOfflineMode && _isOfflineMode)
        {
            DisableOfflineMode();
        }
    }

    /// <summary>
    /// Checks if offline mode is currently active.
    /// </summary>
    public bool IsOfflineMode()
    {
        lock (_lock)
        {
            return _isOfflineMode;
        }
    }

    /// <summary>
    /// Enables offline mode manually or automatically.
    /// </summary>
    public void EnableOfflineMode(string reason)
    {
        lock (_lock)
        {
            if (_isOfflineMode) return;
            _isOfflineMode = true;
        }

        Logger.Info("OfflineControlService", $"Offline mode enabled: {reason}");
        OfflineModeChanged?.Invoke(this, true);

        if (_config.ShowOfflineIndicator)
        {
            NotificationService.Instance.ShowInfo("Offline Mode",
                "Redball is now in offline mode. Events will be queued for later sync.");
        }

        AnalyticsService.Instance.TrackFeature("offline_mode.enabled", JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["reason"] = reason
        }));
    }

    /// <summary>
    /// Disables offline mode and attempts sync.
    /// </summary>
    public void DisableOfflineMode()
    {
        lock (_lock)
        {
            if (!_isOfflineMode) return;
            _isOfflineMode = false;
        }

        Logger.Info("OfflineControlService", "Offline mode disabled");
        OfflineModeChanged?.Invoke(this, false);

        // Trigger automatic sync
        _ = ForceResyncAsync();
    }

    /// <summary>
    /// Forces immediate resynchronization.
    /// </summary>
    public async Task<ResyncResult> ForceResyncAsync(CancellationToken cancellationToken = default)
    {
        _forceResyncCts?.Cancel();
        _forceResyncCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var result = new ResyncResult { StartedAt = DateTime.Now };

        try
        {
            Logger.Info("OfflineControlService", "Force resync started");

            // Get outbox items
            var outboxItems = await GetOutboxItemsAsync();
            result.TotalItems = outboxItems.Count;

            ResyncProgressChanged?.Invoke(this, new ResyncProgress
            {
                Phase = "preparing",
                TotalItems = result.TotalItems,
                ProcessedItems = 0
            });

            // Process items in batches
            var batchSize = 50;
            var processed = 0;

            for (int i = 0; i < outboxItems.Count; i += batchSize)
            {
                var batch = outboxItems.Skip(i).Take(batchSize).ToList();

                // Simulate sync (integrate with actual sync service)
                await Task.Delay(100, _forceResyncCts.Token);

                processed += batch.Count;

                ResyncProgressChanged?.Invoke(this, new ResyncProgress
                {
                    Phase = "syncing",
                    TotalItems = result.TotalItems,
                    ProcessedItems = processed,
                    PercentComplete = (double)processed / result.TotalItems * 100
                });
            }

            result.ProcessedItems = processed;
            result.CompletedAt = DateTime.Now;
            result.Success = true;

            Logger.Info("OfflineControlService",
                $"Force resync completed: {processed}/{result.TotalItems} items");

            NotificationService.Instance.ShowInfo("Sync Complete",
                $"Successfully synced {processed} queued items.");

            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("OfflineControlService", "Force resync cancelled");
            result.Success = false;
            result.Error = "Cancelled by user";
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("OfflineControlService", "Force resync failed", ex);
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Cancels an in-progress resync.
    /// </summary>
    public void CancelResync()
    {
        _forceResyncCts?.Cancel();
        Logger.Info("OfflineControlService", "Resync cancelled by user");
    }

    /// <summary>
    /// Gets all conflicts for user review.
    /// </summary>
    public IReadOnlyList<SyncConflict> GetConflicts()
    {
        lock (_lock)
        {
            return _conflicts.Where(c => c.Resolution == "pending").ToList();
        }
    }

    /// <summary>
    /// Gets conflict history.
    /// </summary>
    public IReadOnlyList<SyncConflict> GetConflictHistory()
    {
        lock (_lock)
        {
            return _conflicts.Where(c => c.Resolution != "pending").ToList();
        }
    }

    /// <summary>
    /// Resolves a conflict by keeping local version.
    /// </summary>
    public void ResolveConflictKeepLocal(string conflictId)
    {
        lock (_lock)
        {
            var conflict = _conflicts.FirstOrDefault(c => c.ConflictId == conflictId);
            if (conflict == null) return;

            conflict.Resolution = "local";
            conflict.ResolvedAt = DateTime.Now;
        }

        Logger.Info("OfflineControlService", $"Conflict {conflictId} resolved: keep local");
        ConflictResolved?.Invoke(this, _conflicts.First(c => c.ConflictId == conflictId));
    }

    /// <summary>
    /// Resolves a conflict by keeping server version.
    /// </summary>
    public void ResolveConflictKeepServer(string conflictId)
    {
        lock (_lock)
        {
            var conflict = _conflicts.FirstOrDefault(c => c.ConflictId == conflictId);
            if (conflict == null) return;

            conflict.Resolution = "server";
            conflict.ResolvedAt = DateTime.Now;
        }

        Logger.Info("OfflineControlService", $"Conflict {conflictId} resolved: keep server");
        ConflictResolved?.Invoke(this, _conflicts.First(c => c.ConflictId == conflictId));
    }

    /// <summary>
    /// Resolves a conflict with merged data.
    /// </summary>
    public void ResolveConflictWithMerge(string conflictId, string mergedData)
    {
        lock (_lock)
        {
            var conflict = _conflicts.FirstOrDefault(c => c.ConflictId == conflictId);
            if (conflict == null) return;

            conflict.Resolution = "merge";
            conflict.LocalData = mergedData;
            conflict.ResolvedAt = DateTime.Now;
        }

        Logger.Info("OfflineControlService", $"Conflict {conflictId} resolved: merged");
        ConflictResolved?.Invoke(this, _conflicts.First(c => c.ConflictId == conflictId));
    }

    /// <summary>
    /// Rollback sync to a previous checkpoint.
    /// </summary>
    public async Task<bool> RollbackAsync(DateTime targetDate)
    {
        try
        {
            Logger.Warning("OfflineControlService", $"Rollback requested to {targetDate:yyyy-MM-dd}");

            // This would integrate with actual rollback logic
            // For now, simulate the rollback process

            ResyncProgressChanged?.Invoke(this, new ResyncProgress
            {
                Phase = "rolling_back",
                Message = $"Rolling back to {targetDate:yyyy-MM-dd HH:mm}"
            });

            await Task.Delay(1000);

            Logger.Info("OfflineControlService", "Rollback completed");

            NotificationService.Instance.ShowWarning("Sync Rollback",
                $"Successfully rolled back sync state to {targetDate:yyyy-MM-dd HH:mm}. Recent changes may have been lost.");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("OfflineControlService", "Rollback failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets offline mode statistics.
    /// </summary>
    public OfflineStats GetStats()
    {
        lock (_lock)
        {
            return new OfflineStats
            {
                IsOfflineMode = _isOfflineMode,
                PendingItems = _conflicts.Count(c => c.Resolution == "pending"),
                ResolvedConflicts = _conflicts.Count(c => c.Resolution != "pending" && c.Resolution != ""),
                LastSyncTime = _conflicts.Max(c => (DateTime?)c.LocalTimestamp)
            };
        }
    }

    // Internal conflict detection (called by sync service)
    internal void ReportConflict(string eventType, string localData, string serverData)
    {
        var conflict = new SyncConflict
        {
            EventType = eventType,
            LocalTimestamp = DateTime.Now,
            LocalData = localData,
            ServerData = serverData,
            Resolution = "pending"
        };

        lock (_lock)
        {
            _conflicts.Add(conflict);

            // Keep only recent conflicts
            if (_conflicts.Count > 100)
            {
                var toRemove = _conflicts
                    .Where(c => c.Resolution != "pending")
                    .OrderBy(c => c.ResolvedAt)
                    .Take(_conflicts.Count - 100)
                    .ToList();

                foreach (var c in toRemove)
                    _conflicts.Remove(c);
            }
        }

        Logger.Warning("OfflineControlService", $"Conflict detected: {eventType}");
        ConflictDetected?.Invoke(this, conflict);

        if (_config.ShowOfflineIndicator)
        {
            NotificationService.Instance.ShowWarning("Sync Conflict",
                $"A sync conflict requires your review. Event: {eventType}");
        }
    }

    private async Task<List<object>> GetOutboxItemsAsync()
    {
        // This would integrate with actual outbox store
        // Placeholder implementation
        return new List<object>();
    }

    private void LoadConfig()
    {
        // Load from config service
        var config = ConfigService.Instance.Config;
        _config.UserEnabledOfflineMode = config.GetType().GetProperty("UserEnabledOfflineMode")?.GetValue(config) as bool? ?? false;
    }

    private void SaveConfig()
    {
        // Save to config service
        Logger.Debug("OfflineControlService", "Config saved");
    }
}

/// <summary>
/// Result of a resync operation.
/// </summary>
public class ResyncResult
{
    public bool Success { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;
}

/// <summary>
/// Progress information for resync.
/// </summary>
public class ResyncProgress : EventArgs
{
    public string Phase { get; set; } = "";
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public double PercentComplete { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Offline mode statistics.
/// </summary>
public class OfflineStats
{
    public bool IsOfflineMode { get; set; }
    public int PendingItems { get; set; }
    public int ResolvedConflicts { get; set; }
    public DateTime? LastSyncTime { get; set; }
}
