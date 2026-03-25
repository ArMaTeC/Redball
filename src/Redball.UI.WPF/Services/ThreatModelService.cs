using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Redball.UI.Services;

/// <summary>
/// Represents a threat in the threat model.
/// </summary>
public class ThreatModelEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public ThreatCategory Category { get; set; }
    public RiskLevel Risk { get; set; }
    public string? Mitigation { get; set; }
    public string? TestReference { get; set; }
    public bool IsMitigated { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

/// <summary>
/// Categories of threats in the STRIDE model.
/// </summary>
public enum ThreatCategory
{
    Spoofing,
    Tampering,
    Repudiation,
    InformationDisclosure,
    DenialOfService,
    ElevationOfPrivilege,
    Other
}

/// <summary>
/// Risk levels for threats.
/// </summary>
public enum RiskLevel
{
    Critical,
    High,
    Medium,
    Low,
    Informational
}

/// <summary>
/// Service for managing the application's threat model.
/// Implements sec-5 from improve_me.txt: Threat model document per release with mitigations/tests mapping.
/// </summary>
public class ThreatModelService
{
    private static readonly Lazy<ThreatModelService> _instance = new(() => new ThreatModelService());
    public static ThreatModelService Instance => _instance.Value;

    private readonly List<ThreatModelEntry> _threats = new();
    private readonly object _lock = new();

    /// <summary>
    /// Current version of the threat model document.
    /// </summary>
    public string DocumentVersion { get; private set; } = "1.0.0";

    /// <summary>
    /// Last updated date of the threat model.
    /// </summary>
    public DateTime LastUpdated { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Release version this threat model applies to.
    /// </summary>
    public string TargetReleaseVersion { get; set; } = "";

    private ThreatModelService()
    {
        InitializeDefaultThreatModel();
    }

    /// <summary>
    /// Initializes the default threat model for Redball.
    /// </summary>
    private void InitializeDefaultThreatModel()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        TargetReleaseVersion = version;

        // Add threats based on the security improvements we've implemented
        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-001",
            Title = "API Keys Stored in Plain Text",
            Description = "API keys and credentials stored in plain text configuration files could be exposed to other users or malware.",
            Category = ThreatCategory.InformationDisclosure,
            Risk = RiskLevel.High,
            Mitigation = "Implemented ISecretProvider pattern with Windows Credential Manager backend. All API keys now stored in OS-protected credential store.",
            TestReference = "SecretManagerServiceTests.ValidateCredentialStorage",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-2"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-002",
            Title = "Configuration File Tampering",
            Description = "Malicious software or users could modify the application's configuration file to change behavior or extract sensitive settings.",
            Category = ThreatCategory.Tampering,
            Risk = RiskLevel.High,
            Mitigation = "DPAPI encryption enabled by default. Config files encrypted with CurrentUser scope. Integrity signatures verify config hasn't been modified.",
            TestReference = "ConfigServiceTests.VerifyEncryptedStorage, TamperPolicyServiceTests.ConfigTamperDetection",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-1, sec-4"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-003",
            Title = "Malicious Update Installation",
            Description = "Attacker could distribute malicious update packages that get installed by the auto-updater.",
            Category = ThreatCategory.ElevationOfPrivilege,
            Risk = RiskLevel.Critical,
            Mitigation = "Trust chain validation: Authenticode signature verification, certificate pinning to trusted publishers, manifest hash verification. TamperPolicyService blocks/quarantines based on policy.",
            TestReference = "SecurityServiceTests.ValidateUpdatePackageTrust, UpdateServiceTests.VerifySignatureValidation",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-3, sec-4"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-004",
            Title = "Untrusted Publisher Certificate",
            Description = "Update signed by unknown or untrusted certificate authority could be accepted.",
            Category = ThreatCategory.Spoofing,
            Risk = RiskLevel.High,
            Mitigation = "Certificate thumbprint pinning with runtime extensibility. TamperPolicyService handles CertificateNotPinned events with Warn/Quarantine/Block policies.",
            TestReference = "SecurityServiceTests.CertificatePinningValidation",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-3, sec-4"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-005",
            Title = "Secret Store Corruption",
            Description = "Windows Credential Manager entries could be corrupted or deleted, causing service disruption.",
            Category = ThreatCategory.DenialOfService,
            Risk = RiskLevel.Medium,
            Mitigation = "SecretManagerService with fallback provider support. Health monitoring and graceful degradation when primary store unavailable.",
            TestReference = "SecretManagerServiceTests.FallbackProviderTest",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-2"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-006",
            Title = "Update Download Interception",
            Description = "Man-in-the-middle attack could intercept update download and serve malicious payload.",
            Category = ThreatCategory.Tampering,
            Risk = RiskLevel.High,
            Mitigation = "HTTPS with TLS 1.2+ enforcement. Manifest hash verification ensures downloaded file matches expected hash even if transport compromised.",
            TestReference = "UpdateServiceTests.TlsEnforcement, SecurityServiceTests.ManifestHashValidation",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-3"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-007",
            Title = "Insider Threat - Privileged User",
            Description = "Privileged user with admin access could tamper with application files or configuration.",
            Category = ThreatCategory.ElevationOfPrivilege,
            Risk = RiskLevel.Medium,
            Mitigation = "DPAPI encryption binds config to user identity. TamperPolicyService detects and logs config modifications. Audit trail through tamper event history.",
            TestReference = "TamperPolicyServiceTests.AuditTrailValidation",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-1, sec-4"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-008",
            Title = "Crash Dump Information Disclosure",
            Description = "Crash dumps might contain sensitive information like secrets or API keys.",
            Category = ThreatCategory.InformationDisclosure,
            Risk = RiskLevel.Medium,
            Mitigation = "Secrets stored in Windows Credential Manager (never in memory long-term). Minidumps exclude heap data. No secrets in crash logs.",
            TestReference = "SecurityServiceTests.VerifyNoSecretsInDumps",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-2"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-009",
            Title = "Registry Tampering",
            Description = "Registry-based config storage could be modified by malicious software.",
            Category = ThreatCategory.Tampering,
            Risk = RiskLevel.Medium,
            Mitigation = "Same DPAPI encryption applies to registry storage. Integrity signatures validated on load. TamperPolicyService integration.",
            TestReference = "ConfigServiceTests.RegistryIntegrityValidation",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-1, sec-4"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-010",
            Title = "Downgrade Attack",
            Description = "Attacker might try to force installation of older, vulnerable version.",
            Category = ThreatCategory.Tampering,
            Risk = RiskLevel.Medium,
            Mitigation = "Version check in update service prevents downgrade. Only updates to higher versions allowed.",
            TestReference = "UpdateServiceTests.VersionValidation",
            IsMitigated = true,
            VerifiedBy = "Security Review sec-3"
        });

        // Known unmitigated or partially mitigated threats
        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-011",
            Title = "Physical Access to Unlocked Machine",
            Description = "Attacker with physical access to unlocked machine could access secrets or modify config.",
            Category = ThreatCategory.InformationDisclosure,
            Risk = RiskLevel.High,
            Mitigation = "DPAPI encryption requires user login. Session lock detection can pause sensitive operations. Partial mitigation only - OS-level security required.",
            TestReference = "SessionLockServiceTests.LockDetection",
            IsMitigated = false,
            VerifiedBy = "Security Review - Partial"
        });

        AddThreat(new ThreatModelEntry
        {
            Id = "SEC-012",
            Title = "Memory Dump Analysis",
            Description = "Sophisticated attacker could analyze process memory to extract transient secrets.",
            Category = ThreatCategory.InformationDisclosure,
            Risk = RiskLevel.Medium,
            Mitigation = "Secrets retrieved on-demand from Credential Manager, not cached in memory. Short-lived secret handles. Defense in depth only - OS-level process protection required.",
            TestReference = "Memory profiling analysis",
            IsMitigated = false,
            VerifiedBy = "Security Review - Future enhancement"
        });

        Logger.Info("ThreatModelService", $"Initialized with {_threats.Count} threats for version {TargetReleaseVersion}");
    }

    /// <summary>
    /// Adds a new threat to the model.
    /// </summary>
    public void AddThreat(ThreatModelEntry threat)
    {
        lock (_lock)
        {
            _threats.Add(threat);
        }
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets all threats, optionally filtered by category or mitigation status.
    /// </summary>
    public IReadOnlyList<ThreatModelEntry> GetThreats(
        ThreatCategory? category = null, 
        RiskLevel? minRisk = null,
        bool? isMitigated = null)
    {
        lock (_lock)
        {
            var query = _threats.AsEnumerable();
            
            if (category.HasValue)
                query = query.Where(t => t.Category == category.Value);
            
            if (minRisk.HasValue)
                query = query.Where(t => t.Risk <= minRisk.Value); // Lower enum value = higher risk
            
            if (isMitigated.HasValue)
                query = query.Where(t => t.IsMitigated == isMitigated.Value);
            
            return query.OrderBy(t => t.Risk).ThenBy(t => t.Id).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Gets a specific threat by ID.
    /// </summary>
    public ThreatModelEntry? GetThreatById(string id)
    {
        lock (_lock)
        {
            return _threats.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Updates the mitigation status of a threat.
    /// </summary>
    public bool UpdateMitigationStatus(string threatId, bool isMitigated, string? verifiedBy = null)
    {
        lock (_lock)
        {
            var threat = _threats.FirstOrDefault(t => t.Id.Equals(threatId, StringComparison.OrdinalIgnoreCase));
            if (threat == null) return false;

            threat.IsMitigated = isMitigated;
            threat.VerifiedBy = verifiedBy;
            threat.VerifiedAt = DateTime.UtcNow;
            LastUpdated = DateTime.UtcNow;

            Logger.Info("ThreatModelService", $"Updated threat {threatId}: Mitigated={isMitigated}, VerifiedBy={verifiedBy}");
            return true;
        }
    }

    /// <summary>
    /// Gets summary statistics for the threat model.
    /// </summary>
    public ThreatModelSummary GetSummary()
    {
        lock (_lock)
        {
            return new ThreatModelSummary
            {
                TotalThreats = _threats.Count,
                MitigatedCount = _threats.Count(t => t.IsMitigated),
                UnmitigatedCount = _threats.Count(t => !t.IsMitigated),
                CriticalRiskCount = _threats.Count(t => t.Risk == RiskLevel.Critical && !t.IsMitigated),
                HighRiskCount = _threats.Count(t => t.Risk == RiskLevel.High && !t.IsMitigated),
                LastUpdated = LastUpdated,
                DocumentVersion = DocumentVersion,
                TargetRelease = TargetReleaseVersion
            };
        }
    }

    /// <summary>
    /// Generates the threat model document in Markdown format.
    /// </summary>
    public string GenerateMarkdownDocument()
    {
        var sb = new StringBuilder();
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var summary = GetSummary();

        sb.AppendLine($"# Redball Threat Model Document");
        sb.AppendLine($"**Version:** {DocumentVersion}  ");
        sb.AppendLine($"**Target Release:** {TargetReleaseVersion}  ");
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC  ");
        sb.AppendLine($"**Last Updated:** {LastUpdated:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Total Threats Identified:** {summary.TotalThreats}");
        sb.AppendLine($"- **Mitigated:** {summary.MitigatedCount} ({(summary.MitigatedCount * 100.0 / summary.TotalThreats):F1}%)");
        sb.AppendLine($"- **Unmitigated:** {summary.UnmitigatedCount}");
        sb.AppendLine($"- **Critical Risk (Unmitigated):** {summary.CriticalRiskCount}");
        sb.AppendLine($"- **High Risk (Unmitigated):** {summary.HighRiskCount}");
        sb.AppendLine();
        
        sb.AppendLine("## Threat Inventory");
        sb.AppendLine();
        
        // Group by risk level
        var riskGroups = _threats
            .GroupBy(t => t.Risk)
            .OrderBy(g => g.Key);
        
        foreach (var group in riskGroups)
        {
            sb.AppendLine($"### {group.Key} Risk");
            sb.AppendLine();
            
            foreach (var threat in group.OrderBy(t => t.IsMitigated ? 1 : 0).ThenBy(t => t.Id))
            {
                var status = threat.IsMitigated ? "✅ Mitigated" : "⚠️ Unmitigated";
                sb.AppendLine($"#### {threat.Id}: {threat.Title}");
                sb.AppendLine($"**Status:** {status}  ");
                sb.AppendLine($"**Category:** {threat.Category}  ");
                sb.AppendLine($"**Description:** {threat.Description}");
                
                if (!string.IsNullOrEmpty(threat.Mitigation))
                {
                    sb.AppendLine($"**Mitigation:** {threat.Mitigation}");
                }
                
                if (!string.IsNullOrEmpty(threat.TestReference))
                {
                    sb.AppendLine($"**Test Reference:** `{threat.TestReference}`");
                }
                
                if (!string.IsNullOrEmpty(threat.VerifiedBy))
                {
                    sb.AppendLine($"**Verified By:** {threat.VerifiedBy} on {threat.VerifiedAt:yyyy-MM-dd}");
                }
                
                sb.AppendLine();
            }
        }
        
        sb.AppendLine("## STRIDE Category Summary");
        sb.AppendLine();
        sb.AppendLine("| Category | Count | Mitigated | Unmitigated |");
        sb.AppendLine("|----------|-------|-----------|-------------|");
        
        var categoryGroups = _threats.GroupBy(t => t.Category).OrderBy(g => g.Key.ToString());
        foreach (var group in categoryGroups)
        {
            var total = group.Count();
            var mitigated = group.Count(t => t.IsMitigated);
            var unmitigated = total - mitigated;
            sb.AppendLine($"| {group.Key} | {total} | {mitigated} | {unmitigated} |");
        }
        
        sb.AppendLine();
        sb.AppendLine("## Test Coverage Mapping");
        sb.AppendLine();
        sb.AppendLine("The following security tests validate the mitigations:");
        sb.AppendLine();
        
        var testedThreats = _threats.Where(t => !string.IsNullOrEmpty(t.TestReference)).ToList();
        foreach (var threat in testedThreats)
        {
            sb.AppendLine($"- `{threat.TestReference}` → {threat.Id}: {threat.Title}");
        }
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*This document is automatically generated from the ThreatModelService.");
        sb.AppendLine("It maps security threats to their mitigations and corresponding test coverage.*");

        return sb.ToString();
    }

    /// <summary>
    /// Exports the threat model to a JSON file.
    /// </summary>
    public bool ExportToJson(string filePath)
    {
        try
        {
            var export = new
            {
                DocumentVersion,
                LastUpdated,
                TargetReleaseVersion,
                Summary = GetSummary(),
                Threats = _threats.ToList()
            };

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(filePath, json);
            Logger.Info("ThreatModelService", $"Threat model exported to: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ThreatModelService", "Failed to export threat model", ex);
            return false;
        }
    }

    /// <summary>
    /// Saves the markdown document to a file.
    /// </summary>
    public bool SaveMarkdownDocument(string filePath)
    {
        try
        {
            var markdown = GenerateMarkdownDocument();
            File.WriteAllText(filePath, markdown);
            Logger.Info("ThreatModelService", $"Threat model document saved to: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ThreatModelService", "Failed to save threat model document", ex);
            return false;
        }
    }
}

/// <summary>
/// Summary statistics for the threat model.
/// </summary>
public class ThreatModelSummary
{
    public int TotalThreats { get; set; }
    public int MitigatedCount { get; set; }
    public int UnmitigatedCount { get; set; }
    public int CriticalRiskCount { get; set; }
    public int HighRiskCount { get; set; }
    public DateTime LastUpdated { get; set; }
    public string DocumentVersion { get; set; } = "";
    public string TargetRelease { get; set; } = "";
}
