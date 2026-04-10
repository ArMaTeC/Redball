using Redball.Core.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Audit logging service for compliance and security tracking.
/// Records user actions, system events, and configuration changes.
/// </summary>
public class AuditLogService
{
    private static readonly Lazy<AuditLogService> _instance = new(() => new AuditLogService());
    public static AuditLogService Instance => _instance.Value;

    private readonly string _auditLogDir;
    private readonly string _currentLogFile;
    private readonly object _lock = new();
    
    // Default retention: 90 days for compliance
    public int RetentionDays { get; set; } = 90;
    
    // Maximum log file size before rotation (10MB)
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    private AuditLogService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _auditLogDir = Path.Combine(localAppData, "Redball", "AuditLogs");
        
        if (!Directory.Exists(_auditLogDir))
        {
            Directory.CreateDirectory(_auditLogDir);
        }
        
        _currentLogFile = Path.Combine(_auditLogDir, $"audit_{DateTime.Now:yyyyMM}.log");
        
        Logger.Verbose("AuditLogService", $"Audit log directory: {_auditLogDir}");
    }

    /// <summary>
    /// Records a user action in the audit log.
    /// </summary>
    public void LogUserAction(string action, string details, string? userId = null)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.UserAction,
            Action = action,
            Details = details,
            UserId = userId ?? Environment.UserName,
            MachineName = Environment.MachineName
        };
        
        WriteEntry(entry);
    }

    /// <summary>
    /// Records a configuration change.
    /// </summary>
    public void LogConfigChange(string settingName, string? oldValue, string? newValue, string? userId = null)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.ConfigChange,
            Action = "ConfigChange",
            Details = $"{settingName}: '{oldValue}' -> '{newValue}'",
            UserId = userId ?? Environment.UserName,
            MachineName = Environment.MachineName
        };
        
        WriteEntry(entry);
    }

    /// <summary>
    /// Records a security-related event.
    /// </summary>
    public void LogSecurityEvent(string action, string details, bool isSuccess, string? userId = null)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.Security,
            Action = action,
            Details = details,
            IsSuccess = isSuccess,
            UserId = userId ?? Environment.UserName,
            MachineName = Environment.MachineName
        };
        
        WriteEntry(entry);
    }

    /// <summary>
    /// Records a security event from the security policy service.
    /// </summary>
    public void LogSecurityEvent(SecurityEvent securityEvent)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = securityEvent.Timestamp,
            EventType = AuditEventType.Security,
            Action = securityEvent.EventType.ToString(),
            Details = securityEvent.Description,
            IsSuccess = true,
            UserId = securityEvent.UserId,
            MachineName = securityEvent.DeviceId
        };
        
        WriteEntry(entry);
    }

    /// <summary>
    /// Records system-level events (start, stop, errors).
    /// </summary>
    public void LogSystemEvent(string action, string details, bool isSuccess = true)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.System,
            Action = action,
            Details = details,
            IsSuccess = isSuccess,
            UserId = "SYSTEM",
            MachineName = Environment.MachineName
        };
        
        WriteEntry(entry);
    }

    /// <summary>
    /// Records a keep-awake session event.
    /// </summary>
    public void LogSessionEvent(string action, TimeSpan? duration = null, string? details = null)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.Session,
            Action = action,
            Details = details ?? (duration.HasValue ? $"Duration: {duration.Value.TotalMinutes:F1} min" : null),
            UserId = Environment.UserName,
            MachineName = Environment.MachineName
        };
        
        WriteEntry(entry);
    }

    private void WriteEntry(AuditLogEntry entry)
    {
        lock (_lock)
        {
            try
            {
                // Check if rotation needed
                RotateLogIfNeeded();
                
                // Write entry as JSON line
                var json = JsonSerializer.Serialize(entry);
                File.AppendAllText(_currentLogFile, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.Error("AuditLogService", $"Failed to write audit entry: {ex.Message}");
            }
        }
    }

    private void RotateLogIfNeeded()
    {
        try
        {
            if (File.Exists(_currentLogFile))
            {
                var fileInfo = new FileInfo(_currentLogFile);
                
                // Rotate if file exceeds max size
                if (fileInfo.Length > MaxFileSizeBytes)
                {
                    var rotatedName = Path.Combine(_auditLogDir, $"audit_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    File.Move(_currentLogFile, rotatedName);
                    Logger.Info("AuditLogService", $"Log rotated: {rotatedName}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("AuditLogService", $"Log rotation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets audit log entries for a date range.
    /// </summary>
    public List<AuditLogEntry> GetEntries(DateTime? startDate = null, DateTime? endDate = null, AuditEventType? eventType = null)
    {
        var entries = new List<AuditLogEntry>();
        
        try
        {
            var start = startDate ?? DateTime.UtcNow.AddDays(-7);
            var end = endDate ?? DateTime.UtcNow;
            
            // Get all log files that might contain entries in the range
            var logFiles = Directory.GetFiles(_auditLogDir, "audit_*.log")
                .Select(f => new FileInfo(f))
                .Where(f => f.LastWriteTimeUtc >= start && f.CreationTimeUtc <= end)
                .OrderBy(f => f.CreationTimeUtc);
            
            foreach (var file in logFiles)
            {
                try
                {
                    var lines = File.ReadAllLines(file.FullName);
                    foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                    {
                        try
                        {
                            // SECURITY: Use SecureJsonSerializer with size limit and max depth
                            var entry = SecureJsonSerializer.Deserialize<AuditLogEntry>(line);
                            if (entry != null &&
                                entry.Timestamp >= start &&
                                entry.Timestamp <= end &&
                                (eventType == null || entry.EventType == eventType))
                            {
                                entries.Add(entry);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug("AuditLogService", $"Skipping malformed audit log line: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("AuditLogService", $"Failed to read log file {file.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AuditLogService", $"Failed to retrieve entries: {ex.Message}");
        }
        
        return entries.OrderBy(e => e.Timestamp).ToList();
    }

    /// <summary>
    /// Exports audit logs to CSV format for compliance reporting.
    /// </summary>
    public string ExportToCsv(DateTime? startDate = null, DateTime? endDate = null)
    {
        var entries = GetEntries(startDate, endDate);
        
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Timestamp,EventType,Action,Details,UserId,MachineName,IsSuccess");
        
        foreach (var entry in entries)
        {
            csv.AppendLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss},{entry.EventType},{entry.Action},\"{entry.Details?.Replace("\"", "\"\"")}\",{entry.UserId},{entry.MachineName},{entry.IsSuccess}");
        }
        
        return csv.ToString();
    }

    /// <summary>
    /// Cleans up old audit logs beyond retention period.
    /// </summary>
    public int CleanupOldLogs()
    {
        var deletedCount = 0;
        
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-RetentionDays);
            var logFiles = Directory.GetFiles(_auditLogDir, "audit_*.log");
            
            foreach (var file in logFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < cutoffDate)
                    {
                        File.Delete(file);
                        deletedCount++;
                        Logger.Info("AuditLogService", $"Deleted old audit log: {fileInfo.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("AuditLogService", $"Failed to delete {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AuditLogService", $"Cleanup failed: {ex.Message}");
        }
        
        return deletedCount;
    }

    /// <summary>
    /// Gets summary statistics for the audit log.
    /// </summary>
    public AuditLogSummary GetSummary(DateTime? startDate = null, DateTime? endDate = null)
    {
        var entries = GetEntries(startDate, endDate);
        
        return new AuditLogSummary
        {
            TotalEntries = entries.Count,
            StartDate = entries.FirstOrDefault()?.Timestamp ?? DateTime.UtcNow,
            EndDate = entries.LastOrDefault()?.Timestamp ?? DateTime.UtcNow,
            UserActionCount = entries.Count(e => e.EventType == AuditEventType.UserAction),
            ConfigChangeCount = entries.Count(e => e.EventType == AuditEventType.ConfigChange),
            SecurityEventCount = entries.Count(e => e.EventType == AuditEventType.Security),
            SystemEventCount = entries.Count(e => e.EventType == AuditEventType.System),
            SessionEventCount = entries.Count(e => e.EventType == AuditEventType.Session),
            UniqueUsers = entries.Select(e => e.UserId).Distinct().Count(),
            FailedActions = entries.Count(e => !e.IsSuccess)
        };
    }
}

public enum AuditEventType
{
    UserAction,
    ConfigChange,
    Security,
    System,
    Session,
    // Security audit specific types
    ServiceStarted,
    AuthenticationSuccess,
    AuthenticationFailure,
    EncryptionOperation,
    EncryptionFailure,
    TamperDetected,
    UpdateSuccess,
    UpdateFailure,
    AccessGranted,
    AccessDenied,
    KeyCredentialCreated,
    KeyCredentialDeleted,
    SuspiciousActivity,
    IntegrityViolation
}

public class AuditLogEntry
{
    public DateTime Timestamp { get; set; }
    public AuditEventType EventType { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public bool IsSuccess { get; set; } = true;
}

public class AuditLogSummary
{
    public int TotalEntries { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int UserActionCount { get; set; }
    public int ConfigChangeCount { get; set; }
    public int SecurityEventCount { get; set; }
    public int SystemEventCount { get; set; }
    public int SessionEventCount { get; set; }
    public int UniqueUsers { get; set; }
    public int FailedActions { get; set; }
}
