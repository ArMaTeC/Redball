using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Update channel (rollout ring).
/// </summary>
public enum UpdateChannel
{
    /// <summary>Internal testing only.</summary>
    Canary,
    /// <summary>Early adopters and power users.</summary>
    Beta,
    /// <summary>General availability.</summary>
    Stable,
    /// <summary>Enterprise controlled rollout.</summary>
    Enterprise
}

/// <summary>
/// Rollout cohort configuration.
/// </summary>
public class RolloutCohort
{
    /// <summary>
    /// Cohort identifier (e.g., "cohort-a", "cohort-b").
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Human-readable name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Percentage of users in this cohort (0-100).
    /// </summary>
    public int Percentage { get; set; }

    /// <summary>
    /// Target update channel.
    /// </summary>
    public UpdateChannel Channel { get; set; }

    /// <summary>
    /// Whether this cohort receives updates automatically.
    /// </summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>
    /// Rollback criteria for this cohort.
    /// </summary>
    public RollbackCriteria RollbackTriggers { get; set; } = new();
}

/// <summary>
/// Criteria for automatic rollback.
/// </summary>
public class RollbackCriteria
{
    /// <summary>
    /// Max acceptable crash rate %.
    /// </summary>
    public double MaxCrashRate { get; set; } = 5.0;

    /// <summary>
    /// Max acceptable error rate %.
    /// </summary>
    public double MaxErrorRate { get; set; } = 10.0;

    /// <summary>
    /// Min acceptable success rate %.
    /// </summary>
    public double MinSuccessRate { get; set; } = 95.0;

    /// <summary>
    /// Monitoring window for evaluation.
    /// </summary>
    public TimeSpan EvaluationWindow { get; set; } = TimeSpan.FromHours(4);
}

/// <summary>
/// Update rollout status.
/// </summary>
public class RolloutStatus
{
    /// <summary>
    /// Version being rolled out.
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// Target channel.
    /// </summary>
    public UpdateChannel Channel { get; set; }

    /// <summary>
    /// Rollout start time.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Current rollout percentage (0-100).
    /// </summary>
    public int CurrentPercentage { get; set; }

    /// <summary>
    /// Target rollout percentage.
    /// </summary>
    public int TargetPercentage { get; set; }

    /// <summary>
    /// Whether rollout is paused.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// Whether rollout is complete.
    /// </summary>
    public bool IsComplete => CurrentPercentage >= TargetPercentage;

    /// <summary>
    /// Health metrics for this rollout.
    /// </summary>
    public RolloutHealth Health { get; set; } = new();

    /// <summary>
    /// Rollback info if triggered.
    /// </summary>
    public RollbackInfo? Rollback { get; set; }
}

/// <summary>
/// Health metrics for a rollout.
/// </summary>
public class RolloutHealth
{
    public int TotalInstalls { get; set; }
    public int SuccessfulInstalls { get; set; }
    public int FailedInstalls { get; set; }
    public int Rollbacks { get; set; }
    public double SuccessRate => TotalInstalls > 0 ? (double)SuccessfulInstalls / TotalInstalls * 100 : 0;
    public double CrashRate { get; set; }
    public double ErrorRate { get; set; }
    public bool IsHealthy => SuccessRate >= 95 && CrashRate < 5 && ErrorRate < 10;
}

/// <summary>
/// Rollback information.
/// </summary>
public class RollbackInfo
{
    public DateTime TriggeredAt { get; set; }
    public string Reason { get; set; } = "";
    public string TriggeredBy { get; set; } = ""; // "auto" or "manual"
    public string PreviousVersion { get; set; } = "";
}

/// <summary>
/// Service for managing staged rollouts with cohort-based rollback.
/// Implements dist-1 from improve_me.txt: Staged rollout channels with cohort-based rollback.
/// </summary>
public class RolloutService
{
    private static readonly Lazy<RolloutService> _instance = new(() => new RolloutService());
    public static RolloutService Instance => _instance.Value;

    private readonly Dictionary<UpdateChannel, List<RolloutCohort>> _cohorts = new();
    private readonly Dictionary<string, RolloutStatus> _activeRollouts = new();
    private readonly object _lock = new();

    public event EventHandler<RolloutStatus>? RolloutStatusChanged;
    public event EventHandler<RollbackInfo>? RollbackTriggered;

    private RolloutService()
    {
        InitializeDefaultCohorts();
        Logger.Info("RolloutService", "Rollout service initialized");
    }

    /// <summary>
    /// Gets the user's assigned update channel.
    /// </summary>
    public UpdateChannel GetUserChannel(string userId)
    {
        // Determine channel based on user hash and cohort assignment
        var hash = userId.GetHashCode();
        var normalized = Math.Abs(hash % 100);

        lock (_lock)
        {
            // Check canary (first 5%)
            if (normalized < 5 && _cohorts[UpdateChannel.Canary].Any(c => c.Percentage > 0))
                return UpdateChannel.Canary;

            // Check beta (next 15%)
            if (normalized < 20 && _cohorts[UpdateChannel.Beta].Any(c => c.Percentage > 0))
                return UpdateChannel.Beta;

            // Default to stable
            return UpdateChannel.Stable;
        }
    }

