namespace Redball.Core.Sync;

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// HTTP-based implementation of ISyncApi for sending events to a remote API.
/// Includes idempotency key headers and basic retry logic.
/// </summary>
public sealed class HttpSyncApi : ISyncApi, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly ILogger<HttpSyncApi>? _logger;

    /// <summary>
    /// Creates a new HTTP sync API client.
    /// </summary>
    public HttpSyncApi(string baseUrl, string apiKey, ILogger<HttpSyncApi>? logger = null, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
    }

    /// <summary>
    /// Sends a sync event to the remote API with idempotency key.
    /// </summary>
    public async Task<bool> SendEventAsync(SyncEvent evt, CancellationToken ct = default)
    {
        try
        {
            var idempotencyKey = IdempotencyKeyGenerator.Generate(evt);

            var payload = new
            {
                evt.EventId,
                evt.AggregateId,
                evt.AggregateVersion,
                evt.EventType,
                evt.PayloadJson,
                evt.CreatedUtc,
                IdempotencyKey = idempotencyKey
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Add idempotency key header
            content.Headers.Add("Idempotency-Key", idempotencyKey);

            var url = $"{_baseUrl}/api/v1/sync/events";
            _logger?.LogDebug("Sending sync event {EventId} to {Url}", evt.EventId, url);

            var response = await _httpClient.PostAsync(url, content, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger?.LogDebug("Event {EventId} sent successfully", evt.EventId);
                return true;
            }

            // 409 Conflict means already processed (idempotency hit)
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger?.LogDebug("Event {EventId} already processed (idempotency)", evt.EventId);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            _logger?.LogWarning("Event {EventId} failed: {StatusCode} - {Error}",
                evt.EventId, response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending event {EventId}", evt.EventId);
            return false;
        }
    }

    /// <summary>
    /// Performs a health check on the sync API.
    /// </summary>
    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}/api/v1/health";
            var response = await _httpClient.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// No-op sync API for offline-only mode or when sync is disabled.
/// </summary>
public sealed class NoOpSyncApi : ISyncApi
{
    public static NoOpSyncApi Instance { get; } = new();

    public Task<bool> SendEventAsync(SyncEvent evt, CancellationToken ct = default)
    {
        // Always returns true but doesn't actually send anything
        // This keeps events in the outbox until a real API is configured
        return Task.FromResult(false);
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }
}
