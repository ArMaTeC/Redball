using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Comprehensive security audit logging service.
/// Tracks all security-relevant events for compliance and forensics.
/// </summary>
public class SecurityAuditService : IDisposable
{
    private static readonly Lazy<SecurityAuditService> _instance = new(() => new SecurityAuditService());
    public static SecurityAuditService Instance => _instance.Value;

    private readonly string _auditLogPath;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly Timer? _flushTimer;
    private readonly List<SecurityAuditEvent> _pendingEvents = new();
    private readonly JsonSerializerOptions _jsonOptions;

    private const int MaxLogSizeMB = 50;
    private const int MaxRetentionDays = 365;
    private const int MaxEventsInMemory = 100;

    private SecurityAuditService()
    {
        var auditDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball", "Audit");
        Directory.CreateDirectory(auditDir);
        
        _auditLogPath = Path.Combine(auditDir, $"security_audit_{DateTime.Now:yyyyMM}.log");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Flush every 30 seconds
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        Logger.Info("SecurityAuditService", $"Audit logging initialized: {_auditLogPath}");
        
        // Log service startup
        LogEvent(AuditEventType.ServiceStarted, "SecurityAudit", new { Version = GetType().Assembly.GetName().Version?.ToString() });
    }

    /// <summary>
    /// Logs a security event with full context.
    /// </summary>
    /// <param name="eventType">Type of security event</param>
    /// <param name="component">Component that generated the event</param>
    /// <param name="details">Additional event details (serialized)</param>
    /// <param name="severity">Event severity</param>
    public void LogEvent(AuditEventType eventType, string component, object? details = null, SecuritySeverity severity = SecuritySeverity.Info)
    {
        var auditEvent = new SecurityAuditEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Component = component,
            Severity = severity,
            Details = details != null ? JsonSerializer.Serialize(details, _jsonOptions) : null,
            SessionId = GetSessionId(),
            UserSid = GetCurrentUserSid(),
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId
        };

        lock (_pendingEvents)
        {
            _pendingEvents.Add(auditEvent);
            
            // Flush if we have too many pending events
            if (_pendingEvents.Count >= MaxEventsInMemory)
            {
                Task.Run(() => Flush());
            }
        }

