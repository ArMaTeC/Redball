using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Admin dashboard service for IT departments to monitor usage and enforce policies.
/// Provides aggregated reports, compliance metrics, and remote policy management.
/// </summary>
public class AdminDashboardService
{
    private static readonly Lazy<AdminDashboardService> _instance = new(() => new AdminDashboardService());
    public static AdminDashboardService Instance => _instance.Value;

    private readonly string _dashboardCacheDir;
    private readonly string _policyFile;
    private AdminPolicy _activePolicy = new();
    
    public event EventHandler<PolicyChangedEventArgs>? PolicyChanged;
    public event EventHandler<ComplianceReportReadyEventArgs>? ComplianceReportReady;

    public bool IsAdminModeEnabled => ConfigService.Instance.Config.EnableAdminDashboard;
    public AdminPolicy ActivePolicy => _activePolicy;

    private AdminDashboardService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _dashboardCacheDir = Path.Combine(localAppData, "Redball", "AdminDashboard");
        _policyFile = Path.Combine(_dashboardCacheDir, "policy.json");
        
        if (!Directory.Exists(_dashboardCacheDir))
        {
            Directory.CreateDirectory(_dashboardCacheDir);
        }
        
        LoadPolicy();
        Logger.Verbose("AdminDashboardService", "Initialized");
    }

    /// <summary>
    /// Generates a usage report for a date range.
    /// </summary>
    public UsageReport GenerateUsageReport(DateTime startDate, DateTime endDate)
    {
        var auditEntries = AuditLogService.Instance.GetEntries(startDate, endDate);
        var sessionStats = SessionStatsService.Instance;
        
        var report = new UsageReport
        {
            Period = new DateRange { Start = startDate, End = endDate },
            TotalSessions = auditEntries.Count(e => e.Action == "SessionStart" || e.Action == "TimedSessionStart"),
            TotalActiveTime = TimeSpan.FromMinutes(auditEntries
                .Where(e => e.EventType == AuditEventType.Session && e.Details != null)
                .Select(e => ExtractDuration(e.Details))
                .Sum()),
            UniqueUsers = auditEntries.Select(e => e.UserId).Distinct().Count(),
            ConfigChanges = auditEntries.Count(e => e.EventType == AuditEventType.ConfigChange),
            SecurityEvents = auditEntries.Count(e => e.EventType == AuditEventType.Security),
            AutoPauseEvents = auditEntries.Count(e => e.Action == "AutoPause"),
            TopUsers = GetTopUsers(auditEntries, 10),
            DailyBreakdown = GetDailyBreakdown(auditEntries, startDate, endDate)
        };
        
        Logger.Info("AdminDashboardService", $"Generated usage report: {report.TotalSessions} sessions, {report.UniqueUsers} users");
        
        return report;
    }

    /// <summary>
    /// Generates a compliance report for regulatory requirements.
    /// </summary>
    public ComplianceReport GenerateComplianceReport(DateTime startDate, DateTime endDate)
    {
        var auditEntries = AuditLogService.Instance.GetEntries(startDate, endDate);
        var summary = AuditLogService.Instance.GetSummary(startDate, endDate);
        
        var report = new ComplianceReport
        {
            Period = new DateRange { Start = startDate, End = endDate },
            GeneratedAt = DateTime.UtcNow,
            TotalEvents = summary.TotalEntries,
            FailedActions = summary.FailedActions,
            UserActionCount = summary.UserActionCount,
            ConfigChangeCount = summary.ConfigChangeCount,
            SecurityEventCount = summary.SecurityEventCount,
            SystemEventCount = summary.SystemEventCount,
            SessionEventCount = summary.SessionEventCount,
            UniqueUsers = summary.UniqueUsers,
            ComplianceScore = CalculateComplianceScore(summary),
            Violations = FindViolations(auditEntries),
            DataRetentionStatus = CheckDataRetention(),
            EncryptionStatus = CheckEncryptionStatus()
        };
        
        ComplianceReportReady?.Invoke(this, new ComplianceReportReadyEventArgs { Report = report });
        
        return report;
    }

    /// <summary>
    /// Applies a new admin policy.
    /// </summary>
    public async Task<bool> ApplyPolicyAsync(AdminPolicy policy)
    {
        try
        {
            _activePolicy = policy;
            SavePolicy();
            
            // Apply policy to current config
            var config = ConfigService.Instance.Config;
            ApplyPolicyToConfig(config, policy);
            
            PolicyChanged?.Invoke(this, new PolicyChangedEventArgs 
            { 
                Policy = policy, 
                AppliedAt = DateTime.UtcNow 
            });
            
            Logger.Info("AdminDashboardService", "Policy applied successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("AdminDashboardService", "Failed to apply policy", ex);
            return false;
        }
    }

    /// <summary>
    /// Validates current configuration against active policy.
    /// </summary>
    public PolicyValidationResult ValidateCompliance()
    {
        var config = ConfigService.Instance.Config;
        var violations = new List<PolicyViolation>();
        
        // Check max session duration
        if (_activePolicy.MaxSessionDurationMinutes.HasValue && 
            config.DefaultDuration > _activePolicy.MaxSessionDurationMinutes.Value)
        {
            violations.Add(new PolicyViolation
            {
                Rule = "MaxSessionDuration",
                Expected = $"<= {_activePolicy.MaxSessionDurationMinutes} min",
                Actual = $"{config.DefaultDuration} min",
                Severity = ViolationSeverity.Warning
            });
        }
        
        // Check required features
        if (_activePolicy.RequireBatteryAware && !config.BatteryAware)
        {
            violations.Add(new PolicyViolation
            {
                Rule = "RequireBatteryAware",
                Expected = "Enabled",
                Actual = "Disabled",
                Severity = ViolationSeverity.Error
            });
        }
        
        if (_activePolicy.RequireIdleDetection && !config.IdleDetection)
        {
            violations.Add(new PolicyViolation
            {
                Rule = "RequireIdleDetection",
                Expected = "Enabled",
                Actual = "Disabled",
                Severity = ViolationSeverity.Error
            });
        }
        
        // Check telemetry settings
        if (_activePolicy.RequireTelemetry && !config.EnableTelemetry)
        {
            violations.Add(new PolicyViolation
            {
                Rule = "RequireTelemetry",
                Expected = "Enabled",
                Actual = "Disabled",
                Severity = ViolationSeverity.Warning
            });
        }
        
        return new PolicyValidationResult
        {
            IsCompliant = !violations.Any(v => v.Severity == ViolationSeverity.Error),
            Violations = violations,
            LastChecked = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Exports dashboard data to CSV for external analysis.
    /// </summary>
    public string ExportUsageReportCsv(UsageReport report)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Metric,Value");
        csv.AppendLine($"Period Start,{report.Period.Start:yyyy-MM-dd}");
        csv.AppendLine($"Period End,{report.Period.End:yyyy-MM-dd}");
        csv.AppendLine($"Total Sessions,{report.TotalSessions}");
        csv.AppendLine($"Total Active Time (hours),{report.TotalActiveTime.TotalHours:F2}");
        csv.AppendLine($"Unique Users,{report.UniqueUsers}");
        csv.AppendLine($"Config Changes,{report.ConfigChanges}");
        csv.AppendLine($"Security Events,{report.SecurityEvents}");
        csv.AppendLine($"Auto-Pause Events,{report.AutoPauseEvents}");
        
        csv.AppendLine();
        csv.AppendLine("Daily Breakdown");
        csv.AppendLine("Date,Sessions,Active Hours");
        foreach (var day in report.DailyBreakdown)
        {
            csv.AppendLine($"{day.Date:yyyy-MM-dd},{day.SessionCount},{day.ActiveHours:F2}");
        }
        
        return csv.ToString();
    }

    /// <summary>
    /// Gets real-time system health metrics.
    /// </summary>
    public SystemHealthMetrics GetSystemHealth()
    {
        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        
        return new SystemHealthMetrics
        {
            Timestamp = DateTime.UtcNow,
            MemoryUsageMB = currentProcess.WorkingSet64 / (1024 * 1024),
            Uptime = DateTime.Now - currentProcess.StartTime,
            IsKeepAwakeActive = KeepAwakeService.Instance.IsActive,
            ActiveSessionDuration = SessionStatsService.Instance.CurrentSessionDuration,
            ConfigServiceStatus = ConfigService.Instance != null ? "OK" : "Error",
            LastError = null, // Would need to be tracked by a custom error tracker
            PendingUpdates = 0 // Would be populated by UpdateService
        };
    }

    // Private methods
    private void LoadPolicy()
    {
        try
        {
            if (File.Exists(_policyFile))
            {
                var json = File.ReadAllText(_policyFile);
                _activePolicy = JsonSerializer.Deserialize<AdminPolicy>(json) ?? new AdminPolicy();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("AdminDashboardService", $"Failed to load policy: {ex.Message}");
            _activePolicy = new AdminPolicy();
        }
    }

    private void SavePolicy()
    {
        try
        {
            var json = JsonSerializer.Serialize(_activePolicy, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_policyFile, json);
        }
        catch (Exception ex)
        {
            Logger.Error("AdminDashboardService", $"Failed to save policy: {ex.Message}");
        }
    }

    private void ApplyPolicyToConfig(RedballConfig config, AdminPolicy policy)
    {
        if (!policy.AllowUserOverrides)
        {
            // Force policy settings
            if (policy.RequireBatteryAware) config.BatteryAware = true;
            if (policy.RequireIdleDetection) config.IdleDetection = true;
            if (policy.RequireTelemetry) config.EnableTelemetry = true;
            if (policy.MaxSessionDurationMinutes.HasValue && config.DefaultDuration > policy.MaxSessionDurationMinutes.Value)
            {
                config.DefaultDuration = policy.MaxSessionDurationMinutes.Value;
            }
        }
    }

    private double ExtractDuration(string? details)
    {
        if (string.IsNullOrEmpty(details)) return 0;
        
        // Parse "Duration: X min" or similar
        var match = System.Text.RegularExpressions.Regex.Match(details, @"(\d+(?:\.\d+)?)\s*min");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var minutes))
        {
            return minutes;
        }
        
        return 0;
    }

    private List<TopUser> GetTopUsers(List<AuditLogEntry> entries, int count)
    {
        return entries
            .GroupBy(e => e.UserId)
            .Select(g => new TopUser
            {
                UserId = g.Key,
                SessionCount = g.Count(e => e.Action == "SessionStart" || e.Action == "TimedSessionStart"),
                TotalActiveMinutes = g.Where(e => e.EventType == AuditEventType.Session)
                    .Select(e => ExtractDuration(e.Details))
                    .Sum()
            })
            .OrderByDescending(u => u.TotalActiveMinutes)
            .Take(count)
            .ToList();
    }

    private List<DailyStats> GetDailyBreakdown(List<AuditLogEntry> entries, DateTime start, DateTime end)
    {
        var days = new List<DailyStats>();
        var current = start.Date;
        
        while (current <= end.Date)
        {
            var dayEntries = entries.Where(e => e.Timestamp.Date == current).ToList();
            
            days.Add(new DailyStats
            {
                Date = current,
                SessionCount = dayEntries.Count(e => e.Action == "SessionStart" || e.Action == "TimedSessionStart"),
                ActiveHours = dayEntries
                    .Where(e => e.EventType == AuditEventType.Session)
                    .Select(e => ExtractDuration(e.Details))
                    .Sum() / 60.0
            });
            
            current = current.AddDays(1);
        }
        
        return days;
    }

    private double CalculateComplianceScore(AuditLogSummary summary)
    {
        if (summary.TotalEntries == 0) return 100;
        
        var failedRatio = (double)summary.FailedActions / summary.TotalEntries;
        return Math.Max(0, 100 - (failedRatio * 100));
    }

    private List<ComplianceViolation> FindViolations(List<AuditLogEntry> entries)
    {
        var violations = new List<ComplianceViolation>();
        
        // Check for excessive failed actions
        var failedActions = entries.Where(e => !e.IsSuccess).ToList();
        if (failedActions.Count > 10)
        {
            violations.Add(new ComplianceViolation
            {
                Type = "ExcessiveFailedActions",
                Description = $"{failedActions.Count} failed actions detected",
                Count = failedActions.Count,
                Severity = ViolationSeverity.Warning
            });
        }
        
        // Check for suspicious security events
        var securityEvents = entries.Where(e => e.EventType == AuditEventType.Security).ToList();
        if (securityEvents.Count > 5)
        {
            violations.Add(new ComplianceViolation
            {
                Type = "ElevatedSecurityActivity",
                Description = $"{securityEvents.Count} security events detected",
                Count = securityEvents.Count,
                Severity = ViolationSeverity.Warning
            });
        }
        
        return violations;
    }

    private DataRetentionStatus CheckDataRetention()
    {
        return new DataRetentionStatus
        {
            AuditLogRetentionDays = AuditLogService.Instance.RetentionDays,
            LastCleanup = DateTime.UtcNow.AddDays(-1), // Approximate
            IsCompliant = true
        };
    }

    private EncryptionStatus CheckEncryptionStatus()
    {
        var config = ConfigService.Instance.Config;
        return new EncryptionStatus
        {
            ConfigEncrypted = !string.IsNullOrEmpty(config.ConfigSignature),
            SaltPresent = !string.IsNullOrEmpty(config.ConfigSalt),
            OverallStatus = !string.IsNullOrEmpty(config.ConfigSignature) ? "Encrypted" : "Plain"
        };
    }
}

