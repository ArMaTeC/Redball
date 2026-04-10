using Redball.Core.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Advanced Security Policy Service for enterprise deployments.
/// Enforces security policies, compliance checks, and tamper protection.
/// </summary>
public class SecurityPolicyService
{
    private static readonly Lazy<SecurityPolicyService> _instance = new(() => new SecurityPolicyService());
    public static SecurityPolicyService Instance => _instance.Value;

    private SecurityPolicy _currentPolicy;
    private readonly string _policyCachePath;
    private readonly List<SecurityViolation> _violationLog;

    public event EventHandler<SecurityViolationEventArgs>? ViolationDetected;
    public event EventHandler<PolicyChangedEventArgs>? PolicyChanged;

    public SecurityPolicy CurrentPolicy => _currentPolicy;
    public IReadOnlyList<SecurityViolation> ViolationLog => _violationLog.AsReadOnly();
    public bool IsPolicyEnforced => _currentPolicy.IsEnforced;

    private SecurityPolicyService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _policyCachePath = Path.Combine(appData, "Redball", "security-policy.json");
        _violationLog = new List<SecurityViolation>();
        _currentPolicy = new SecurityPolicy(); // Initialize to default before loading
        
        LoadCachedPolicy();
        
        Logger.Verbose("SecurityPolicyService", "Initialized");
    }

    /// <summary>
    /// Loads a security policy from enterprise configuration.
    /// </summary>
    public async Task LoadPolicyAsync(SecurityPolicy policy)
    {
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));

        var oldPolicy = _currentPolicy;
        _currentPolicy = policy;
        
        SaveCachedPolicy();
        
        PolicyChanged?.Invoke(this, new PolicyChangedEventArgs
        {
            OldPolicy = oldPolicy,
            NewPolicy = policy,
            ChangedAt = DateTime.UtcNow
        });

        Logger.Info("SecurityPolicyService", $"Loaded security policy: {policy.PolicyName}");
        
        // Run initial compliance check
        await RunComplianceCheckAsync();
    }

    /// <summary>
    /// Validates current system state against security policy.
    /// </summary>
    public async Task<ComplianceReport> RunComplianceCheckAsync()
    {
        var report = new ComplianceReport
        {
            CheckedAt = DateTime.UtcNow,
            PolicyName = _currentPolicy.PolicyName,
            Checks = new List<ComplianceCheck>()
        };

        // Check encryption at rest
        if (_currentPolicy.RequireConfigEncryption)
        {
            var encryptionCheck = CheckConfigEncryption();
            report.Checks.Add(encryptionCheck);
            
            if (!encryptionCheck.IsCompliant)
            {
                LogViolation(SecurityViolationType.ConfigNotEncrypted, 
                    "Configuration files are not encrypted");
            }
        }

        // Check secure installation path
        if (_currentPolicy.RequireSecureInstallPath)
        {
            var installCheck = CheckSecureInstallation();
            report.Checks.Add(installCheck);
            
            if (!installCheck.IsCompliant)
            {
                LogViolation(SecurityViolationType.InsecureInstallPath,
                    "Application not installed in secure location");
            }
        }

        // Check tamper protection
        if (_currentPolicy.EnableTamperProtection)
        {
            var tamperCheck = await CheckTamperProtectionAsync();
            report.Checks.Add(tamperCheck);
            
            if (!tamperCheck.IsCompliant)
            {
                LogViolation(SecurityViolationType.TamperingDetected,
                    "Application files have been modified");
            }
        }

        // Check audit logging
        if (_currentPolicy.RequireAuditLogging)
        {
            var auditCheck = CheckAuditLogging();
            report.Checks.Add(auditCheck);
            
            if (!auditCheck.IsCompliant)
            {
                LogViolation(SecurityViolationType.AuditLoggingDisabled,
                    "Audit logging is not enabled");
            }
        }

        // Check for prohibited software
        if (_currentPolicy.ProhibitedSoftware?.Any() == true)
        {
            var softwareCheck = CheckProhibitedSoftware();
            report.Checks.Add(softwareCheck);
            
            if (!softwareCheck.IsCompliant)
            {
                LogViolation(SecurityViolationType.ProhibitedSoftwareDetected,
                    $"Prohibited software detected: {softwareCheck.Details}");
            }
        }

        // Calculate overall compliance
        report.IsCompliant = report.Checks.All(c => c.IsCompliant);
        report.ComplianceScore = (double)report.Checks.Count(c => c.IsCompliant) / report.Checks.Count;

        Logger.Info("SecurityPolicyService", 
            $"Compliance check completed: {report.ComplianceScore:P0} compliant");

        return report;
    }

    /// <summary>
    /// Validates an action against current security policy.
    /// </summary>
    public PolicyValidationResult ValidateAction(SecurityAction action, Dictionary<string, object>? context = null)
    {
        var result = new PolicyValidationResult
        {
            Action = action,
            IsAllowed = true,
            ValidationTime = DateTime.UtcNow
        };

        switch (action)
        {
            case SecurityAction.ChangeSettings:
                if (!_currentPolicy.AllowUserConfigChanges)
                {
                    result.IsAllowed = false;
                    result.Reason = "User configuration changes are disabled by policy";
                }
                break;

            case SecurityAction.DisableBatteryAware:
                if (_currentPolicy.RequireBatteryAware)
                {
                    result.IsAllowed = false;
                    result.Reason = "Battery-aware mode is required by security policy";
                }
                break;

            case SecurityAction.DisableIdleDetection:
                if (_currentPolicy.RequireIdleDetection)
                {
                    result.IsAllowed = false;
                    result.Reason = "Idle detection is required by security policy";
                }
                break;

            case SecurityAction.ExceedMaxDuration:
                if (_currentPolicy.MaxSessionDurationMinutes.HasValue && context != null)
                {
                    if (context.TryGetValue("DurationMinutes", out var durationObj) && 
                        durationObj is int duration && 
                        duration > _currentPolicy.MaxSessionDurationMinutes.Value)
                    {
                        result.IsAllowed = false;
                        result.Reason = $"Session exceeds maximum allowed duration of {_currentPolicy.MaxSessionDurationMinutes.Value} minutes";
                    }
                }
                break;

            case SecurityAction.ExportData:
                if (!_currentPolicy.AllowDataExport)
                {
                    result.IsAllowed = false;
                    result.Reason = "Data export is disabled by security policy";
                }
                break;

            case SecurityAction.InstallPlugin:
                if (!_currentPolicy.AllowThirdPartyPlugins)
                {
                    result.IsAllowed = false;
                    result.Reason = "Third-party plugins are prohibited by security policy";
                }
                break;
        }

        if (!result.IsAllowed)
        {
            LogViolation(SecurityViolationType.PolicyViolation, 
                $"Action '{action}' blocked: {result.Reason}");
        }

        return result;
    }

    /// <summary>
    /// Gets the file hash for tamper detection.
    /// </summary>
    public async Task<string> ComputeFileHashAsync(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            await using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hash);
        }
        catch (Exception ex)
        {
            Logger.Error("SecurityPolicyService", $"Failed to compute hash for {filePath}", ex);
            return string.Empty;
        }
    }

    /// <summary>
    /// Reports a security event for audit logging.
    /// </summary>
    public void ReportSecurityEvent(SecurityEventType eventType, string description, string? userId = null)
    {
        var securityEvent = new SecurityEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Description = description,
            UserId = userId ?? Environment.UserName,
            DeviceId = GetDeviceId(),
            Severity = GetEventSeverity(eventType)
        };

        // Log to audit system
        AuditLogService.Instance.LogSecurityEvent(securityEvent);

        // If critical, also notify
        if (securityEvent.Severity == SecuritySeverity.Critical)
        {
            ViolationDetected?.Invoke(this, new SecurityViolationEventArgs
            {
                Violation = new SecurityViolation
                {
                    Timestamp = DateTime.UtcNow,
                    Type = SecurityViolationType.CriticalEvent,
                    Description = description
                }
            });
        }

        Logger.Info("SecurityPolicyService", $"Security event: {eventType} - {description}");
    }

    /// <summary>
    /// Generates a compliance report for enterprise reporting.
    /// </summary>
    public async Task<string> ExportComplianceReportAsync()
    {
        var report = await RunComplianceCheckAsync();
        
        var export = new ComplianceExport
        {
            GeneratedAt = DateTime.UtcNow,
            DeviceId = GetDeviceId(),
            UserName = Environment.UserName,
            Report = report,
            RecentViolations = _violationLog.TakeLast(10).ToList()
        };

        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }

    // Private helper methods

    private ComplianceCheck CheckConfigEncryption()
    {
        // Check if config files are encrypted
        var configPath = ConfigService.Instance.ConfigFilePath;
        var isEncrypted = false;

        if (File.Exists(configPath))
        {
            // Check for encryption markers (DPAPI headers)
            var bytes = File.ReadAllBytes(configPath);
            isEncrypted = bytes.Length > 0 && bytes[0] == 0x01; // DPAPI marker
        }

        return new ComplianceCheck
        {
            Name = "Configuration Encryption",
            IsCompliant = isEncrypted,
            Details = isEncrypted ? "Configuration is encrypted" : "Configuration is not encrypted",
            Severity = CheckSeverity.High
        };
    }

    private ComplianceCheck CheckSecureInstallation()
    {
        var installPath = AppContext.BaseDirectory;
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Secure locations: Program Files, LocalAppData (per-user install)
        var isSecure = installPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) ||
                      installPath.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase);

        // Not secure if in temp or downloads
        var tempPath = Path.GetTempPath();
        var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        
        if (installPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase) ||
            installPath.StartsWith(downloadsPath, StringComparison.OrdinalIgnoreCase))
        {
            isSecure = false;
        }

        return new ComplianceCheck
        {
            Name = "Secure Installation Path",
            IsCompliant = isSecure,
            Details = $"Installed at: {installPath}",
            Severity = CheckSeverity.Medium
        };
    }

    private async Task<ComplianceCheck> CheckTamperProtectionAsync()
    {
        // Compute hash of critical files
        var criticalFiles = new[] { "Redball.exe", "Redball.Core.dll", "Redball.UI.WPF.dll" };
        var installPath = AppContext.BaseDirectory;
        
        var allValid = true;
        var details = new List<string>();

        foreach (var file in criticalFiles)
        {
            var filePath = Path.Combine(installPath, file);
            if (File.Exists(filePath))
            {
                var hash = await ComputeFileHashAsync(filePath);
                // In production, compare against signed manifest
                details.Add($"{file}: {hash[..16]}...");
            }
            else
            {
                allValid = false;
                details.Add($"{file}: MISSING");
            }
        }

        return new ComplianceCheck
        {
            Name = "Tamper Protection",
            IsCompliant = allValid,
            Details = string.Join(", ", details),
            Severity = CheckSeverity.Critical
        };
    }

    private ComplianceCheck CheckAuditLogging()
    {
        var auditEnabled = ConfigService.Instance.Config.EnableAuditLogging;
        
        return new ComplianceCheck
        {
            Name = "Audit Logging",
            IsCompliant = auditEnabled,
            Details = auditEnabled ? "Audit logging enabled" : "Audit logging disabled",
            Severity = CheckSeverity.High
        };
    }

    private ComplianceCheck CheckProhibitedSoftware()
    {
        var prohibited = _currentPolicy.ProhibitedSoftware ?? new List<string>();
        var detected = new List<string>();

        foreach (var software in prohibited)
        {
            // Check for running processes
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(software);
                if (processes.Any())
                {
                    detected.Add(software);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("SecurityPolicyService", $"Process check failed for {software}: {ex.Message}");
            }
        }

        return new ComplianceCheck
        {
            Name = "Prohibited Software",
            IsCompliant = !detected.Any(),
            Details = detected.Any() ? $"Detected: {string.Join(", ", detected)}" : "No prohibited software detected",
            Severity = CheckSeverity.High
        };
    }

    private void LogViolation(SecurityViolationType type, string description)
    {
        var violation = new SecurityViolation
        {
            Timestamp = DateTime.UtcNow,
            Type = type,
            Description = description
        };

        _violationLog.Add(violation);

        ViolationDetected?.Invoke(this, new SecurityViolationEventArgs
        {
            Violation = violation
        });

        Logger.Warning("SecurityPolicyService", $"Security violation: {type} - {description}");
    }

    private void LoadCachedPolicy()
    {
        if (File.Exists(_policyCachePath))
        {
            try
            {
                var json = File.ReadAllText(_policyCachePath);
                // SECURITY: Use SecureJsonSerializer with size limit and max depth
                _currentPolicy = SecureJsonSerializer.Deserialize<SecurityPolicy>(json) ?? new SecurityPolicy();
            }
            catch (Exception ex)
            {
                // SECURITY: Log full details internally
                Logger.Warning("SecurityPolicyService", "Failed to load cached policy", ex);
                _currentPolicy = new SecurityPolicy();
            }
        }
        else
        {
            _currentPolicy = new SecurityPolicy();
        }
    }

    private void SaveCachedPolicy()
    {
        try
        {
            var json = JsonSerializer.Serialize(_currentPolicy, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_policyCachePath, json);
        }
        catch (Exception ex)
        {
            Logger.Warning("SecurityPolicyService", $"Failed to cache policy: {ex.Message}");
        }
    }

    private SecuritySeverity GetEventSeverity(SecurityEventType eventType)
    {
        return eventType switch
        {
            SecurityEventType.PolicyViolation => SecuritySeverity.Warning,
            SecurityEventType.TamperingDetected => SecuritySeverity.Critical,
            SecurityEventType.UnauthorizedAccess => SecuritySeverity.Critical,
            SecurityEventType.ProhibitedSoftware => SecuritySeverity.High,
            SecurityEventType.ConfigChanged => SecuritySeverity.Info,
            SecurityEventType.SessionStarted => SecuritySeverity.Info,
            SecurityEventType.SessionEnded => SecuritySeverity.Info,
            _ => SecuritySeverity.Info
        };
    }

    private string GetDeviceId()
    {
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineName}:{userName}";
        
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).Substring(0, 16).ToLower();
    }
}

