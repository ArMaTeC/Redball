namespace Redball.Core.Sync;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Background service that dispatches sync events from the outbox to the remote API.
/// Implements exponential backoff with jitter for retries and handles circuit breaker patterns.
/// </summary>
public sealed class OutboxDispatcherService : BackgroundService
{
    private readonly IOutboxStore _store;
    private readonly ISyncApi _api;
    private readonly ILogger<OutboxDispatcherService> _logger;
    private readonly TimeSpan _tickInterval;
    private readonly int _maxRetries;
    private readonly int _batchSize;

    private DateTime _lastSuccessfulSend = DateTime.MinValue;
    private int _consecutiveFailures;
    private bool _circuitOpen;
    private DateTime _circuitOpenUntil = DateTime.MinValue;

    /// <summary>
    /// Creates a new outbox dispatcher.
    /// </summary>
    public OutboxDispatcherService(
        IOutboxStore store,
        ISyncApi api,
        ILogger<OutboxDispatcherService> logger,
        TimeSpan? tickInterval = null,
        int maxRetries = 10,
        int batchSize = 50)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tickInterval = tickInterval ?? TimeSpan.FromSeconds(5);
        _maxRetries = maxRetries;
        _batchSize = batchSize;
    }

    /// <summary>
    /// Statistics for the sync health dashboard.
    /// </summary>
    public SyncHealthMetrics GetHealthMetrics()
    {
        return new SyncHealthMetrics(
            LastSuccessfulSend: _lastSuccessfulSend == DateTime.MinValue ? null : _lastSuccessfulSend,
            ConsecutiveFailures: _consecutiveFailures,
            CircuitBreakerOpen: _circuitOpen,
            CircuitBreakerOpensAt: _circuitOpen ? _circuitOpenUntil : null
        );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcher started (tick interval: {Interval}s)", _tickInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("OutboxDispatcher stopping...");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in dispatcher tick");
            }

            try
            {
                await Task.Delay(_tickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("OutboxDispatcher stopped");
    }

    /// <summary>
    /// Processes a batch of pending events.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        // Check circuit breaker
        if (_circuitOpen)
        {
            if (DateTime.UtcNow < _circuitOpenUntil)
            {
                _logger.LogDebug("Circuit breaker open, skipping tick until {ResetTime}", _circuitOpenUntil);
                return;
            }

            _logger.LogInformation("Circuit breaker half-open, attempting recovery");
            _circuitOpen = false;
        }

        // Check API health before processing
        var apiHealthy = await _api.HealthCheckAsync(ct);
        if (!apiHealthy)
        {
            _logger.LogWarning("Sync API health check failed, deferring processing");
            RecordFailure();
            return;
        }

        var batch = await _store.DequeueBatchAsync(_batchSize, DateTime.UtcNow, ct);

        if (batch.Count == 0)
        {
            // No work to do - reset consecutive failures if we've been healthy
            if (_consecutiveFailures > 0 && DateTime.UtcNow - _lastSuccessfulSend > TimeSpan.FromMinutes(5))
            {
                _logger.LogDebug("No pending events, resetting failure count");
                _consecutiveFailures = 0;
            }
            return;
        }

        _logger.LogDebug("Processing {Count} sync events", batch.Count);

        var successCount = 0;
        var failCount = 0;

        foreach (var evt in batch)
        {
            try
            {
                // Check for max retries
                if (evt.RetryCount >= _maxRetries)
                {
                    _logger.LogWarning("Event {EventId} exceeded max retries ({MaxRetries}), moving to dead letter",
                        evt.EventId, _maxRetries);
                    await _store.MarkDeadLetterAsync(evt.EventId, $"Max retries ({_maxRetries}) exceeded", ct);
                    failCount++;
                    continue;
                }

                // Send with idempotency key
                var ok = await _api.SendEventAsync(evt, ct);

                if (ok)
                {
                    await _store.MarkSucceededAsync(evt.EventId, ct);
                    _logger.LogDebug("Event {EventId} sent successfully", evt.EventId);
                    successCount++;
                    RecordSuccess();
                }
                else
                {
                    await _store.MarkFailedAsync(evt.EventId, "API returned failure", ct);
                    _logger.LogWarning("Event {EventId} failed to send (API rejected)", evt.EventId);
                    failCount++;
                    RecordFailure();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending event {EventId}", evt.EventId);
                await _store.MarkFailedAsync(evt.EventId, ex.Message, ct);
                failCount++;
                RecordFailure();
            }
        }

        _logger.LogDebug("Batch complete: {Success} succeeded, {Failed} failed", successCount, failCount);

        // Circuit breaker logic: open circuit if too many consecutive failures
        if (_consecutiveFailures >= 10)
        {
            _circuitOpen = true;
            _circuitOpenUntil = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            _logger.LogWarning("Circuit breaker opened until {ResetTime} due to {Failures} consecutive failures",
                _circuitOpenUntil, _consecutiveFailures);
        }
    }

    /// <summary>
    /// Forces an immediate sync attempt (for user-initiated sync).
    /// </summary>
    public async Task ForceSyncAsync(CancellationToken ct)
    {
        _logger.LogInformation("Force sync requested");
        _circuitOpen = false;
        _consecutiveFailures = 0;
        await TickAsync(ct);
    }

    private void RecordSuccess()
    {
        _lastSuccessfulSend = DateTime.UtcNow;
        _consecutiveFailures = 0;
    }

    private void RecordFailure()
    {
        _consecutiveFailures++;
    }
}

/// <summary>
/// Real-time metrics for sync health monitoring.
/// </summary>
public sealed record SyncHealthMetrics(
    DateTime? LastSuccessfulSend,
    int ConsecutiveFailures,
    bool CircuitBreakerOpen,
    DateTime? CircuitBreakerOpensAt);