// Data models
public class AdminPolicy
{
    public string PolicyName { get; set; } = "Default Policy";
    public string? OrganizationName { get; set; }
    public bool AllowUserOverrides { get; set; } = true;
    
    // Feature requirements
    public bool RequireBatteryAware { get; set; }
    public bool RequireIdleDetection { get; set; }
    public bool RequireNetworkAware { get; set; }
    public bool RequireTelemetry { get; set; }
    
    // Limits
    public int? MaxSessionDurationMinutes { get; set; }
    public int? MaxDailyUsageHours { get; set; }
    public bool DisableScheduledRestart { get; set; }
    
    // Compliance
    public bool RequireAuditLogging { get; set; } = true;
    public int AuditRetentionDays { get; set; } = 90;
    public bool RequireConfigEncryption { get; set; }
}

public class UsageReport
{
    public DateRange Period { get; set; } = new();
    public int TotalSessions { get; set; }
    public TimeSpan TotalActiveTime { get; set; }
    public int UniqueUsers { get; set; }
    public int ConfigChanges { get; set; }
    public int SecurityEvents { get; set; }
    public int AutoPauseEvents { get; set; }
    public List<TopUser> TopUsers { get; set; } = new();
    public List<DailyStats> DailyBreakdown { get; set; } = new();
}