// Policy models
public class SecurityPolicy
{
    public string PolicyId { get; set; } = Guid.NewGuid().ToString();
    public string PolicyName { get; set; } = "Default Policy";
    public string OrganizationId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsEnforced { get; set; }
    
    // Security requirements
    public bool RequireConfigEncryption { get; set; }
    public bool RequireSecureInstallPath { get; set; }
    public bool EnableTamperProtection { get; set; }
    public bool RequireAuditLogging { get; set; }
    
    // Feature restrictions
    public bool AllowUserConfigChanges { get; set; } = true;
    public bool AllowDataExport { get; set; } = true;
    public bool AllowThirdPartyPlugins { get; set; } = true;
    
    // Operational policies
    public bool RequireBatteryAware { get; set; }
    public bool RequireIdleDetection { get; set; }
    public int? MaxSessionDurationMinutes { get; set; }
    
    // Software restrictions
    public List<string>? ProhibitedSoftware { get; set; }
    
    // Audit settings
    public int AuditRetentionDays { get; set; } = 90;
}

public class ComplianceCheck
{
    public string Name { get; set; } = string.Empty;
    public bool IsCompliant { get; set; }
    public string Details { get; set; } = string.Empty;
    public CheckSeverity Severity { get; set; }
}

public enum CheckSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}

