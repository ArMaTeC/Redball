namespace Redball.Core.Sync;

/// <summary>
/// Persistent store for sync events with durable queuing semantics.
/// </summary>
public interface IOutboxStore : IDisposable
{
    /// <summary>
    /// Enqueues a new sync event for later delivery.
    /// </summary>
    Task EnqueueAsync(SyncEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Dequeues a batch of events ready for delivery (next attempt time <= now).
    /// Returns at most <paramref name="max"/> events ordered by NextAttemptUtc.
    /// </summary>
    Task<IReadOnlyList<SyncEvent>> DequeueBatchAsync(int max, DateTime utcNow, CancellationToken ct = default);

    /// <summary>
    /// Marks an event as successfully delivered.
    /// </summary>
    Task MarkSucceededAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Marks an event as failed with updated retry state.
    /// </summary>
    Task MarkFailedAsync(Guid eventId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Permanently fails an event (max retries exceeded).
    /// </summary>
    Task MarkDeadLetterAsync(Guid eventId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Gets the current queue depth (pending + in-flight events).
    /// </summary>
    Task<int> GetQueueDepthAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the age of the oldest pending event.
    /// </summary>
    Task<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets aggregate statistics for sync health monitoring.
    /// </summary>
    Task<SyncStatistics> GetStatisticsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets events by status for diagnostics/review.
    /// </summary>
    Task<IReadOnlyList<SyncEvent>> GetEventsByStatusAsync(SyncEventStatus status, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Cancels a pending event (removes from queue).
    /// </summary>
    Task CancelEventAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Retries a dead-letter event (resets to pending with fresh retry count).
    /// </summary>
    Task RetryDeadLetterAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Purges completed events older than the retention period.
    /// </summary>
    Task<int> PurgeCompletedAsync(TimeSpan retention, CancellationToken ct = default);
}

/// <summary>
/// Aggregate statistics for sync health dashboard.
/// </summary>
public sealed record SyncStatistics(
    int TotalEvents,
    int PendingCount,
    int InFlightCount,
    int CompletedCount,
    int DeadLetterCount,
    TimeSpan? OldestPendingAge,
    DateTime? LastSuccessfulSync,
    double AvgRetriesPerEvent)
{
    public static SyncStatistics Empty => new(0, 0, 0, 0, 0, null, null, 0);
}

/// <summary>
/// Sync API abstraction for remote event delivery.
/// </summary>
public interface ISyncApi
{
    /// <summary>
    /// Sends a sync event to the remote API with idempotency key.
    /// Returns true if accepted (or already processed), false if failed/retryable.
    /// </summary>
    Task<bool> SendEventAsync(SyncEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Performs a health check on the sync API.
    /// </summary>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}
