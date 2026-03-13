using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Cloud analytics service for opt-in remote analytics collection.
/// Data is only transmitted when user explicitly opts in.
/// </summary>
public class CloudAnalyticsService
{
    private static readonly HttpClient _httpClient = new();
    private readonly bool _enabled;
    private readonly string _endpointUrl;
    private readonly string _apiKey;

    public CloudAnalyticsService(bool enabled, string endpointUrl = "", string apiKey = "")
    {
        _enabled = enabled && !string.IsNullOrEmpty(endpointUrl);
        _endpointUrl = endpointUrl;
        _apiKey = apiKey;
        
        if (_enabled)
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Redball-Analytics/1.0");
            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }
        }
    }

    /// <summary>
    /// Sends anonymized usage data to cloud analytics endpoint.
    /// </summary>
    public async Task<bool> SendAnalyticsAsync(AnalyticsSummary localData)
    {
        if (!_enabled) return false;

        try
        {
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
            var response = await _httpClient.PostAsync(_endpointUrl, content);

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
