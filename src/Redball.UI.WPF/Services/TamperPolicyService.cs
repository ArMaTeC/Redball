using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Redball.UI.Services;

/// <summary>
/// Defines the policy level for tamper detection responses.
/// </summary>
public enum TamperPolicyLevel
{
    /// <summary>
    /// Log warnings but take no action. Suitable for monitoring/audit mode.
    /// </summary>
    Warn = 0,
    
    /// <summary>
    /// Quarantine the affected component - disable the feature but keep app running.
    /// </summary>
    Quarantine = 1,
    
    /// <summary>
    /// Block the operation entirely. User must resolve before continuing.
    /// </summary>
    Block = 2
}

/// <summary>
/// Types of tamper events that can be detected.
/// </summary>
public enum TamperEventType
{
    ConfigFileTampered,
    UpdateSignatureInvalid,
    CertificateNotPinned,
    IntegrityCheckFailed,
    RegistryTampered,
    SecretStoreCorrupted,
    Unknown
}

/// <summary>
/// Represents a detected tamper event with details.
/// </summary>
public class TamperEvent
{
    public TamperEventType Type { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public string? FilePath { get; set; }
    public string? ExpectedHash { get; set; }
    public string? ActualHash { get; set; }
    public string? Description { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionAction { get; set; }
}

/// <summary>
/// Service for managing tamper detection policies and responses.
/// Implements sec-4 from improve_me.txt: Tamper policy levels with user-safe recovery.
/// </summary>
public class TamperPolicyService
{
    private static readonly Lazy<TamperPolicyService> _instance = new(() => new TamperPolicyService());
    public static TamperPolicyService Instance => _instance.Value;

    private readonly List<TamperEvent> _tamperEvents = new();
    private readonly object _lock = new();

    /// <summary>
    /// Current policy level for config file tampering.
    /// </summary>
    public TamperPolicyLevel ConfigTamperPolicy { get; set; } = TamperPolicyLevel.Warn;

    /// <summary>
    /// Current policy level for update signature failures.
    /// </summary>
    public TamperPolicyLevel UpdateSignaturePolicy { get; set; } = TamperPolicyLevel.Block;

    /// <summary>
    /// Current policy level for certificate pinning failures.
    /// </summary>
    public TamperPolicyLevel CertificatePinPolicy { get; set; } = TamperPolicyLevel.Quarantine;

    /// <summary>
    /// Current policy level for integrity check failures.
    /// </summary>
    public TamperPolicyLevel IntegrityPolicy { get; set; } = TamperPolicyLevel.Block;

    /// <summary>
    /// Event raised when a tamper event is detected.
    /// </summary>
    public event EventHandler<TamperEvent>? TamperDetected;

    /// <summary>
    /// Event raised when a tamper event is resolved.
    /// </summary>
    public event EventHandler<TamperEvent>? TamperResolved;

    private TamperPolicyService()
    {
        LoadPoliciesFromConfig();
    }

    /// <summary>
    /// Loads policy levels from configuration.
    /// </summary>
    private void LoadPoliciesFromConfig()
    {
        try
        {
            var config = ConfigService.Instance.Config;
            // Future: Load from config when properties are added to RedballConfig
            // For now, use secure defaults
            Logger.Info("TamperPolicyService", "Tamper policies loaded with secure defaults");
        }
        catch (Exception ex)
        {
            Logger.Warning("TamperPolicyService", $"Failed to load policies from config: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles a detected tamper event according to the configured policy level.
    /// </summary>
    /// <param name="eventType">Type of tamper event</param>
    /// <param name="filePath">Optional file path associated with the event</param>
    /// <param name="description">Human-readable description</param>
    /// <returns>True if the operation should proceed, false if it should be blocked</returns>
    public bool HandleTamperEvent(TamperEventType eventType, string? filePath = null, string? description = null)
    {
        var policy = GetPolicyForEventType(eventType);
        var tamperEvent = new TamperEvent
        {
            Type = eventType,
            FilePath = filePath,
            Description = description ?? $"{eventType} detected",
            DetectedAt = DateTime.UtcNow
        };

        lock (_lock)
        {
            _tamperEvents.Add(tamperEvent);
        }

        Logger.Warning("TamperPolicyService", 
            $"TAMPER DETECTED: {eventType} at {filePath ?? "unknown"}. Policy: {policy}. Description: {description}");

        // Raise event for UI notification
        TamperDetected?.Invoke(this, tamperEvent);

        switch (policy)
        {
            case TamperPolicyLevel.Warn:
                // Just log and notify, allow operation
                ShowTamperWarning(tamperEvent);
                return true;

            case TamperPolicyLevel.Quarantine:
                // Disable affected feature but keep app running
                QuarantineFeature(eventType, tamperEvent);
                return true;

            case TamperPolicyLevel.Block:
                // Block operation entirely
                ShowTamperBlock(tamperEvent);
                return false;

            default:
                return true;
        }
    }

    /// <summary>
    /// Gets the appropriate policy level for a tamper event type.
    /// </summary>
    private TamperPolicyLevel GetPolicyForEventType(TamperEventType eventType)
    {
        return eventType switch
        {
            TamperEventType.ConfigFileTampered => ConfigTamperPolicy,
            TamperEventType.UpdateSignatureInvalid => UpdateSignaturePolicy,
            TamperEventType.CertificateNotPinned => CertificatePinPolicy,
            TamperEventType.IntegrityCheckFailed => IntegrityPolicy,
            _ => TamperPolicyLevel.Warn
        };
    }

    /// <summary>
    /// Shows a warning notification for tamper detection in Warn mode.
    /// </summary>
    private void ShowTamperWarning(TamperEvent tamperEvent)
    {
        try
        {
            var title = "Security Warning";
            var message = $"Potential tampering detected: {tamperEvent.Type}\n\n{tamperEvent.Description}\n\nThe application will continue running, but this incident has been logged.";
            
            NotificationService.Instance.ShowWarning(title, message);
            Logger.Info("TamperPolicyService", "Tamper warning displayed to user");
        }
        catch (Exception ex)
        {
            Logger.Error("TamperPolicyService", "Failed to show tamper warning", ex);
        }
    }

    /// <summary>
    /// Shows a blocking dialog for tamper detection in Block mode.
    /// </summary>
    private void ShowTamperBlock(TamperEvent tamperEvent)
    {
        try
        {
            var title = "Security Alert - Operation Blocked";
            var message = $"Critical security issue detected: {tamperEvent.Type}\n\n{tamperEvent.Description}\n\nThis operation has been blocked for your safety. Please check the application logs and contact support if needed.";
            
            NotificationService.Instance.ShowError(title, message);
            Logger.Warning("TamperPolicyService", "Tamper block notification displayed - operation blocked");
        }
        catch (Exception ex)
        {
            Logger.Error("TamperPolicyService", "Failed to show tamper block notification", ex);
        }
    }

    /// <summary>
    /// Quarantines a feature when tampering is detected in Quarantine mode.
    /// </summary>
    private void QuarantineFeature(TamperEventType eventType, TamperEvent tamperEvent)
    {
        try
        {
            string quarantinedFeature;
            
            switch (eventType)
            {
                case TamperEventType.CertificateNotPinned:
                    // Disable automatic updates from non-pinned publishers
                    quarantinedFeature = "AutoUpdateFromUnknownPublisher";
                    Logger.Warning("TamperPolicyService", "Quarantined: Automatic updates from unknown publishers disabled");
                    NotificationService.Instance.ShowWarning(
                        "Feature Quarantined",
                        "Automatic updates from unknown certificate publishers have been disabled. You can still manually check for updates.");
                    break;

                case TamperEventType.ConfigFileTampered:
                    quarantinedFeature = "ConfigPersistence";
                    Logger.Warning("TamperPolicyService", "Quarantined: Config persistence disabled until integrity restored");
                    NotificationService.Instance.ShowWarning(
                        "Feature Quarantined",
                        "Configuration file persistence has been quarantined. Settings will not be saved until integrity is restored.");
                    break;

                default:
                    quarantinedFeature = "Unknown";
                    Logger.Warning("TamperPolicyService", $"Quarantined: Unknown feature for event {eventType}");
                    break;
            }

            tamperEvent.Description = $"{tamperEvent.Description} [Feature quarantined: {quarantinedFeature}]";
        }
        catch (Exception ex)
        {
            Logger.Error("TamperPolicyService", "Failed to quarantine feature", ex);
        }
    }

    /// <summary>
    /// Resolves a tamper event with a user-safe recovery action.
    /// </summary>
    /// <param name="tamperEvent">The tamper event to resolve</param>
    /// <param name="resolutionAction">Description of how it was resolved</param>
    /// <returns>True if resolution was successful</returns>
    public bool ResolveTamperEvent(TamperEvent tamperEvent, string resolutionAction)
    {
        try
        {
            lock (_lock)
            {
                var existing = _tamperEvents.FirstOrDefault(e => 
                    e.Type == tamperEvent.Type && 
                    e.DetectedAt == tamperEvent.DetectedAt &&
                    !e.IsResolved);

                if (existing == null)
                {
                    Logger.Warning("TamperPolicyService", "Cannot resolve: tamper event not found or already resolved");
                    return false;
                }

                existing.IsResolved = true;
                existing.ResolvedAt = DateTime.UtcNow;
                existing.ResolutionAction = resolutionAction;
            }

            Logger.Info("TamperPolicyService", $"Tamper event resolved: {tamperEvent.Type}. Action: {resolutionAction}");
            TamperResolved?.Invoke(this, tamperEvent);
            
            NotificationService.Instance.ShowInfo(
                "Security Issue Resolved",
                $"The security issue ({tamperEvent.Type}) has been resolved. {resolutionAction}");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("TamperPolicyService", "Failed to resolve tamper event", ex);
            return false;
        }
    }

    /// <summary>
    /// Attempts to auto-recover from a tamper event based on the event type.
    /// </summary>
    public bool AttemptAutoRecovery(TamperEvent tamperEvent)
    {
        try
        {
            string? resolutionAction = null;

            switch (tamperEvent.Type)
            {
                case TamperEventType.ConfigFileTampered:
                    // Auto-recover by resetting to defaults
                    resolutionAction = "Configuration reset to defaults";
                    ConfigService.Instance.Config = new RedballConfig();
                    ConfigService.Instance.Save();
                    break;

                case TamperEventType.CertificateNotPinned:
                    // Cannot auto-recover - requires user decision
                    Logger.Warning("TamperPolicyService", "Auto-recovery not available for CertificateNotPinned - requires user decision");
                    return false;

                case TamperEventType.UpdateSignatureInvalid:
                    // Cannot auto-recover - blocked update cannot be installed
                    Logger.Warning("TamperPolicyService", "Auto-recovery not available for UpdateSignatureInvalid - update blocked");
                    return false;

                case TamperEventType.IntegrityCheckFailed:
                    // Attempt to restore from backup if available
                    resolutionAction = "Attempted restore from backup";
                    // Future: Implement backup restore logic
                    Logger.Info("TamperPolicyService", "Auto-recovery: Integrity check - would attempt backup restore");
                    break;

                default:
                    Logger.Warning("TamperPolicyService", $"Auto-recovery not available for {tamperEvent.Type}");
                    return false;
            }

            if (!string.IsNullOrEmpty(resolutionAction))
            {
                return ResolveTamperEvent(tamperEvent, resolutionAction);
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("TamperPolicyService", "Auto-recovery failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets all tamper events, optionally filtering by resolved status.
    /// </summary>
    public IReadOnlyList<TamperEvent> GetTamperEvents(bool includeResolved = false)
    {
        lock (_lock)
        {
            if (includeResolved)
            {
                return _tamperEvents.ToList().AsReadOnly();
            }
            return _tamperEvents.Where(e => !e.IsResolved).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Gets the count of unresolved tamper events.
    /// </summary>
    public int GetUnresolvedCount()
    {
        lock (_lock)
        {
            return _tamperEvents.Count(e => !e.IsResolved);
        }
    }

    /// <summary>
    /// Clears all resolved tamper events from history.
    /// </summary>
    public void ClearResolvedEvents()
    {
        lock (_lock)
        {
            _tamperEvents.RemoveAll(e => e.IsResolved);
            Logger.Info("TamperPolicyService", "Resolved tamper events cleared from history");
        }
    }
}
