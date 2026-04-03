using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.Core.Sync;

/// <summary>
/// Cross-platform analytics synchronization service.
/// Aggregates usage data across Windows, macOS, and Linux implementations.
/// </summary>
public class CrossPlatformAnalyticsSync
{
    private static readonly Lazy<CrossPlatformAnalyticsSync> _instance = new(() => new CrossPlatformAnalyticsSync());
    public static CrossPlatformAnalyticsSync Instance => _instance.Value;

    private readonly string _analyticsDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public event EventHandler<AnalyticsSyncEventArgs>? AnalyticsSynced;

    private CrossPlatformAnalyticsSync()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _analyticsDirectory = Path.Combine(appData, "Redball", "Analytics");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        EnsureDirectoryExists();
    }

    /// <summary>
    /// Records a cross-platform analytics event.
    /// </summary>
    public async Task RecordEventAsync(string eventName, Dictionary<string, object>? properties = null, string? platform = null)
    {
        try
        {
            var platformName = platform ?? GetCurrentPlatform();
            var eventData = new CrossPlatformAnalyticsEvent
            {
                Timestamp = DateTime.UtcNow,
                Platform = platformName,
                EventName = eventName,
                Properties = properties ?? new Dictionary<string, object>(),
                SessionId = GetCurrentSessionId(),
                UserId = GetAnonymousUserId()
            };

            var fileName = $"analytics_{DateTime.UtcNow:yyyyMM}.json";
            var filePath = Path.Combine(_analyticsDirectory, fileName);

            // Append to monthly log
            var line = JsonSerializer.Serialize(eventData, _jsonOptions);
            await File.AppendAllTextAsync(filePath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Logger.Error("CrossPlatformAnalyticsSync", $"Failed to record analytics event: {eventName}", ex);
        }
    }

    /// <summary>
    /// Synchronizes local analytics with cross-platform aggregation.
    /// </summary>
    public async Task<AggregatedAnalytics> AggregateAnalyticsAsync(int days = 30)
    {
        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddDays(-days);

        var aggregated = new AggregatedAnalytics
        {
            Period = new DateRange { Start = startDate, End = endDate },
            PlatformBreakdown = new Dictionary<string, PlatformAnalytics>()
        };

        try
        {
            // Load all analytics files in period
            var files = Directory.GetFiles(_analyticsDirectory, "analytics_*.json")
                .Where(f => IsFileInRange(f, startDate, endDate))
                .ToList();

            var allEvents = new List<CrossPlatformAnalyticsEvent>();

            foreach (var file in files)
            {
                var lines = await File.ReadAllLinesAsync(file);
                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    try
                    {
                        var evt = JsonSerializer.Deserialize<CrossPlatformAnalyticsEvent>(line, _jsonOptions);
                        if (evt != null && evt.Timestamp >= startDate && evt.Timestamp <= endDate)
                        {
                            allEvents.Add(evt);
                        }
                    }
                    catch { /* Skip malformed lines */ }
                }
            }

            // Aggregate by platform
            var platformGroups = allEvents.GroupBy(e => e.Platform);
            foreach (var group in platformGroups)
            {
                var platformStats = new PlatformAnalytics
                {
                    Platform = group.Key,
                    TotalEvents = group.Count(),
                    UniqueSessions = group.Select(e => e.SessionId).Distinct().Count(),
                    UniqueUsers = group.Select(e => e.UserId).Distinct().Count(),
                    EventBreakdown = group.GroupBy(e => e.EventName)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    TopProperties = group.SelectMany(e => e.Properties)
                        .GroupBy(p => p.Key)
                        .ToDictionary(g => g.Key, g => g.Count())
                };

                aggregated.PlatformBreakdown[group.Key] = platformStats;
            }

            // Calculate totals
            aggregated.TotalEvents = allEvents.Count;
            aggregated.UniqueSessions = allEvents.Select(e => e.SessionId).Distinct().Count();
            aggregated.UniqueUsers = allEvents.Select(e => e.UserId).Distinct().Count();

            AnalyticsSynced?.Invoke(this, new AnalyticsSyncEventArgs
            {
                AggregatedData = aggregated,
                SyncedAt = DateTime.UtcNow
            });

            Logger.Info("CrossPlatformAnalyticsSync", 
                $"Aggregated analytics: {aggregated.TotalEvents} events across {aggregated.PlatformBreakdown.Count} platforms");
        }
        catch (Exception ex)
        {
            Logger.Error("CrossPlatformAnalyticsSync", "Analytics aggregation failed", ex);
        }

        return aggregated;
    }

    /// <summary>
    /// Exports aggregated analytics for reporting.
    /// </summary>
    public async Task<string> ExportAnalyticsReportAsync(int days = 30)
    {
        var aggregated = await AggregateAnalyticsAsync(days);
        
        var report = new AnalyticsReport
        {
            GeneratedAt = DateTime.UtcNow,
            Period = aggregated.Period,
            Summary = new AnalyticsSummary
            {
                TotalEvents = aggregated.TotalEvents,
                TotalUsers = aggregated.UniqueUsers,
                TotalSessions = aggregated.UniqueSessions,
                PlatformDistribution = aggregated.PlatformBreakdown
                    .ToDictionary(p => p.Key, p => p.Value.TotalEvents)
            },
            PlatformDetails = aggregated.PlatformBreakdown
        };

        return JsonSerializer.Serialize(report, _jsonOptions);
    }

    /// <summary>
    /// Gets feature usage comparison across platforms.
    /// </summary>
    public async Task<FeatureParityReport> GetFeatureParityReportAsync()
    {
        var report = new FeatureParityReport
        {
            GeneratedAt = DateTime.UtcNow,
            Features = new List<FeatureUsage>()
        };

        var analytics = await AggregateAnalyticsAsync(30);

        // Define features to track
        var features = new[]
        {
            "KeepAwake",
            "BatteryAware",
            "NetworkAware",
            "IdleDetection",
            "TypeThing",
            "MiniWidget",
            "TeamsIntegration",
            "SlackIntegration",
            "ZoomIntegration",
            "BrowserExtension",
            "SmartSchedule"
        };

        foreach (var feature in features)
        {
            var featureUsage = new FeatureUsage
            {
                FeatureName = feature,
                PlatformUsage = new Dictionary<string, int>()
            };

            foreach (var platform in analytics.PlatformBreakdown)
            {
                var count = platform.Value.EventBreakdown
                    .Where(e => e.Key.Contains(feature, StringComparison.OrdinalIgnoreCase))
                    .Sum(e => e.Value);
                
                featureUsage.PlatformUsage[platform.Key] = count;
            }

            featureUsage.TotalUsage = featureUsage.PlatformUsage.Values.Sum();
            featureUsage.IsAvailableOnAllPlatforms = featureUsage.PlatformUsage.Count >= 3;

            report.Features.Add(featureUsage);
        }

        return report;
    }

    /// <summary>
    /// Cleans up old analytics data beyond retention period.
    /// </summary>
    public async Task<int> CleanupOldAnalyticsAsync(int retentionDays = 365)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        var deletedCount = 0;

        try
        {
            var files = Directory.GetFiles(_analyticsDirectory, "analytics_*.json");
            
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTimeUtc < cutoffDate)
                {
                    File.Delete(file);
                    deletedCount++;
                }
            }

            Logger.Info("CrossPlatformAnalyticsSync", $"Cleaned up {deletedCount} old analytics files");
        }
        catch (Exception ex)
        {
            Logger.Error("CrossPlatformAnalyticsSync", "Analytics cleanup failed", ex);
        }

        return deletedCount;
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_analyticsDirectory))
        {
            Directory.CreateDirectory(_analyticsDirectory);
        }
    }

    private string GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return "unknown";
    }

    private string GetCurrentSessionId()
    {
        // Generate or retrieve current session ID
        var sessionId = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToString("yyyyMMddHHmmss");
        return sessionId;
    }

    private string GetAnonymousUserId()
    {
        // Generate anonymous user ID based on machine characteristics
        // This should be consistent per device but not personally identifiable
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineName}:{userName}";
        
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).Substring(0, 16);
    }

    private bool IsFileInRange(string filePath, DateTime start, DateTime end)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.StartsWith("analytics_") && fileName.Length >= 16)
            {
                var dateStr = fileName.Substring(10, 6); // yyyyMM
                if (DateTime.TryParseExact(dateStr, "yyyyMM", null, System.Globalization.DateTimeStyles.None, out var fileDate))
                {
                    return fileDate.Year >= start.Year && fileDate.Year <= end.Year;
                }
            }
        }
        catch { }
        
        return true; // Include if we can't parse
    }
}

