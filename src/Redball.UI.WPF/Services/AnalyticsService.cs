using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Analytics service for tracking feature usage, user engagement, funnels, and retention.
/// All data is stored locally; no cloud transmission without explicit opt-in.
/// </summary>
public class AnalyticsService
{
    private static readonly string AnalyticsFile = Path.Combine(
        AppContext.BaseDirectory, "analytics.json");
    
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private AnalyticsData _data = new();
    private Timer? _flushTimer;
    private readonly bool _enabled;

    public AnalyticsService(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
        {
            Load();
            // Auto-flush every 5 minutes
            _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }
    }

    /// <summary>
    /// Track a feature usage event
    /// </summary>
    public void TrackFeature(string featureName, string? context = null)
    {
        if (!_enabled) return;

        _lock.EnterWriteLock();
        try
        {
            var feature = _data.Features.GetValueOrDefault(featureName) ?? new FeatureStats();
            feature.UsageCount++;
            feature.LastUsed = DateTime.UtcNow;
            if (context != null)
            {
                feature.Contexts[context] = feature.Contexts.GetValueOrDefault(context) + 1;
            }
            _data.Features[featureName] = feature;
            _data.LastUpdated = DateTime.UtcNow;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        Logger.Debug("Analytics", $"Tracked feature: {featureName}");
    }

    /// <summary>
    /// Track user funnel progression
    /// </summary>
    public void TrackFunnel(string funnelName, string step)
    {
        if (!_enabled) return;

        _lock.EnterWriteLock();
        try
        {
            var funnel = _data.Funnels.GetValueOrDefault(funnelName) ?? new FunnelStats();
            funnel.Steps[step] = funnel.Steps.GetValueOrDefault(step) + 1;
            funnel.LastUpdated = DateTime.UtcNow;
            _data.Funnels[funnelName] = funnel;
            _data.LastUpdated = DateTime.UtcNow;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        Logger.Debug("Analytics", $"Tracked funnel: {funnelName} - {step}");
    }

    /// <summary>
    /// Track retention event (e.g., day 0, day 7, day 30)
    /// </summary>
    public void TrackRetention(int day)
    {
        if (!_enabled) return;

        _lock.EnterWriteLock();
        try
        {
            _data.Retention[$"Day{day}"] = DateTime.UtcNow;
            _data.LastUpdated = DateTime.UtcNow;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        Logger.Debug("Analytics", $"Tracked retention: Day {day}");
    }

    /// <summary>
    /// Record NPS survey response
    /// </summary>
    public void RecordNps(int score, string? feedback = null)
    {
        if (!_enabled) return;

        _lock.EnterWriteLock();
        try
        {
            _data.NpsResponses.Add(new NpsResponse
            {
                Score = score,
                Feedback = feedback,
                Timestamp = DateTime.UtcNow
            });
            _data.LastUpdated = DateTime.UtcNow;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        Logger.Debug("Analytics", $"Recorded NPS: {score}");
    }

    /// <summary>
    /// Calculate current NPS score
    /// </summary>
    public double GetNpsScore()
    {
        _lock.EnterReadLock();
        try
        {
            if (_data.NpsResponses.Count == 0) return 0;

            var promoters = _data.NpsResponses.Count(r => r.Score >= 9);
            var detractors = _data.NpsResponses.Count(r => r.Score <= 6);
            var total = _data.NpsResponses.Count;

            return ((promoters - detractors) / (double)total) * 100;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Get funnel conversion rate
    /// </summary>
    public double GetFunnelConversion(string funnelName, string fromStep, string toStep)
    {
        _lock.EnterReadLock();
        try
        {
            if (!_data.Funnels.TryGetValue(funnelName, out var funnel))
                return 0;

            var fromCount = funnel.Steps.GetValueOrDefault(fromStep);
            var toCount = funnel.Steps.GetValueOrDefault(toStep);

            if (fromCount == 0) return 0;
            return (toCount / (double)fromCount) * 100;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Get retention rate for a specific day
    /// </summary>
    public double GetRetentionRate(int day)
    {
        _lock.EnterReadLock();
        try
        {
            var totalUsers = _data.TotalSessions;
            if (totalUsers == 0) return 0;

            var retainedUsers = _data.Retention.Count(r => r.Key == $"Day{day}");
            return (retainedUsers / (double)totalUsers) * 100;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Track session duration
    /// </summary>
    public void TrackSessionStart()
    {
        if (!_enabled) return;
        _data.SessionStartTime = DateTime.UtcNow;
        _data.TotalSessions++;
    }

    public void TrackSessionEnd()
    {
        if (!_enabled) return;
        if (_data.SessionStartTime.HasValue)
        {
            var duration = DateTime.UtcNow - _data.SessionStartTime.Value;
            _data.TotalSessionDuration += duration;
            _data.SessionStartTime = null;
            Flush();
        }
    }

    /// <summary>
    /// Get usage summary for display
    /// </summary>
    public AnalyticsSummary GetSummary()
    {
        _lock.EnterReadLock();
        try
        {
            var topFeatures = _data.Features
                .OrderByDescending(f => f.Value.UsageCount)
                .Take(5)
                .Select(f => new TopFeature { Name = f.Key, Count = f.Value.UsageCount })
                .ToList();

            return new AnalyticsSummary
            {
                TotalSessions = _data.TotalSessions,
                TotalUsageTime = _data.TotalSessionDuration,
                TopFeatures = topFeatures,
                FirstSeen = _data.FirstSeen,
                LastUpdated = _data.LastUpdated,
                NpsScore = GetNpsScore(),
                RetentionDay7 = GetRetentionRate(7),
                RetentionDay30 = GetRetentionRate(30)
            };
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Export analytics data (for user to view or share)
    /// </summary>
    public string Export()
    {
        _lock.EnterReadLock();
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(_data, options);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Clear all analytics data
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _data = new AnalyticsData { FirstSeen = DateTime.UtcNow };
            Flush();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void Load()
    {
        if (!File.Exists(AnalyticsFile))
        {
            _data = new AnalyticsData { FirstSeen = DateTime.UtcNow };
            return;
        }

        try
        {
            _lock.EnterWriteLock();
            var json = File.ReadAllText(AnalyticsFile);
            _data = JsonSerializer.Deserialize<AnalyticsData>(json) ?? new AnalyticsData();
            if (_data.FirstSeen == default)
                _data.FirstSeen = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Logger.Error("Analytics", "Failed to load analytics data", ex);
            _data = new AnalyticsData { FirstSeen = DateTime.UtcNow };
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void Flush()
    {
        if (!_enabled) return;

        try
        {
            _lock.EnterReadLock();
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_data, options);
            File.WriteAllText(AnalyticsFile, json);
            Logger.Debug("Analytics", "Analytics data flushed to disk");
        }
        catch (Exception ex)
        {
            Logger.Error("Analytics", "Failed to save analytics data", ex);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        Flush();
    }
}

/// <summary>
/// Analytics data model stored locally
/// </summary>
public class AnalyticsData
{
    public DateTime FirstSeen { get; set; }
    public DateTime LastUpdated { get; set; }
    public int TotalSessions { get; set; }
    public TimeSpan TotalSessionDuration { get; set; }
    public DateTime? SessionStartTime { get; set; }
    public Dictionary<string, FeatureStats> Features { get; set; } = new();
    public Dictionary<string, FunnelStats> Funnels { get; set; } = new();
    public Dictionary<string, DateTime> Retention { get; set; } = new();
    public List<NpsResponse> NpsResponses { get; set; } = new();
}

public class FeatureStats
{
    public int UsageCount { get; set; }
    public DateTime LastUsed { get; set; }
    public Dictionary<string, int> Contexts { get; set; } = new();
}

public class FunnelStats
{
    public Dictionary<string, int> Steps { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class NpsResponse
{
    public int Score { get; set; }
    public string? Feedback { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AnalyticsSummary
{
    public int TotalSessions { get; set; }
    public TimeSpan TotalUsageTime { get; set; }
    public List<TopFeature> TopFeatures { get; set; } = new();
    public DateTime FirstSeen { get; set; }
    public DateTime LastUpdated { get; set; }
    public double NpsScore { get; set; }
    public double RetentionDay7 { get; set; }
    public double RetentionDay30 { get; set; }
}

public class TopFeature
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}
