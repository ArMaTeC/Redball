using Redball.Core.Security;
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
public class AnalyticsService : IAnalyticsService
{
    private bool _disposed;
    private static readonly Lazy<AnalyticsService> _instance = new(() => new AnalyticsService());
    public static AnalyticsService Instance => _instance.Value;
    
    private static readonly string AnalyticsFile = Path.Combine(
        AppContext.BaseDirectory, "analytics.json");
    private const int TrendWindowDays = 7;
    private const int DataRetentionDays = 90;
    
    private static readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private AnalyticsData _data = new();
    private Timer? _flushTimer;
    
    // DEBOUNCE: Timer and tracking for high-frequency events
    private readonly Dictionary<string, Timer> _debounceTimers = new();
    private readonly Dictionary<string, int> _pendingEventCounts = new();
    private const int DebounceMs = 250;
    private readonly object _debounceLock = new();

    private readonly bool? _testEnabled;
    private bool IsEnabled => _testEnabled ?? ConfigService.Instance.Config.EnableTelemetry;

    public AnalyticsService(bool? enabled = null)
    {
        _testEnabled = enabled;
        Load();
        // Auto-flush every 5 minutes
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        
        // PRIVACY: Clean up old analytics data on startup
        CleanupOldData();
    }
    
    /// <summary>
    /// Removes analytics data older than the retention period (90 days default).
    /// Implements GDPR/CCPA data minimization principles.
    /// </summary>
    private void CleanupOldData()
    {
        try
        {
            _lock.EnterWriteLock();
            
            var cutoffDate = DateTime.UtcNow.AddDays(-DataRetentionDays);
            var cutoffKey = GetDayKey(cutoffDate);
            var removedCount = 0;
            
            // Clean up feature usage history
            foreach (var feature in _data.Features.Values)
            {
                var keysToRemove = feature.DailyUsage.Keys
                    .Where(k => string.Compare(k, cutoffKey, StringComparison.Ordinal) < 0)
                    .ToList();
                
                foreach (var key in keysToRemove)
                {
                    feature.DailyUsage.Remove(key);
                    removedCount++;
                }
            }
            
            // Clean up engagement history
            var engagementKeysToRemove = _data.SessionHistory.Keys
                .Where(k => string.Compare(k, cutoffKey, StringComparison.Ordinal) < 0)
                .ToList();
            
            foreach (var key in engagementKeysToRemove)
            {
                _data.SessionHistory.Remove(key);
                removedCount++;
            }
            
            if (removedCount > 0)
            {
                Logger.Info("AnalyticsService", $"Cleaned up {removedCount} analytics entries older than {DataRetentionDays} days");
                Flush();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AnalyticsService", "Failed to cleanup old analytics data", ex);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private static string GetFeatureCategory(string featureName)
    {
        if (featureName.StartsWith("keepawake.", StringComparison.OrdinalIgnoreCase) ||
            featureName == "app.launch" ||
            featureName == "app.exit")
        {
            return "Core Usage";
        }

        if (featureName.StartsWith("typething.", StringComparison.OrdinalIgnoreCase))
        {
            return "TypeThing";
        }

        if (featureName.StartsWith("onboarding.", StringComparison.OrdinalIgnoreCase) ||
            featureName == "startup.enabled")
        {
            return "Onboarding";
        }

        if (featureName.StartsWith("settings.", StringComparison.OrdinalIgnoreCase))
        {
            return "Settings";
        }

        if (featureName.StartsWith("diagnostics.", StringComparison.OrdinalIgnoreCase) ||
            featureName.StartsWith("config.", StringComparison.OrdinalIgnoreCase) ||
            featureName == "logs.opened")
        {
            return "Diagnostics";
        }

        if (featureName.StartsWith("update.", StringComparison.OrdinalIgnoreCase) ||
            featureName == "github.opened")
        {
            return "Updates";
        }

        if (featureName.StartsWith("analytics.", StringComparison.OrdinalIgnoreCase) ||
            featureName.StartsWith("metrics.", StringComparison.OrdinalIgnoreCase) ||
            featureName == "about.opened")
        {
            return "Insights";
        }

        return "Other";
    }

    private static string GetDayKey(DateTime timestamp)
    {
        return timestamp.ToString("yyyy-MM-dd");
    }

    private static int SumRange(Dictionary<string, int> history, DateTime startInclusive, DateTime endExclusive)
    {
        return history.Sum(entry =>
        {
            if (!DateTime.TryParse(entry.Key, out var day))
            {
                return 0;
            }

            var normalizedDay = day.Date;
            return normalizedDay >= startInclusive.Date && normalizedDay < endExclusive.Date
                ? entry.Value
                : 0;
        });
    }

    private static double CalculateTrendPercent(int recent, int prior)
    {
        if (prior <= 0)
        {
            return recent > 0 ? 100 : 0;
        }

        return ((recent - prior) / (double)prior) * 100;
    }

    private static int GetFeatureCount(IReadOnlyDictionary<string, FeatureStats> features, string featureName)
    {
        return features.TryGetValue(featureName, out var stats) ? stats.UsageCount : 0;
    }

    private static double CalculateRate(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0;
        }

        return (numerator / (double)denominator) * 100;
    }

    /// <summary>
    /// Track a feature usage event with automatic debouncing for high-frequency events.
    /// </summary>
    public void TrackFeature(string featureName, string? context = null)
    {
        if (!IsEnabled) return;

        // DEBOUNCE: High-frequency events (scroll, resize, mouse) are debounced
        if (IsHighFrequencyEvent(featureName))
        {
            TrackFeatureDebounced(featureName, context);
            return;
        }

        _lock.EnterWriteLock();
        try
        {
            var feature = _data.Features.GetValueOrDefault(featureName) ?? new FeatureStats();
            var todayKey = GetDayKey(DateTime.UtcNow);
            feature.UsageCount++;
            feature.LastUsed = DateTime.UtcNow;
            feature.DailyUsage[todayKey] = feature.DailyUsage.GetValueOrDefault(todayKey) + 1;
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
    /// Check if an event is high-frequency and should be debounced.
    /// </summary>
    private static bool IsHighFrequencyEvent(string featureName)
    {
        var highFreqPrefixes = new[] { "scroll.", "resize.", "mouse.move", "mouse.drag" };
        return highFreqPrefixes.Any(prefix => featureName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Track high-frequency event with debouncing (250ms).
    /// </summary>
    private void TrackFeatureDebounced(string featureName, string? context)
    {
        lock (_debounceLock)
        {
            _pendingEventCounts[featureName] = _pendingEventCounts.GetValueOrDefault(featureName) + 1;

            // Cancel existing timer if any
            if (_debounceTimers.TryGetValue(featureName, out var existingTimer))
            {
                existingTimer.Change(Timeout.Infinite, Timeout.Infinite);
                existingTimer.Dispose();
            }

            // Create new timer
            var timer = new Timer(_ =>
            {
                FlushDebouncedEvent(featureName, context);
            }, null, DebounceMs, Timeout.Infinite);

            _debounceTimers[featureName] = timer;
        }
    }

    /// <summary>
    /// Flush a debounced event after the delay period.
    /// </summary>
    private void FlushDebouncedEvent(string featureName, string? context)
    {
        int count;
        lock (_debounceLock)
        {
            if (!_pendingEventCounts.TryGetValue(featureName, out count))
                return;

            _pendingEventCounts.Remove(featureName);
            _debounceTimers.Remove(featureName);
        }

        if (count == 0) return;

        _lock.EnterWriteLock();
        try
        {
            var feature = _data.Features.GetValueOrDefault(featureName) ?? new FeatureStats();
            var todayKey = GetDayKey(DateTime.UtcNow);
            feature.UsageCount += count;
            feature.LastUsed = DateTime.UtcNow;
            feature.DailyUsage[todayKey] = feature.DailyUsage.GetValueOrDefault(todayKey) + count;
            if (context != null)
            {
                feature.Contexts[context] = feature.Contexts.GetValueOrDefault(context) + count;
            }
            _data.Features[featureName] = feature;
            _data.LastUpdated = DateTime.UtcNow;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        Logger.Debug("Analytics", $"Tracked debounced feature: {featureName} (count: {count})");
    }

    /// <summary>
    /// Track user funnel progression
    /// </summary>
    public void TrackFunnel(string funnelName, string step)
    {
        if (!IsEnabled) return;

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
        if (!IsEnabled) return;

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
        if (!IsEnabled) return;

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
        if (!IsEnabled) return;
        _data.SessionStartTime = DateTime.UtcNow;
        _data.TotalSessions++;
        var todayKey = GetDayKey(DateTime.UtcNow);
        _data.SessionHistory[todayKey] = _data.SessionHistory.GetValueOrDefault(todayKey) + 1;
    }

    public void TrackSessionEnd()
    {
        if (!IsEnabled) return;
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
            var utcToday = DateTime.UtcNow.Date;
            var recentStart = utcToday.AddDays(-(TrendWindowDays - 1));
            var recentEnd = utcToday.AddDays(1);
            var priorStart = recentStart.AddDays(-TrendWindowDays);
            var priorEnd = recentStart;

            var topFeatures = _data.Features
                .OrderByDescending(f => f.Value.UsageCount)
                .Take(5)
                .Select(f => new TopFeature { Name = f.Key, Count = f.Value.UsageCount })
                .ToList();

            var topCategories = _data.Features
                .GroupBy(f => GetFeatureCategory(f.Key))
                .Select(group => new TopFeature
                {
                    Name = group.Key,
                    Count = group.Sum(feature => feature.Value.UsageCount)
                })
                .OrderByDescending(category => category.Count)
                .ToList();

            var categoryTrends = _data.Features
                .GroupBy(f => GetFeatureCategory(f.Key))
                .Select(group =>
                {
                    var recentCount = group.Sum(feature => SumRange(feature.Value.DailyUsage, recentStart, recentEnd));
                    var priorCount = group.Sum(feature => SumRange(feature.Value.DailyUsage, priorStart, priorEnd));
                    return new CategoryTrend
                    {
                        Name = group.Key,
                        RecentCount = recentCount,
                        PriorCount = priorCount,
                        TrendPercent = CalculateTrendPercent(recentCount, priorCount)
                    };
                })
                .OrderByDescending(category => category.RecentCount)
                .ThenByDescending(category => category.TrendPercent)
                .ToList();

            var totalFeatureEvents = _data.Features.Sum(f => f.Value.UsageCount);
            var averageSessionDuration = _data.TotalSessions > 0
                ? TimeSpan.FromTicks(_data.TotalSessionDuration.Ticks / _data.TotalSessions)
                : TimeSpan.Zero;
            var activeUsersLast7Days = _data.TotalSessions;
            var recentSessions = SumRange(_data.SessionHistory, recentStart, recentEnd);
            var priorSessions = SumRange(_data.SessionHistory, priorStart, priorEnd);
            var featureAdoptionRate = _data.TotalSessions > 0
                ? Math.Min(100, (_data.Features.Count / (double)_data.TotalSessions) * 100)
                : 0;
            var lastFeatureUse = _data.Features.Count > 0
                ? _data.Features.Max(f => f.Value.LastUsed)
                : _data.LastUpdated;
            var typeThingStarted = GetFeatureCount(_data.Features, "typething.started");
            var typeThingCompleted = GetFeatureCount(_data.Features, "typething.completed");
            var settingsSaved = GetFeatureCount(_data.Features, "settings.saved");
            var settingsFailed = GetFeatureCount(_data.Features, "settings.save_failed") +
                                 GetFeatureCount(_data.Features, "settings.save_validation_failed");
            var updateSucceeded = GetFeatureCount(_data.Features, "update.download_succeeded");
            var updateFailed = GetFeatureCount(_data.Features, "update.download_failed");
            var diagnosticsOpened = GetFeatureCount(_data.Features, "diagnostics.opened");
            var diagnosticsExported = GetFeatureCount(_data.Features, "diagnostics.exported");
            var onboardingShown = GetFeatureCount(_data.Features, "onboarding.shown");
            var onboardingCompleted = GetFeatureCount(_data.Features, "onboarding.completed");

            return new AnalyticsSummary
            {
                TotalSessions = _data.TotalSessions,
                TotalUsageTime = _data.TotalSessionDuration,
                TopFeatures = topFeatures,
                FirstSeen = _data.FirstSeen,
                LastUpdated = _data.LastUpdated,
                NpsScore = GetNpsScore(),
                RetentionDay7 = GetRetentionRate(7),
                RetentionDay30 = GetRetentionRate(30),
                AverageSessionDuration = averageSessionDuration,
                TotalFeatureEvents = totalFeatureEvents,
                TopCategories = topCategories,
                CategoryTrends = categoryTrends,
                FeatureAdoptionRate = featureAdoptionRate,
                ActiveUsersLast7Days = activeUsersLast7Days,
                LastFeatureUse = lastFeatureUse,
                RecentSessions = recentSessions,
                PriorSessions = priorSessions,
                SessionTrendPercent = CalculateTrendPercent(recentSessions, priorSessions),
                TypeThingSuccessRate = CalculateRate(typeThingCompleted, typeThingStarted),
                TypeThingCompletions = typeThingCompleted,
                TypeThingAttempts = typeThingStarted,
                SettingsSaveSuccessRate = CalculateRate(settingsSaved, settingsSaved + settingsFailed),
                SettingsSaves = settingsSaved,
                SettingsSaveAttempts = settingsSaved + settingsFailed,
                UpdateSuccessRate = CalculateRate(updateSucceeded, updateSucceeded + updateFailed),
                UpdateSuccesses = updateSucceeded,
                UpdateAttempts = updateSucceeded + updateFailed,
                DiagnosticsExportRate = CalculateRate(diagnosticsExported, diagnosticsOpened),
                DiagnosticsExports = diagnosticsExported,
                DiagnosticsOpens = diagnosticsOpened,
                OnboardingCompletionRate = CalculateRate(onboardingCompleted, onboardingShown),
                OnboardingCompletions = onboardingCompleted,
                OnboardingStarts = onboardingShown
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
    /// Export analytics data as CSV for spreadsheet import.
    /// </summary>
    public string ExportToCsv()
    {
        _lock.EnterReadLock();
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Feature,Category,UsageCount,LastUsed");
            foreach (var kvp in _data.Features.OrderByDescending(f => f.Value.UsageCount))
            {
                var category = GetFeatureCategory(kvp.Key);
                var escaped = kvp.Key.Contains(',') ? $"\"{kvp.Key}\"" : kvp.Key;
                sb.AppendLine($"{escaped},{category},{kvp.Value.UsageCount},{kvp.Value.LastUsed:yyyy-MM-dd HH:mm:ss}");
            }

            sb.AppendLine();
            sb.AppendLine("Date,Sessions");
            foreach (var kvp in _data.SessionHistory.OrderBy(s => s.Key))
            {
                sb.AppendLine($"{kvp.Key},{kvp.Value}");
            }

            sb.AppendLine();
            sb.AppendLine("Funnel,Step,Count");
            foreach (var funnel in _data.Funnels)
            {
                foreach (var step in funnel.Value.Steps)
                {
                    sb.AppendLine($"{funnel.Key},{step.Key},{step.Value}");
                }
            }

            return sb.ToString();
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
            // SECURITY: Use SecureJsonSerializer with size limit and max depth
            _data = SecureJsonSerializer.Deserialize<AnalyticsData>(json) ?? new AnalyticsData();
            if (_data.FirstSeen == default)
                _data.FirstSeen = DateTime.UtcNow;

            PruneStaleData();
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

    private void PruneStaleData()
    {
        var cutoff = DateTime.UtcNow.AddDays(-DataRetentionDays);
        var cutoffKey = cutoff.ToString("yyyy-MM-dd");
        var pruned = 0;

        // Prune daily usage from feature stats
        foreach (var feature in _data.Features.Values)
        {
            var staleKeys = feature.DailyUsage.Keys
                .Where(k => string.Compare(k, cutoffKey, StringComparison.Ordinal) < 0)
                .ToList();
            foreach (var key in staleKeys)
            {
                feature.DailyUsage.Remove(key);
                pruned++;
            }
        }

        // Prune session history
        var staleSessionKeys = _data.SessionHistory.Keys
            .Where(k => string.Compare(k, cutoffKey, StringComparison.Ordinal) < 0)
            .ToList();
        foreach (var key in staleSessionKeys)
        {
            _data.SessionHistory.Remove(key);
            pruned++;
        }

        // Prune old NPS responses
        var beforeCount = _data.NpsResponses.Count;
        _data.NpsResponses.RemoveAll(r => r.Timestamp < cutoff);
        pruned += beforeCount - _data.NpsResponses.Count;

        if (pruned > 0)
        {
            Logger.Info("Analytics", $"Pruned {pruned} stale entries older than {DataRetentionDays} days");
        }
    }

    private void Flush()
    {
        if (!IsEnabled) return;

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
        if (_disposed) return;
        _disposed = true;

        _flushTimer?.Dispose();
        _flushTimer = null;

        // Dispose all debounce timers
        lock (_debounceLock)
        {
            foreach (var timer in _debounceTimers.Values)
            {
                timer?.Dispose();
            }
            _debounceTimers.Clear();
            _pendingEventCounts.Clear();
        }

        Flush();

        _lock.Dispose();
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
    public Dictionary<string, int> SessionHistory { get; set; } = new();
    public Dictionary<string, FeatureStats> Features { get; set; } = new();
    public Dictionary<string, FunnelStats> Funnels { get; set; } = new();
    public Dictionary<string, DateTime> Retention { get; set; } = new();
    public List<NpsResponse> NpsResponses { get; set; } = new();
}

public class FeatureStats
{
    public int UsageCount { get; set; }
    public DateTime LastUsed { get; set; }
    public Dictionary<string, int> DailyUsage { get; set; } = new();
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
    public List<TopFeature> TopCategories { get; set; } = new();
    public List<CategoryTrend> CategoryTrends { get; set; } = new();
    public DateTime FirstSeen { get; set; }
    public DateTime LastUpdated { get; set; }
    public double NpsScore { get; set; }
    public double RetentionDay7 { get; set; }
    public double RetentionDay30 { get; set; }
    public TimeSpan AverageSessionDuration { get; set; }
    public int TotalFeatureEvents { get; set; }
    public double FeatureAdoptionRate { get; set; }
    public int ActiveUsersLast7Days { get; set; }
    public DateTime LastFeatureUse { get; set; }
    public int RecentSessions { get; set; }
    public int PriorSessions { get; set; }
    public double SessionTrendPercent { get; set; }
    public double TypeThingSuccessRate { get; set; }
    public int TypeThingCompletions { get; set; }
    public int TypeThingAttempts { get; set; }
    public double SettingsSaveSuccessRate { get; set; }
    public int SettingsSaves { get; set; }
    public int SettingsSaveAttempts { get; set; }
    public double UpdateSuccessRate { get; set; }
    public int UpdateSuccesses { get; set; }
    public int UpdateAttempts { get; set; }
    public double DiagnosticsExportRate { get; set; }
    public int DiagnosticsExports { get; set; }
    public int DiagnosticsOpens { get; set; }
    public double OnboardingCompletionRate { get; set; }
    public int OnboardingCompletions { get; set; }
    public int OnboardingStarts { get; set; }
}

public class TopFeature
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public class CategoryTrend
{
    public string Name { get; set; } = "";
    public int RecentCount { get; set; }
    public int PriorCount { get; set; }
    public double TrendPercent { get; set; }
}
