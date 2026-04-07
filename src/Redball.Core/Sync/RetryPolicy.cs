using System;

namespace Redball.Core.Sync;

/// <summary>
/// Configurable retry policy with exponential backoff strategy.
/// Eliminates magic numbers and provides centralized retry configuration.
/// </summary>
public class RetryPolicy
{
    /// <summary>
    /// Maximum number of retry attempts before giving up.
    /// </summary>
    public int MaxRetries { get; set; } = 10;

    /// <summary>
    /// Initial delay before first retry.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum delay between retries (caps exponential growth).
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Backoff multiplier for exponential backoff (default: 2.0 for doubling).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Whether to add random jitter to prevent thundering herd.
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Maximum jitter percentage (0.0 to 1.0, default 0.2 = ±20%).
    /// </summary>
    public double JitterFactor { get; set; } = 0.2;

    /// <summary>
    /// Calculates the delay for a given retry attempt using exponential backoff.
    /// </summary>
    /// <param name="retryCount">Current retry attempt (0-based)</param>
    /// <returns>Delay duration before next retry</returns>
    public TimeSpan CalculateDelay(int retryCount)
    {
        if (retryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryCount), "Retry count must be non-negative");
        }

        // Exponential backoff: delay = initialDelay * (multiplier ^ retryCount)
        var exponentialDelay = InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, retryCount);
        
        // Cap at max delay
        var cappedDelay = Math.Min(exponentialDelay, MaxDelay.TotalMilliseconds);

        // Apply jitter if enabled
        if (UseJitter)
        {
            var jitterRange = cappedDelay * JitterFactor;
            var jitter = (Random.Shared.NextDouble() * 2 - 1) * jitterRange; // ±jitterRange
            cappedDelay += jitter;
        }

        // Ensure non-negative
        cappedDelay = Math.Max(0, cappedDelay);

        return TimeSpan.FromMilliseconds(cappedDelay);
    }

    /// <summary>
    /// Determines if another retry should be attempted.
    /// </summary>
    /// <param name="retryCount">Current retry attempt (0-based)</param>
    /// <returns>True if retry should be attempted, false if max retries exceeded</returns>
    public bool ShouldRetry(int retryCount)
    {
        return retryCount < MaxRetries;
    }

    /// <summary>
    /// Creates a default retry policy with conservative settings.
    /// </summary>
    public static RetryPolicy Default => new()
    {
        MaxRetries = 10,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromMinutes(5),
        BackoffMultiplier = 2.0,
        UseJitter = true,
        JitterFactor = 0.2
    };

    /// <summary>
    /// Creates an aggressive retry policy for time-sensitive operations.
    /// </summary>
    public static RetryPolicy Aggressive => new()
    {
        MaxRetries = 5,
        InitialDelay = TimeSpan.FromMilliseconds(500),
        MaxDelay = TimeSpan.FromSeconds(30),
        BackoffMultiplier = 1.5,
        UseJitter = true,
        JitterFactor = 0.1
    };

    /// <summary>
    /// Creates a patient retry policy for background operations.
    /// </summary>
    public static RetryPolicy Patient => new()
    {
        MaxRetries = 20,
        InitialDelay = TimeSpan.FromSeconds(5),
        MaxDelay = TimeSpan.FromMinutes(30),
        BackoffMultiplier = 2.0,
        UseJitter = true,
        JitterFactor = 0.3
    };
}
