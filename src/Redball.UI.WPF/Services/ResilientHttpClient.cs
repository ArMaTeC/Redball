using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Offline Resilience & Sync (5.2): Exponential backoff implementations for retries over unstable connections.
/// </summary>
public class ResilientHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly int _maxRetries;
    private readonly int _baseDelayMs;

    public ResilientHttpClient(int maxRetries = 5, int baseDelayMs = 500)
    {
        _httpClient = new HttpClient();
        _maxRetries = maxRetries;
        _baseDelayMs = baseDelayMs;
    }

    /// <summary>
    /// Executes an HTTP GET request natively wrapped in an exponential backoff jitter matrix.
    /// </summary>
    public async Task<HttpResponseMessage> GetAsyncWithRetry(string url, CancellationToken cancellationToken = default)
    {
        int attempt = 0;
        var random = new Random();

        while (true)
        {
            try
            {
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                // If success or a client error (like 404), return immediately since retrying won't fix it
                if (response.IsSuccessStatusCode || ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500))
                {
                    return response;
                }

                // For 5xx Server Errors or network failures, throw to trigger the catch-block retry
                response.EnsureSuccessStatusCode();
                return response; // Fallback
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= _maxRetries)
                {
                    Logger.Error("ResilientHttpClient", $"Exhausted all {_maxRetries} retries for URL: {url}", ex);
                    throw;
                }

                attempt++;
                
                // Apply pure exponential backoff + randomized jitter (to avoid thundering herd)
                double delayMs = _baseDelayMs * Math.Pow(2, attempt) + random.Next(0, 1000);
                Logger.Info("ResilientHttpClient", $"Network error. Retrying {attempt}/{_maxRetries} in {delayMs}ms...");
                
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
            }
        }
    }
}