/// <summary>
/// Cross-platform analytics event structure.
/// </summary>
public class CrossPlatformAnalyticsEvent
{
    public DateTime Timestamp { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}

/// <summary>
/// Aggregated analytics across all platforms.
/// </summary>
public class AggregatedAnalytics
{
    public DateRange Period { get; set; } = new();
    public int TotalEvents { get; set; }
    public int UniqueSessions { get; set; }
    public int UniqueUsers { get; set; }
    public Dictionary<string, PlatformAnalytics> PlatformBreakdown { get; set; } = new();
}

public class PlatformAnalytics
{
    public string Platform { get; set; } = string.Empty;
    public int TotalEvents { get; set; }
    public int UniqueSessions { get; set; }
    public int UniqueUsers { get; set; }
    public Dictionary<string, int> EventBreakdown { get; set; } = new();
    public Dictionary<string, int> TopProperties { get; set; } = new();
}

public class AnalyticsReport
{
    public DateTime GeneratedAt { get; set; }
    public DateRange Period { get; set; } = new();
    public AnalyticsSummary Summary { get; set; } = new();
    public Dictionary<string, PlatformAnalytics> PlatformDetails { get; set; } = new();
}

public class AnalyticsSummary
{
    public int TotalEvents { get; set; }
    public int TotalUsers { get; set; }
    public int TotalSessions { get; set; }
    public Dictionary<string, int> PlatformDistribution { get; set; } = new();
}

/// <summary>
/// Feature parity report comparing usage across platforms.
/// </summary>
public class FeatureParityReport
{
    public DateTime GeneratedAt { get; set; }
    public List<FeatureUsage> Features { get; set; } = new();
}

public class FeatureUsage
{
    public string FeatureName { get; set; } = string.Empty;
    public Dictionary<string, int> PlatformUsage { get; set; } = new();
    public int TotalUsage { get; set; }
    public bool IsAvailableOnAllPlatforms { get; set; }
    public double UsageParityScore => CalculateParityScore();

    private double CalculateParityScore()
    {
        if (PlatformUsage.Count < 2) return 1.0;
        
        var values = PlatformUsage.Values.ToList();
        if (values.All(v => v == 0)) return 1.0;
        
        var avg = values.Average();
        if (avg == 0) return 1.0;
        
        // Lower variance = higher parity
        var variance = values.Average(v => Math.Pow(v - avg, 2));
        var stdDev = Math.Sqrt(variance);
        
        return Math.Max(0, 1 - (stdDev / avg));
    }
}

public class AnalyticsSyncEventArgs : EventArgs
{
    public AggregatedAnalytics AggregatedData { get; set; } = new();
    public DateTime SyncedAt { get; set; }
}

/// <summary>
/// Date range for analytics periods.
/// </summary>
public class DateRange
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}