    /// <summary>
    /// Gets the cohort for a user.
    /// </summary>
    public RolloutCohort? GetUserCohort(string userId)
    {
        var channel = GetUserChannel(userId);

        lock (_lock)
        {
            var cohorts = _cohorts[channel];
            var hash = userId.GetHashCode();
            var normalized = Math.Abs(hash % 100);

            var cumulative = 0;
            foreach (var cohort in cohorts.OrderBy(c => c.Id))
            {
                cumulative += cohort.Percentage;
                if (normalized < cumulative)
                    return cohort;
            }

            return cohorts.FirstOrDefault();
        }
    }

    /// <summary>
    /// Starts a new rollout.
    /// </summary>
    public RolloutStatus StartRollout(string version, UpdateChannel channel, int targetPercentage)
    {
        var status = new RolloutStatus
        {
            Version = version,
            Channel = channel,
            StartedAt = DateTime.Now,
            TargetPercentage = targetPercentage,
            CurrentPercentage = 0
        };

        lock (_lock)
        {
            _activeRollouts[version] = status;
        }

        Logger.Info("RolloutService", $"Started rollout: {version} to {channel} channel ({targetPercentage}%)");
        RolloutStatusChanged?.Invoke(this, status);

        return status;
    }

    /// <summary>
    /// Updates rollout progress.
    /// </summary>
    public void UpdateProgress(string version, int percentage)
    {
        lock (_lock)
        {
            if (!_activeRollouts.TryGetValue(version, out var status))
                return;

            status.CurrentPercentage = Math.Min(percentage, status.TargetPercentage);
        }

        RolloutStatusChanged?.Invoke(this, _activeRollouts[version]);
    }

    /// <summary>
    /// Pauses a rollout.
    /// </summary>
    public void PauseRollout(string version)
    {
        lock (_lock)
        {
            if (_activeRollouts.TryGetValue(version, out var status))
            {
                status.IsPaused = true;
                Logger.Warning("RolloutService", $"Rollout paused: {version}");
                RolloutStatusChanged?.Invoke(this, status);
            }
        }
    }

    /// <summary>
    /// Resumes a rollout.
    /// </summary>
    public void ResumeRollout(string version)
    {
        lock (_lock)
        {
            if (_activeRollouts.TryGetValue(version, out var status))
            {
                status.IsPaused = false;
                Logger.Info("RolloutService", $"Rollout resumed: {version}");
                RolloutStatusChanged?.Invoke(this, status);
            }
        }
    }

    /// <summary>
    /// Records install success for a rollout.
    /// </summary>
    public void RecordInstallSuccess(string version)
    {
        lock (_lock)
        {
            if (_activeRollouts.TryGetValue(version, out var status))
            {
                status.Health.TotalInstalls++;
                status.Health.SuccessfulInstalls++;
                EvaluateHealth(status);
            }
        }
    }

    /// <summary>
    /// Records install failure for a rollout.
    /// </summary>
    public void RecordInstallFailure(string version)
    {
        lock (_lock)
        {
            if (_activeRollouts.TryGetValue(version, out var status))
            {
                status.Health.TotalInstalls++;
                status.Health.FailedInstalls++;
                EvaluateHealth(status);
            }
        }
    }

    /// <summary>
    /// Records a crash for a rollout.
    /// </summary>
    public void RecordCrash(string version)
    {
        lock (_lock)
        {
            if (_activeRollouts.TryGetValue(version, out var status))
            {
                status.Health.CrashRate = (status.Health.CrashRate * status.Health.TotalInstalls + 1) / (status.Health.TotalInstalls + 1);
                EvaluateHealth(status);
            }
        }
    }

    /// <summary>
    /// Manually triggers a rollback.
    /// </summary>
    public void TriggerRollback(string version, string reason, string previousVersion)
    {
        lock (_lock)
        {
            if (_activeRollouts.TryGetValue(version, out var status))
            {
                var rollback = new RollbackInfo
                {
                    TriggeredAt = DateTime.Now,
                    Reason = reason,
                    TriggeredBy = "manual",
                    PreviousVersion = previousVersion
                };

                status.Rollback = rollback;
                status.IsPaused = true;

                Logger.Error("RolloutService", $"Rollback triggered for {version}: {reason}");
                RollbackTriggered?.Invoke(this, rollback);
                RolloutStatusChanged?.Invoke(this, status);
            }
        }
    }

    /// <summary>
    /// Gets active rollouts.
    /// </summary>
    public IReadOnlyList<RolloutStatus> GetActiveRollouts()
    {
        lock (_lock)
        {
            return _activeRollouts.Values.Where(r => !r.IsComplete && r.Rollback == null).ToList();
        }
    }

    /// <summary>
    /// Gets rollout status for a version.
    /// </summary>
    public RolloutStatus? GetRolloutStatus(string version)
    {
        lock (_lock)
        {
            return _activeRollouts.TryGetValue(version, out var status) ? status : null;
        }
    }

