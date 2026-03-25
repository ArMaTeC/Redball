namespace Redball.Core.Sync;

using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Generates idempotency keys for sync events to ensure exactly-once delivery semantics.
/// </summary>
public static class IdempotencyKeyGenerator
{
    /// <summary>
    /// Creates an idempotency key from a sync event.
    /// Uses EventId for primary idempotency with optional payload verification.
    /// </summary>
    public static string Generate(SyncEvent evt)
    {
        // Primary key is the EventId - this ensures we don't process the same event twice
        // even if the API receives duplicate requests
        return $"redball:{evt.EventId:N}";
    }

    /// <summary>
    /// Creates a composite idempotency key including payload hash for verification.
    /// Use this when you want to detect payload changes between retries.
    /// </summary>
    public static string GenerateWithPayloadHash(SyncEvent evt)
    {
        var payloadHash = ComputeHash(evt.PayloadJson);
        return $"redball:{evt.EventId:N}:{payloadHash}";
    }

    /// <summary>
    /// Creates an aggregate-scoped idempotency key for operations that should be
    /// idempotent within an aggregate (e.g., "only process latest version").
    /// </summary>
    public static string GenerateAggregateScoped(SyncEvent evt)
    {
        return $"redball:{evt.AggregateId}:{evt.AggregateVersion}";
    }

    /// <summary>
    /// Validates that a received idempotency key matches the expected format.
    /// </summary>
    public static bool IsValidFormat(string key)
    {
        return !string.IsNullOrEmpty(key) && key.StartsWith("redball:", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the EventId from an idempotency key.
    /// </summary>
    public static bool TryExtractEventId(string key, out Guid eventId)
    {
        eventId = Guid.Empty;

        if (!IsValidFormat(key))
            return false;

        var parts = key.Split(':');
        if (parts.Length < 2)
            return false;

        return Guid.TryParseExact(parts[1], "N", out eventId);
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..8]; // First 8 chars for brevity
    }
}