        // Also log to standard logger for immediate visibility
        var message = $"[AUDIT] {eventType} in {component}";
        switch (severity)
        {
            case SecuritySeverity.Warning:
                Logger.Warning("SecurityAudit", message);
                break;
            case SecuritySeverity.Critical:
                Logger.Error("SecurityAudit", message);
                break;
            default:
                Logger.Debug("SecurityAudit", message);
                break;
        }
    }

    /// <summary>
    /// Logs configuration changes with before/after comparison.
    /// </summary>
    public void LogConfigChange(string configKey, object? oldValue, object? newValue, string changedBy = "User")
    {
        LogEvent(AuditEventType.ConfigChange, "ConfigService", new
        {
            ConfigKey = configKey,
            OldValue = oldValue?.ToString(),
            NewValue = newValue?.ToString(),
            ChangedBy = changedBy
        }, SecuritySeverity.Info);
    }

    /// <summary>
    /// Logs authentication attempts (success or failure).
    /// </summary>
    public void LogAuthentication(string method, bool success, string? identity = null, string? reason = null)
    {
        LogEvent(
            success ? AuditEventType.AuthenticationSuccess : AuditEventType.AuthenticationFailure,
            "Authentication",
            new { Method = method, Identity = identity, Reason = reason },
            success ? SecuritySeverity.Info : SecuritySeverity.Warning);
    }

    /// <summary>
    /// Logs tamper detection events.
    /// </summary>
    public void LogTamperDetection(string detectionMethod, string details, SecuritySeverity severity = SecuritySeverity.Critical)
    {
        LogEvent(AuditEventType.TamperDetected, "Security", new
        {
            DetectionMethod = detectionMethod,
            Details = details,
            Timestamp = DateTime.UtcNow
        }, severity);
    }

    /// <summary>
    /// Logs encryption/decryption operations.
    /// </summary>
    public void LogCryptoOperation(string operation, string algorithm, bool success, string? keyId = null)
    {
        LogEvent(
            success ? AuditEventType.EncryptionOperation : AuditEventType.EncryptionFailure,
            "Crypto",
            new { Operation = operation, Algorithm = algorithm, KeyId = keyId },
            success ? SecuritySeverity.Info : SecuritySeverity.Warning);
    }

    /// <summary>
    /// Logs update operations.
    /// </summary>
    public void LogUpdateEvent(string operation, string version, bool success, string? checksum = null)
    {
        LogEvent(
            success ? AuditEventType.UpdateSuccess : AuditEventType.UpdateFailure,
            "UpdateService",
            new { Operation = operation, Version = version, Checksum = checksum },
            success ? SecuritySeverity.Info : SecuritySeverity.Warning);
    }

    /// <summary>
    /// Logs access control decisions.
    /// </summary>
    public void LogAccessControl(string resource, string action, bool granted, string? identity = null)
    {
        LogEvent(
            granted ? AuditEventType.AccessGranted : AuditEventType.AccessDenied,
            "AccessControl",
            new { Resource = resource, Action = action, Identity = identity },
            granted ? SecuritySeverity.Info : SecuritySeverity.Warning);
    }

    /// <summary>
    /// Flushes pending events to disk.
    /// </summary>
    public void Flush()
    {
        List<SecurityAuditEvent> eventsToFlush;
        
        lock (_pendingEvents)
        {
            if (_pendingEvents.Count == 0) return;
            
            eventsToFlush = new List<SecurityAuditEvent>(_pendingEvents);
            _pendingEvents.Clear();
        }

        _lock.EnterWriteLock();
        try
        {
            var lines = eventsToFlush.Select(e => JsonSerializer.Serialize(e, _jsonOptions));
            File.AppendAllLines(_auditLogPath, lines, Encoding.UTF8);
            
            Logger.Debug("SecurityAuditService", $"Flushed {eventsToFlush.Count} audit events");
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityAuditService", "Failed to flush audit events", ex);
            
            // Re-queue failed events
            lock (_pendingEvents)
            {
                _pendingEvents.InsertRange(0, eventsToFlush);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Queries audit events with filtering.
    /// </summary>
    public List<SecurityAuditEvent> QueryEvents(DateTime? startTime = null, DateTime? endTime = null, AuditEventType? eventType = null, int maxResults = 1000)
    {
        Flush();
        
        _lock.EnterReadLock();
        try
        {
            var events = new List<SecurityAuditEvent>();
            
            // Read all audit files
            var auditDir = Path.GetDirectoryName(_auditLogPath)!;
            var auditFiles = Directory.GetFiles(auditDir, "security_audit_*.log");
            
            foreach (var file in auditFiles.OrderByDescending(f => f))
            {
                if (events.Count >= maxResults) break;
                
                try
                {
                    var lines = File.ReadAllLines(file);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        try
                        {
                            var evt = JsonSerializer.Deserialize<SecurityAuditEvent>(line, _jsonOptions);
                            if (evt == null) continue;
                            
                            // Apply filters
                            if (startTime.HasValue && evt.Timestamp < startTime.Value) continue;
                            if (endTime.HasValue && evt.Timestamp > endTime.Value) continue;
                            if (eventType.HasValue && evt.EventType != eventType.Value) continue;
                            
                            events.Add(evt);
                            if (events.Count >= maxResults) break;
                        }
                        catch { /* Skip malformed lines */ }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("SecurityAuditService", $"Failed to read audit file {file}: {ex.Message}");
                }
            }
            
            return events.OrderByDescending(e => e.Timestamp).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Exports audit logs to a file for compliance purposes.
    /// </summary>
    public bool ExportAuditLog(string exportPath, DateTime? startTime = null, DateTime? endTime = null)
    {
        try
        {
            var events = QueryEvents(startTime, endTime, maxResults: int.MaxValue);
            var export = new AuditExport
            {
                ExportedAt = DateTime.UtcNow,
                TotalEvents = events.Count,
                Events = events
            };
            
            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(exportPath, json, Encoding.UTF8);
            
            Logger.Info("SecurityAuditService", $"Exported {events.Count} audit events to {exportPath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityAuditService", "Failed to export audit log", ex);
            return false;
        }
    }

    /// <summary>
    /// Cleans up old audit logs based on retention policy.
    /// </summary>
    public void CleanupOldLogs()
    {
        try
        {
            var auditDir = Path.GetDirectoryName(_auditLogPath)!;
            var cutoffDate = DateTime.Now.AddDays(-MaxRetentionDays);
            var files = Directory.GetFiles(auditDir, "security_audit_*.log");
            
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate || fileInfo.Length > MaxLogSizeMB * 1024 * 1024)
                    {
                        File.Delete(file);
                        Logger.Info("SecurityAuditService", $"Cleaned up old audit log: {file}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("SecurityAuditService", $"Failed to cleanup {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityAuditService", "Cleanup failed", ex);
        }
    }

    /// <summary>
    /// Gets a summary of security events for the dashboard.
    /// </summary>
    public SecuritySummary GetSecuritySummary(TimeSpan period)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime - period;
        var events = QueryEvents(startTime, endTime, maxResults: 10000);
        
        return new SecuritySummary
        {
            Period = period,
            TotalEvents = events.Count,
            CriticalEvents = events.Count(e => e.Severity == SecuritySeverity.Critical),
            WarningEvents = events.Count(e => e.Severity == SecuritySeverity.Warning),
            AuthenticationFailures = events.Count(e => e.EventType == AuditEventType.AuthenticationFailure),
            TamperDetections = events.Count(e => e.EventType == AuditEventType.TamperDetected),
            ConfigChanges = events.Count(e => e.EventType == AuditEventType.ConfigChange),
            RecentEvents = events.Take(50).ToList()
        };
    }

    private string GetSessionId()
    {
        // Generate a session ID based on process start time
        var pid = Environment.ProcessId;
        var startTime = ProcessInfo.GetProcessStartTime(pid);
        return $"{pid}_{startTime:yyyyMMddHHmmss}";
    }

    private string GetCurrentUserSid()
    {
        try
        {
            return System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    public void Dispose()
    {
        Flush();
        _flushTimer?.Dispose();
        _lock.Dispose();
        Logger.Info("SecurityAuditService", "Service disposed");
    }
}

/// <summary>
/// Security audit event record.
/// </summary>
public class SecurityAuditEvent
{
    public DateTime Timestamp { get; set; }
    public AuditEventType EventType { get; set; }
    public string Component { get; set; } = "";
    public SecuritySeverity Severity { get; set; }
    public string? Details { get; set; }
    public string SessionId { get; set; } = "";
    public string UserSid { get; set; } = "";
    public string MachineName { get; set; } = "";
    public int ProcessId { get; set; }
}

/// <summary>
/// Security summary for dashboard display.
/// </summary>
public class SecuritySummary
{
    public TimeSpan Period { get; set; }
    public int TotalEvents { get; set; }
    public int CriticalEvents { get; set; }
    public int WarningEvents { get; set; }
    public int AuthenticationFailures { get; set; }
    public int TamperDetections { get; set; }
    public int ConfigChanges { get; set; }
    public List<SecurityAuditEvent> RecentEvents { get; set; } = new();
}

/// <summary>
/// Audit export container.
/// </summary>
public class AuditExport
{
    public DateTime ExportedAt { get; set; }
    public int TotalEvents { get; set; }
    public List<SecurityAuditEvent> Events { get; set; } = new();
}

/// <summary>
/// Helper for process information.
/// </summary>
internal static class ProcessInfo
{
    public static DateTime GetProcessStartTime(int processId)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.StartTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }
}
