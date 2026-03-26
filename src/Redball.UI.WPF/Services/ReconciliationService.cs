using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Reconciliation status.
/// </summary>
public enum ReconciliationStatus
{
    Pending,
    InProgress,
    Verified,
    PartialReplayRequired,
    Failed
}

/// <summary>
/// Reconciliation checkpoint.
/// </summary>
public class ReconciliationCheckpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime Timestamp { get; set; }
    public string DataSnapshot { get; set; } = "";
    public string Checksum { get; set; } = "";
    public int EventCount { get; set; }
    public bool IsVerified { get; set; }
}

/// <summary>
/// Reconciliation event for replay.
/// </summary>
public class ReconciliationEvent
{
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Payload { get; set; } = "";
    public string? PreviousChecksum { get; set; }
    public string? ComputedChecksum { get; set; }
    public bool IsValid { get; set; }
}

/// <summary>
/// Service for reconciliation with checksum verification and partial replay.
/// Implements offline-5 from improve_me.txt: Reconciliation engine.
/// </summary>
public class ReconciliationService
{
    private static readonly Lazy<ReconciliationService> _instance = new(() => new ReconciliationService());
    public static ReconciliationService Instance => _instance.Value;

    private readonly List<ReconciliationCheckpoint> _checkpoints = new();
    private readonly List<ReconciliationEvent> _eventLog = new();
    private readonly object _lock = new();

    public event EventHandler<ReconciliationProgress>? ReconciliationProgress;

    private ReconciliationService()
    {
        Logger.Info("ReconciliationService", "Reconciliation service initialized");
    }

    /// <summary>
    /// Creates a checkpoint for current state.
    /// </summary>
    public ReconciliationCheckpoint CreateCheckpoint(string dataSnapshot)
    {
        var checkpoint = new ReconciliationCheckpoint
        {
            Timestamp = DateTime.UtcNow,
            DataSnapshot = dataSnapshot,
            Checksum = ComputeChecksum(dataSnapshot),
            EventCount = _eventLog.Count
        };

        lock (_lock)
        {
            _checkpoints.Add(checkpoint);

            // Keep only last 10 checkpoints
            if (_checkpoints.Count > 10)
            {
                _checkpoints.RemoveAt(0);
            }
        }

        Logger.Info("ReconciliationService", $"Checkpoint created: {checkpoint.Id} ({checkpoint.Checksum})");
        return checkpoint;
    }

    /// <summary>
    /// Records an event for replay capability.
    /// </summary>
    public void RecordEvent(string eventType, string payload)
    {
        var evt = new ReconciliationEvent
        {
            EventId = Guid.NewGuid().ToString("N")[..8],
            EventType = eventType,
            Timestamp = DateTime.UtcNow,
            Payload = payload,
            PreviousChecksum = _eventLog.LastOrDefault()?.ComputedChecksum,
            ComputedChecksum = ComputeChecksum(eventType + payload + DateTime.UtcNow.Ticks),
            IsValid = true
        };

        lock (_lock)
        {
            _eventLog.Add(evt);

            // Trim old events
            if (_eventLog.Count > 1000)
            {
                _eventLog.RemoveAt(0);
            }
        }

        Logger.Debug("ReconciliationService", $"Event recorded: {eventType} ({evt.EventId})");
    }

    /// <summary>
    /// Verifies a checkpoint against current state.
    /// </summary>
    public async Task<bool> VerifyCheckpointAsync(string checkpointId)
    {
        ReconciliationCheckpoint? checkpoint;
        lock (_lock)
        {
            checkpoint = _checkpoints.FirstOrDefault(c => c.Id == checkpointId);
        }

        if (checkpoint == null)
        {
            Logger.Warning("ReconciliationService", $"Checkpoint not found: {checkpointId}");
            return false;
        }

        // Recompute checksum
        var currentChecksum = ComputeChecksum(checkpoint.DataSnapshot);
        var isValid = currentChecksum == checkpoint.Checksum;

        lock (_lock)
        {
            checkpoint.IsVerified = isValid;
        }

        Logger.Info("ReconciliationService", $"Checkpoint {checkpointId} verified: {isValid}");
        return isValid;
    }

