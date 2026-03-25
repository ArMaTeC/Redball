using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Cloud analytics service for opt-in remote analytics collection.
/// Data is only transmitted when user explicitly opts in.
/// API keys are retrieved securely from the SecretManagerService, never stored in config.
/// </summary>
public class CloudAnalyticsService : IDisposable
{
    private static readonly HttpClient _httpClient = new();
    private readonly SecretManagerService _secretManager;
    private readonly bool _enabled;
    private bool _disposed;

    /// <summary>
    /// Creates a new CloudAnalyticsService.
    /// </summary>
    /// <param name="enabled">Whether cloud analytics is enabled by user preference.</param>
    /// <param name="secretManager">Secret manager for retrieving API credentials securely.</param>
    public CloudAnalyticsService(bool enabled, SecretManagerService secretManager)
    {
        _enabled = enabled;
        _secretManager = secretManager ?? throw new ArgumentNullException(nameof(secretManager));

        if (_enabled)
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Redball-Analytics/1.0");
            Logger.Info("CloudAnalytics", $"Cloud analytics service initialized (enabled={enabled})");
        }
        else
        {
            Logger.Debug("CloudAnalytics", "Cloud analytics service initialized (disabled)");
        }
    }

    /// <summary>
    /// Configures the HTTP client with the current API credentials from secure storage.
    /// </summary>
    private async Task<bool> ConfigureCredentialsAsync(CancellationToken ct)
    {
        try
        {
            // Retrieve endpoint and API key from secure storage
            var endpoint = await _secretManager.GetSecretAsync(
                SecretManagerService.KnownSecrets.CloudAnalyticsEndpoint, ct);
            var apiKey = await _secretManager.GetSecretAsync(
                SecretManagerService.KnownSecrets.CloudAnalyticsApiKey, ct);

            if (string.IsNullOrEmpty(endpoint))
            {
                Logger.Warning("CloudAnalytics", "Analytics endpoint not configured in secure storage");
                return false;
            }

            // Update endpoint
            _httpClient.BaseAddress = new Uri(endpoint.TrimEnd('/'));

            // Update or remove API key header
            if (_httpClient.DefaultRequestHeaders.Contains("X-API-Key"))
            {
                _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
            }

            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("CloudAnalytics", "Failed to configure credentials from secure storage", ex);
            return false;
        }
    }

    /// <summary>
    /// Sends anonymized usage data to cloud analytics endpoint.
    /// </summary>
    public async Task<bool> SendAnalyticsAsync(AnalyticsSummary localData, CancellationToken ct = default)
    {
        if (!_enabled) return false;

        try
        {
            // Configure credentials from secure storage
            if (!await ConfigureCredentialsAsync(ct))
            {
                Logger.Warning("CloudAnalytics", "Cannot send analytics - credentials not available");
                return false;
            }

            // Create anonymized payload
            var payload = new
            {
                Timestamp = DateTime.UtcNow,
                Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                SessionCount = localData.TotalSessions,
                TotalUsageMinutes = (int)localData.TotalUsageTime.TotalMinutes,
                TopFeatures = localData.TopFeatures,
                // Cohort data for retention analysis
                CohortMonth = localData.FirstSeen.ToString("yyyy-MM"),
                DaysSinceFirstUse = (DateTime.UtcNow - localData.FirstSeen).Days
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/analytics", content, ct);

            if (response.IsSuccessStatusCode)
            {
                Logger.Info("CloudAnalytics", "Analytics data sent successfully");
                return true;
            }
            else
            {
                Logger.Warning("CloudAnalytics", $"Failed to send analytics: {response.StatusCode}");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Debug("CloudAnalytics", "Analytics send was cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("CloudAnalytics", "Error sending analytics", ex);
            return false;
        }
    }

    /// <summary>
    /// Performs cohort analysis for retention tracking.
    /// </summary>
    public CohortAnalysis PerformCohortAnalysis(AnalyticsData data)
    {
        var analysis = new CohortAnalysis();
        
        try
        {
            // Calculate cohort month
            var cohortMonth = new DateTime(data.FirstSeen.Year, data.FirstSeen.Month, 1);
            analysis.CohortMonth = cohortMonth.ToString("yyyy-MM");
            
            // Calculate retention buckets
            var daysSinceFirst = (DateTime.UtcNow - data.FirstSeen).TotalDays;
            
            analysis.RetentionDay1 = data.TotalSessions > 0;
            analysis.RetentionDay7 = daysSinceFirst >= 7 && data.TotalSessions > 1;
            analysis.RetentionDay30 = daysSinceFirst >= 30 && data.TotalSessions > 2;
            analysis.RetentionDay90 = daysSinceFirst >= 90 && data.TotalSessions > 5;
            
            // Calculate engagement score (0-100)
            var sessionFrequency = daysSinceFirst > 0 
                ? data.TotalSessions / daysSinceFirst * 30 
                : 0;
            analysis.EngagementScore = Math.Min(100, (int)(sessionFrequency * 10));
            
            // Calculate lifetime value proxy (usage hours)
            analysis.TotalUsageHours = (int)data.TotalSessionDuration.TotalHours;
            
            Logger.Debug("CloudAnalytics", 
                $"Cohort analysis: {analysis.CohortMonth}, Engagement: {analysis.EngagementScore}");
        }
        catch (Exception ex)
        {
            Logger.Error("CloudAnalytics", "Error performing cohort analysis", ex);
        }

        return analysis;
    }

    /// <summary>
    /// Checks whether cloud analytics can be enabled (credentials are configured).
    /// </summary>
    public async Task<bool> CanEnableAsync(CancellationToken ct = default)
    {
        try
        {
            var endpoint = await _secretManager.GetSecretAsync(
                SecretManagerService.KnownSecrets.CloudAnalyticsEndpoint, ct);
            return !string.IsNullOrEmpty(endpoint);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the current configuration status for diagnostics.
    /// </summary>
    public async Task<CloudAnalyticsConfigStatus> GetConfigStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var endpointExists = await _secretManager.SecretExistsAsync(
                SecretManagerService.KnownSecrets.CloudAnalyticsEndpoint, ct);
            var apiKeyExists = await _secretManager.SecretExistsAsync(
                SecretManagerService.KnownSecrets.CloudAnalyticsApiKey, ct);

            return new CloudAnalyticsConfigStatus
            {
                IsEnabled = _enabled,
                EndpointConfigured = endpointExists,
                ApiKeyConfigured = apiKeyExists,
                CanSend = _enabled && endpointExists,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Logger.Error("CloudAnalytics", "Failed to get config status", ex);
            return new CloudAnalyticsConfigStatus
            {
                IsEnabled = _enabled,
                EndpointConfigured = false,
                ApiKeyConfigured = false,
                CanSend = false,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Logger.Debug("CloudAnalytics", "Disposed");
        }
    }
}

/// <summary>
/// Cloud analytics configuration status for diagnostics.
/// </summary>
public class CloudAnalyticsConfigStatus
{
    public bool IsEnabled { get; set; }
    public bool EndpointConfigured { get; set; }
    public bool ApiKeyConfigured { get; set; }
    public bool CanSend { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Cohort retention analysis results.
/// </summary>
public class CohortAnalysis
{
    public string CohortMonth { get; set; } = "";
    public bool RetentionDay1 { get; set; }
    public bool RetentionDay7 { get; set; }
    public bool RetentionDay30 { get; set; }
    public bool RetentionDay90 { get; set; }
    public int EngagementScore { get; set; }
    public int TotalUsageHours { get; set; }
    
    public double RetentionRate => CalculateRetentionRate();
    
    private double CalculateRetentionRate()
    {
        var checkpoints = new[] { RetentionDay1, RetentionDay7, RetentionDay30, RetentionDay90 };
        var passed = checkpoints.Count(x => x);
        return passed * 100.0 / checkpoints.Length;
    }
}
