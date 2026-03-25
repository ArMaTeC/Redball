namespace Redball.Core.Sync;

using System;

/// <summary>
/// Represents a syncable event in the local outbox store.
/// Events are durable, retryable, and idempotent.
/// </summary>
public sealed record SyncEvent
{
    /// <summary>
    /// Unique identifier for this event (used for idempotency).
    /// </summary>
    public Guid EventId { get; init; }

    /// <summary>
    /// Aggregate identifier (e.g., userId, deviceId, configId) for grouping related events.
    /// </summary>
    public string AggregateId { get; init; } = string.Empty;

    /// <summary>
    /// Version sequence within the aggregate for ordering/conflict detection.
    /// </summary>
    public long AggregateVersion { get; init; }

    /// <summary>
    /// Event type discriminator (e.g., "ConfigChanged", "SettingUpdated").
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// JSON-serialized event payload.
    /// </summary>
    public string PayloadJson { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the event was created.
    /// </summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>
    /// Number of delivery attempts made.
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// UTC timestamp when the next delivery attempt should occur.
    /// </summary>
    public DateTime NextAttemptUtc { get; init; }

    /// <summary>
    /// Current processing status of the event.
    /// </summary>
    public SyncEventStatus Status { get; init; } = SyncEventStatus.Pending;

    /// <summary>
    /// Last error message if delivery failed.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// UTC timestamp when the event was last processed.
    /// </summary>
    public DateTime? LastAttemptUtc { get; init; }

    /// <summary>
    /// Creates a new pending sync event.
    /// </summary>
    public static SyncEvent Create(
        string aggregateId,
        long aggregateVersion,
        string eventType,
        string payloadJson,
        DateTime? createdUtc = null)
    {
        var now = createdUtc ?? DateTime.UtcNow;
        return new SyncEvent
        {
            EventId = Guid.NewGuid(),
            AggregateId = aggregateId,
            AggregateVersion = aggregateVersion,
            EventType = eventType,
            PayloadJson = payloadJson,
            CreatedUtc = now,
            RetryCount = 0,
            NextAttemptUtc = now,
            Status = SyncEventStatus.Pending,
            LastError = null,
            LastAttemptUtc = null
        };
    }

    /// <summary>
    /// Creates a copy with updated retry state after a failed attempt.
    /// </summary>
    public SyncEvent WithRetryAttempt(string reason, int maxDelaySeconds = 300)
    {
        var retry = RetryCount + 1;
        var delay = TimeSpan.FromSeconds(Math.Min(maxDelaySeconds, Math.Pow(2, retry)))
                    + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 750));

        return this with
        {
            RetryCount = retry,
            NextAttemptUtc = DateTime.UtcNow + delay,
            Status = SyncEventStatus.Pending,
            LastError = reason,
            LastAttemptUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a copy marked as successfully delivered.
    /// </summary>
    public SyncEvent WithSuccess()
    {
        return this with
        {
            Status = SyncEventStatus.Completed,
            LastAttemptUtc = DateTime.UtcNow,
            LastError = null
        };
    }

    /// <summary>
    /// Creates a copy marked as failed permanently (max retries exceeded).
    /// </summary>
    public SyncEvent WithPermanentFailure(string reason)
    {
        return this with
        {
            Status = SyncEventStatus.DeadLetter,
            LastError = reason,
            LastAttemptUtc = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Processing status for a sync event.
/// </summary>
public enum SyncEventStatus
{
    /// <summary>Event is queued and ready for delivery.</summary>
    Pending = 0,

    /// <summary>Event is currently being processed.</summary>
    InFlight = 1,

    /// <summary>Event was successfully delivered.</summary>
    Completed = 2,

    /// <summary>Max retries exceeded, requires manual review.</summary>
    DeadLetter = 3,

    /// <summary>Event was cancelled by user or system.</summary>
    Cancelled = 4
}