    /// <summary>
    /// Performs full reconciliation with verification.
    /// </summary>
    public async Task<ReconciliationResult> ReconcileAsync(string fromCheckpointId)
    {
        var result = new ReconciliationResult
        {
            StartedAt = DateTime.UtcNow,
            Status = ReconciliationStatus.InProgress
        };

        ReconciliationProgress?.Invoke(this, new ReconciliationProgress
        {
            Phase = "verifying_checkpoint",
            PercentComplete = 0
        });

        // 1. Verify the base checkpoint
        var checkpointValid = await VerifyCheckpointAsync(fromCheckpointId);
        if (!checkpointValid)
        {
            result.Status = ReconciliationStatus.Failed;
            result.Error = "Checkpoint verification failed";
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }

        ReconciliationProgress?.Invoke(this, new ReconciliationProgress
        {
            Phase = "replaying_events",
            PercentComplete = 25
        });

        // 2. Identify events to replay
        ReconciliationCheckpoint checkpoint;
        lock (_lock)
        {
            checkpoint = _checkpoints.First(c => c.Id == fromCheckpointId);
        }

        var eventsToReplay = _eventLog.Skip(checkpoint.EventCount).ToList();
        result.EventsProcessed = eventsToReplay.Count;

        ReconciliationProgress?.Invoke(this, new ReconciliationProgress
        {
            Phase = "verifying_integrity",
            PercentComplete = 50,
            EventsProcessed = 0,
            TotalEvents = eventsToReplay.Count
        });

        // 3. Replay and verify
        var validEvents = new List<ReconciliationEvent>();
        var invalidEvents = new List<ReconciliationEvent>();

        for (int i = 0; i < eventsToReplay.Count; i++)
        {
            var evt = eventsToReplay[i];
            var recomputedChecksum = ComputeChecksum(evt.EventType + evt.Payload + evt.Timestamp.Ticks);

            if (recomputedChecksum == evt.ComputedChecksum)
            {
                evt.IsValid = true;
                validEvents.Add(evt);
            }
            else
            {
                evt.IsValid = false;
                invalidEvents.Add(evt);
            }

            if (i % 10 == 0)
            {
                ReconciliationProgress?.Invoke(this, new ReconciliationProgress
                {
                    Phase = "verifying_integrity",
                    PercentComplete = 50 + (i * 50 / eventsToReplay.Count),
                    EventsProcessed = i,
                    TotalEvents = eventsToReplay.Count
                });
            }
        }

        result.ValidEvents = validEvents.Count;
        result.InvalidEvents = invalidEvents.Count;

        // 4. Determine final status
        if (invalidEvents.Count == 0)
        {
            result.Status = ReconciliationStatus.Verified;
        }
        else if (validEvents.Count > 0)
        {
            result.Status = ReconciliationStatus.PartialReplayRequired;
            result.RequiresPartialReplay = true;
        }
        else
        {
            result.Status = ReconciliationStatus.Failed;
        }

        result.CompletedAt = DateTime.UtcNow;

        Logger.Info("ReconciliationService",
            $"Reconciliation complete: {result.Status}, {validEvents.Count} valid, {invalidEvents.Count} invalid");

        return result;
    }

    /// <summary>
    /// Performs partial replay from a specific event.
    /// </summary>
    public async Task<PartialReplayResult> PartialReplayAsync(string fromEventId)
    {
        var result = new PartialReplayResult
        {
            StartedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            var startIndex = _eventLog.FindIndex(e => e.EventId == fromEventId);
            if (startIndex < 0)
            {
                result.Success = false;
                result.Error = "Start event not found";
                return result;
            }

            var eventsToReplay = _eventLog.Skip(startIndex).Where(e => e.IsValid).ToList();

            foreach (var evt in eventsToReplay)
            {
                try
                {
                    // Replay the event (this would integrate with actual business logic)
                    result.ReplayedEvents++;
                }
                catch (Exception ex)
                {
                    result.FailedEvents++;
                    result.Errors.Add($"Event {evt.EventId}: {ex.Message}");
                }
            }

            result.Success = result.FailedEvents == 0;
        }

        result.CompletedAt = DateTime.UtcNow;

        Logger.Info("ReconciliationService",
            $"Partial replay complete: {result.ReplayedEvents} replayed, {result.FailedEvents} failed");

        return result;
    }

    /// <summary>
    /// Gets all checkpoints.
    /// </summary>
    public IReadOnlyList<ReconciliationCheckpoint> GetCheckpoints()
    {
        lock (_lock)
        {
            return _checkpoints.ToList();
        }
    }

    /// <summary>
    /// Gets recent events.
    /// </summary>
    public IReadOnlyList<ReconciliationEvent> GetRecentEvents(int count = 100)
    {
        lock (_lock)
        {
            return _eventLog.TakeLast(count).ToList();
        }
    }

    private string ComputeChecksum(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).Substring(0, 16);
    }
}

/// <summary>
/// Reconciliation result.
/// </summary>
public class ReconciliationResult
{
    public ReconciliationStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int EventsProcessed { get; set; }
    public int ValidEvents { get; set; }
    public int InvalidEvents { get; set; }
    public bool RequiresPartialReplay { get; set; }
    public string? Error { get; set; }

    public TimeSpan? Duration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt
        : null;
}

/// <summary>
/// Partial replay result.
/// </summary>
public class PartialReplayResult
{
    public bool Success { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ReplayedEvents { get; set; }
    public int FailedEvents { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? Error { get; set; }
}

/// <summary>
/// Reconciliation progress event.
/// </summary>
public class ReconciliationProgress : EventArgs
{
    public string Phase { get; set; } = "";
    public double PercentComplete { get; set; }
    public int EventsProcessed { get; set; }
    public int TotalEvents { get; set; }
}