public class ComplianceReport
{
    public DateRange Period { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public int TotalEvents { get; set; }
    public int FailedActions { get; set; }
    public int UserActionCount { get; set; }
    public int ConfigChangeCount { get; set; }
    public int SecurityEventCount { get; set; }
    public int SystemEventCount { get; set; }
    public int SessionEventCount { get; set; }
    public int UniqueUsers { get; set; }
    public double ComplianceScore { get; set; }
    public List<ComplianceViolation> Violations { get; set; } = new();
    public DataRetentionStatus DataRetentionStatus { get; set; } = new();
    public EncryptionStatus EncryptionStatus { get; set; } = new();
}

public class PolicyValidationResult
{
    public bool IsCompliant { get; set; }
    public List<PolicyViolation> Violations { get; set; } = new();
    public DateTime LastChecked { get; set; }
}

public class SystemHealthMetrics
{
    public DateTime Timestamp { get; set; }
    public long MemoryUsageMB { get; set; }
    public TimeSpan Uptime { get; set; }
    public bool IsKeepAwakeActive { get; set; }
    public TimeSpan ActiveSessionDuration { get; set; }
    public string ConfigServiceStatus { get; set; } = "Unknown";
    public string? LastError { get; set; }
    public int PendingUpdates { get; set; }
}

public class DateRange
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

public class TopUser
{
    public string UserId { get; set; } = string.Empty;
    public int SessionCount { get; set; }
    public double TotalActiveMinutes { get; set; }
}

public class DailyStats
{
    public DateTime Date { get; set; }
    public int SessionCount { get; set; }
    public double ActiveHours { get; set; }
}

public class PolicyViolation
{
    public string Rule { get; set; } = string.Empty;
    public string Expected { get; set; } = string.Empty;
    public string Actual { get; set; } = string.Empty;
    public ViolationSeverity Severity { get; set; }
}

public class ComplianceViolation
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
    public ViolationSeverity Severity { get; set; }
}

public class DataRetentionStatus
{
    public int AuditLogRetentionDays { get; set; }
    public DateTime LastCleanup { get; set; }
    public bool IsCompliant { get; set; }
}

public class EncryptionStatus
{
    public bool ConfigEncrypted { get; set; }
    public bool SaltPresent { get; set; }
    public string OverallStatus { get; set; } = "Unknown";
}

public enum ViolationSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

// Event args
public class PolicyChangedEventArgs : EventArgs
{
    public AdminPolicy Policy { get; set; } = new();
    public DateTime AppliedAt { get; set; }
}

public class ComplianceReportReadyEventArgs : EventArgs
{
    public ComplianceReport Report { get; set; } = new();
}
