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
/// Requires explicit user consent before collecting any data.
/// </summary>
public class CrossPlatformAnalyticsSync
{
    private static readonly Lazy<CrossPlatformAnalyticsSync> _instance = new(() => new CrossPlatformAnalyticsSync());
    public static CrossPlatformAnalyticsSync Instance => _instance.Value;

    private readonly string _analyticsDirectory;
    private readonly string _consentFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _consentGranted;
    private bool _consentConfigured;

    public event EventHandler<AnalyticsSyncEventArgs>? AnalyticsSynced;

    /// <summary>
    /// Whether the user has granted consent for analytics collection.
    /// </summary>
    public bool ConsentGranted
    {
        get => _consentGranted;
        set
        {
            _consentGranted = value;
            _consentConfigured = true;
            SaveConsentConfiguration();
        }
    }

    /// <summary>
    /// Whether the user has made a consent decision (opt-in or opt-out).
    /// If false, first-run consent dialog should be shown.
    /// </summary>
    public bool IsConsentConfigured => _consentConfigured;

    private CrossPlatformAnalyticsSync()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _analyticsDirectory = Path.Combine(appData, "Redball", "Analytics");
        _consentFilePath = Path.Combine(appData, "Redball", "analytics_consent.json");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        EnsureDirectoryExists();
        LoadConsentConfiguration();
    }

    /// <summary>
    /// Loads consent configuration from disk.
    /// </summary>
    private void LoadConsentConfiguration()
    {
        try
        {
            if (File.Exists(_consentFilePath))
            {
                var json = File.ReadAllText(_consentFilePath);
                var config = JsonSerializer.Deserialize<AnalyticsConsentConfig>(json);
                if (config != null)
                {
                    _consentGranted = config.ConsentGranted;
                    _consentConfigured = config.ConsentConfigured;
                }
            }
        }
        catch
        {
            // If we can't read consent file, treat as unconfigured (require opt-in)
            _consentGranted = false;
            _consentConfigured = false;
        }
    }

    /// <summary>
    /// Persists consent configuration to disk.
    /// </summary>
    private void SaveConsentConfiguration()
    {
        try
        {
            var config = new AnalyticsConsentConfig
            {
                ConsentGranted = _consentGranted,
                ConsentConfigured = _consentConfigured,
                ConfiguredAt = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(_consentFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error("CrossPlatformAnalyticsSync", "Failed to save consent configuration", ex);
        }
    }

    /// <summary>
    /// Resets consent configuration (for testing or privacy resets).
    /// </summary>
    public void ResetConsent()
    {
        _consentGranted = false;
        _consentConfigured = false;
        try
        {
            if (File.Exists(_consentFilePath))
            {
                File.Delete(_consentFilePath);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("CrossPlatformAnalyticsSync", $"Failed to delete consent file: {ex.Message}");
        }
    }

    /// <summary>
    /// Records a cross-platform analytics event.
    /// Only records if user has granted consent.
    /// Applies aggressive sampling for high-frequency events.
    /// </summary>
    public async Task RecordEventAsync(string eventName, Dictionary<string, object>? properties = null, string? platform = null)
    {
        // PRIVACY: Do not collect analytics without explicit user consent
        if (!_consentGranted)
        {
            return;
        }

        // PERFORMANCE: Apply sampling for high-frequency events to reduce storage/battery impact
        if (!ShouldSampleEvent(eventName))
        {
            return;
        }

        try
        {
            var platformName = platform ?? GetCurrentPlatform();
            
            // PRIVACY: Sanitize properties to prevent PII leakage
            var sanitizedProperties = SanitizeProperties(properties);
            
            var eventData = new CrossPlatformAnalyticsEvent
            {
                Timestamp = DateTime.UtcNow,
                Platform = platformName,
                EventName = eventName,
                Properties = sanitizedProperties,
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
    /// Determines if an event should be sampled based on frequency.
    /// High-frequency events: 1% for mouse moves, 10% for scroll, 100% for clicks.
    /// </summary>
    private static bool ShouldSampleEvent(string eventName)
    {
        var random = Random.Shared.Next(100);
        
        // Mouse movement: 1% sampling
        if (eventName.Contains("mouse.move", StringComparison.OrdinalIgnoreCase) ||
            eventName.Contains("mouse.drag", StringComparison.OrdinalIgnoreCase))
        {
            return random < 1;
        }
        
        // Scroll events: 10% sampling
        if (eventName.Contains("scroll", StringComparison.OrdinalIgnoreCase))
        {
            return random < 10;
        }
        
        // All other events (clicks, etc.): 100% sampling
        return true;
    }

    /// <summary>
    /// Sanitizes analytics properties to prevent PII leakage.
    /// Only allows whitelisted property names and safe value types.
    /// </summary>
    private static Dictionary<string, object> SanitizeProperties(Dictionary<string, object>? properties)
    {
        if (properties == null || properties.Count == 0)
        {
            return new Dictionary<string, object>();
        }

        var sanitized = new Dictionary<string, object>();
        
        // Whitelist of allowed property names (case-insensitive)
        var allowedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "duration", "count", "size", "width", "height", "index", "page",
            "category", "type", "status", "result", "action", "feature",
            "enabled", "disabled", "success", "failure", "error_code",
            "version", "platform", "theme", "language", "timezone_offset"
        };

        foreach (var kvp in properties)
        {
            // Skip properties not in whitelist
            if (!allowedProperties.Contains(kvp.Key))
            {
                continue;
            }

            // Only allow safe value types (primitives, no strings that could contain PII)
            var value = kvp.Value;
            if (value == null)
            {
                sanitized[kvp.Key] = "null";
            }
            else if (value is bool || value is int || value is long || value is double || value is float)
            {
                sanitized[kvp.Key] = value;
            }
            else if (value is string str)
            {
                // For strings, only allow short enum-like values (no user input)
                if (str.Length <= 50 && !str.Contains('/') && !str.Contains('\\') && !str.Contains('@'))
                {
                    sanitized[kvp.Key] = str;
                }
            }
            // Skip complex objects, arrays, etc.
        }

        return sanitized;
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
                    catch (Exception ex)
                    {
                        Logger.Debug("CrossPlatformAnalyticsSync", $"Skipping malformed analytics line in aggregation: {ex.Message}");
                    }
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

    /// <summary>
    /// GDPR/CCPA: Export all user analytics data in portable JSON format.
    /// </summary>
    public async Task<string> ExportUserDataAsync(string exportPath)
    {
        try
        {
            var exportData = new
            {
                ExportedAt = DateTime.UtcNow,
                UserId = GetAnonymousUserId(),
                ConsentGranted = _consentGranted,
                ConsentConfiguredAt = _consentConfigured ? DateTime.UtcNow : (DateTime?)null,
                Analytics = await LoadAllAnalyticsEventsAsync()
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(exportPath, json);

            Logger.Info("CrossPlatformAnalyticsSync", $"User data exported to {exportPath}");
            return exportPath;
        }
        catch (Exception ex)
        {
            Logger.Error("CrossPlatformAnalyticsSync", "Data export failed", ex);
            throw;
        }
    }

    /// <summary>
    /// GDPR/CCPA: Delete all user analytics data permanently.
    /// </summary>
    public async Task<bool> DeleteAllUserDataAsync()
    {
        try
        {
            // Delete all analytics files
            var files = Directory.GetFiles(_analyticsDirectory, "analytics_*.json");
            foreach (var file in files)
            {
                File.Delete(file);
            }

            // Delete consent file
            if (File.Exists(_consentFilePath))
            {
                File.Delete(_consentFilePath);
            }

            // Reset consent state
            _consentGranted = false;
            _consentConfigured = false;

            Logger.Info("CrossPlatformAnalyticsSync", $"Deleted all user data ({files.Length} files)");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("CrossPlatformAnalyticsSync", "Data deletion failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Loads all analytics events from disk for export.
    /// </summary>
    private async Task<List<CrossPlatformAnalyticsEvent>> LoadAllAnalyticsEventsAsync()
    {
        var allEvents = new List<CrossPlatformAnalyticsEvent>();

        try
        {
            var files = Directory.GetFiles(_analyticsDirectory, "analytics_*.json");
            
            foreach (var file in files)
            {
                var lines = await File.ReadAllLinesAsync(file);
                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    try
                    {
                        var evt = JsonSerializer.Deserialize<CrossPlatformAnalyticsEvent>(line, _jsonOptions);
                        if (evt != null)
                        {
                            allEvents.Add(evt);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug("CrossPlatformAnalyticsSync", $"Skipping malformed analytics line in export: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CrossPlatformAnalyticsSync", "Failed to load analytics events", ex);
        }

        return allEvents;
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
        catch (Exception ex)
        {
            Logger.Debug("CrossPlatformAnalyticsSync", $"Failed to parse file date: {ex.Message}");
        }
        
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

/// <summary>
/// Analytics consent configuration for persistence.
/// </summary>
public class AnalyticsConsentConfig
{
    public bool ConsentGranted { get; set; }
    public bool ConsentConfigured { get; set; }
    public DateTime ConfiguredAt { get; set; }
}