public class SecurityViolation
{
    public DateTime Timestamp { get; set; }
    public SecurityViolationType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? UserId { get; set; }
}

public enum SecurityViolationType
{
    PolicyViolation,
    ConfigNotEncrypted,
    InsecureInstallPath,
    TamperingDetected,
    AuditLoggingDisabled,
    ProhibitedSoftwareDetected,
    CriticalEvent
}

public class SecurityEvent
{
    public DateTime Timestamp { get; set; }
    public SecurityEventType EventType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public SecuritySeverity Severity { get; set; }
}

public enum SecurityEventType
{
    PolicyViolation,
    TamperingDetected,
    UnauthorizedAccess,
    ProhibitedSoftware,
    ConfigChanged,
    SessionStarted,
    SessionEnded
}

public enum SecuritySeverity
{
    Info,
    Warning,
    High,
    Critical
}

public enum SecurityAction
{
    ChangeSettings,
    DisableBatteryAware,
    DisableIdleDetection,
    ExceedMaxDuration,
    ExportData,
    InstallPlugin
}

public class ComplianceExport
{
    public DateTime GeneratedAt { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public ComplianceReport Report { get; set; } = new();
    public List<SecurityViolation> RecentViolations { get; set; } = new();
}

public class PolicyValidationResult
{
    public SecurityAction Action { get; set; }
    public bool IsAllowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime ValidationTime { get; set; }
}

// Event args
public class PolicyChangedEventArgs : EventArgs
{
    public SecurityPolicy OldPolicy { get; set; } = new();
    public SecurityPolicy NewPolicy { get; set; } = new();
    public DateTime ChangedAt { get; set; }
}

public class SecurityViolationEventArgs : EventArgs
{
    public SecurityViolation Violation { get; set; } = new();
}
