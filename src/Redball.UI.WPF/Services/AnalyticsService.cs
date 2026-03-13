using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Analytics service for tracking feature usage and user engagement.
/// All data is stored locally; no cloud transmission without explicit opt-in.
/// </summary>
public class AnalyticsService
{
    private static readonly string AnalyticsFile = Path.Combine(
        AppContext.BaseDirectory, "analytics.json");
    
    private readonly ReaderWriterLockSlim _lock = new();
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
                LastUpdated = _data.LastUpdated
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
}

public class FeatureStats
{
    public int UsageCount { get; set; }
    public DateTime LastUsed { get; set; }
    public Dictionary<string, int> Contexts { get; set; } = new();
}

public class AnalyticsSummary
{
    public int TotalSessions { get; set; }
    public TimeSpan TotalUsageTime { get; set; }
    public List<TopFeature> TopFeatures { get; set; } = new();
    public DateTime FirstSeen { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class TopFeature
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}