    /// <summary>
    /// Gets cohort configuration for a channel.
    /// </summary>
    public IReadOnlyList<RolloutCohort> GetCohorts(UpdateChannel channel)
    {
        lock (_lock)
        {
            return _cohorts.TryGetValue(channel, out var cohorts) ? cohorts.ToList() : new List<RolloutCohort>();
        }
    }

    private void InitializeDefaultCohorts()
    {
        // Canary channel - 5% of users, internal testing
        _cohorts[UpdateChannel.Canary] = new List<RolloutCohort>
        {
            new()
            {
                Id = "canary-internal",
                Name = "Internal Testers",
                Percentage = 100,
                Channel = UpdateChannel.Canary,
                AutoUpdate = true,
                RollbackTriggers = new RollbackCriteria { MaxCrashRate = 2.0, MaxErrorRate = 5.0 }
            }
        };

        // Beta channel - 15% of users, early adopters
        _cohorts[UpdateChannel.Beta] = new List<RolloutCohort>
        {
            new()
            {
                Id = "beta-a",
                Name = "Early Adopters A",
                Percentage = 50,
                Channel = UpdateChannel.Beta,
                AutoUpdate = true,
                RollbackTriggers = new RollbackCriteria { MaxCrashRate = 3.0, MaxErrorRate = 8.0 }
            },
            new()
            {
                Id = "beta-b",
                Name = "Early Adopters B",
                Percentage = 50,
                Channel = UpdateChannel.Beta,
                AutoUpdate = true,
                RollbackTriggers = new RollbackCriteria { MaxCrashRate = 3.0, MaxErrorRate = 8.0 }
            }
        };

        // Stable channel - 80% of users, gradual rollout
        _cohorts[UpdateChannel.Stable] = new List<RolloutCohort>
        {
            new()
            {
                Id = "stable-10",
                Name = "Stable 10%",
                Percentage = 10,
                Channel = UpdateChannel.Stable,
                AutoUpdate = false, // Manual approval for stable
                RollbackTriggers = new RollbackCriteria { MaxCrashRate = 1.0, MaxErrorRate = 5.0 }
            },
            new()
            {
                Id = "stable-50",
                Name = "Stable 50%",
                Percentage = 40,
                Channel = UpdateChannel.Stable,
                AutoUpdate = false,
                RollbackTriggers = new RollbackCriteria { MaxCrashRate = 1.0, MaxErrorRate = 5.0 }
            },
            new()
            {
                Id = "stable-all",
                Name = "Stable Full",
                Percentage = 50,
                Channel = UpdateChannel.Stable,
                AutoUpdate = false,
                RollbackTriggers = new RollbackCriteria { MaxCrashRate = 1.0, MaxErrorRate = 5.0 }
            }
        };

        // Enterprise channel - controlled by IT admin
        _cohorts[UpdateChannel.Enterprise] = new List<RolloutCohort>
        {
            new()
            {
                Id = "enterprise-pilot",
                Name = "Enterprise Pilot",
                Percentage = 10,
                Channel = UpdateChannel.Enterprise,
                AutoUpdate = false,
                RollbackTriggers = new RollbackCriteria { MaxCrashRate = 0.5, MaxErrorRate = 2.0 }
            },
            new()
            {
                Id = "enterprise-all",
                Name = "Enterprise Full",
                Percentage = 90,
                Channel = UpdateChannel.Enterprise,
                AutoUpdate = false,
                RollbackTriggers = new RollbackCriteria { MaxCrashRate = 0.5, MaxErrorRate = 2.0 }
            }
        };
    }

    private void EvaluateHealth(RolloutStatus status)
    {
        // Auto-rollback if health criteria exceeded
        if (status.Health.CrashRate > 5.0)
        {
            TriggerAutoRollback(status, $"Crash rate {status.Health.CrashRate:F1}% exceeded threshold 5%");
        }
        else if (status.Health.ErrorRate > 10.0)
        {
            TriggerAutoRollback(status, $"Error rate {status.Health.ErrorRate:F1}% exceeded threshold 10%");
        }
        else if (status.Health.SuccessRate < 95.0)
        {
            TriggerAutoRollback(status, $"Success rate {status.Health.SuccessRate:F1}% below threshold 95%");
        }
    }

    private void TriggerAutoRollback(RolloutStatus status, string reason)
    {
        if (status.Rollback != null) return; // Already rolled back

        var rollback = new RollbackInfo
        {
            TriggeredAt = DateTime.Now,
            Reason = reason,
            TriggeredBy = "auto",
            PreviousVersion = GetPreviousVersion(status.Version)
        };

        status.Rollback = rollback;
        status.IsPaused = true;

        Logger.Error("RolloutService", $"Auto-rollback triggered for {status.Version}: {reason}");
        RollbackTriggered?.Invoke(this, rollback);
        RolloutStatusChanged?.Invoke(this, status);
    }

    private string GetPreviousVersion(string currentVersion)
    {
        // In real implementation, this would query update service
        // For now, return placeholder
        var parts = currentVersion.Split('.');
        if (parts.Length >= 3 && int.TryParse(parts[2], out var patch))
        {
            parts[2] = (patch - 1).ToString();
            return string.Join(".", parts);
        }
        return currentVersion;
    }
}
