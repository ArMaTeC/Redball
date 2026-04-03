using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Feature tier enumeration.
/// </summary>
public enum FeatureTier
{
    Core,       // Always available, stable
    Pro,        // Paid/Advanced features
    Experimental  // Beta/preview features
}

/// <summary>
/// Feature kill criteria.
/// </summary>
public class KillCriteria
{
    public int MaxBugCount { get; set; }
    public double MinAdoptionRate { get; set; }
    public int MaxSupportTickets { get; set; }
    public DateTime? SunsetDate { get; set; }
}

/// <summary>
/// Tiered feature definition.
/// </summary>
public class TieredFeature
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public FeatureTier Tier { get; set; }
    public string Description { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public KillCriteria? KillCriteria { get; set; }
    public List<string> RequiredFeatures { get; set; } = new();
    public DateTime? LaunchDate { get; set; }
    public int CurrentBugCount { get; set; }
    public double AdoptionRate { get; set; }
    public int SupportTicketCount { get; set; }
    public bool IsMarkedForRemoval { get; set; }
    public string? RemovalReason { get; set; }
}

/// <summary>
/// Service for managing feature tiers and kill criteria.
/// Implements strat-3 from improve_me.txt: Introduce feature-tiering with kill criteria.
/// </summary>
public class FeatureTieringService
{
    private static readonly Lazy<FeatureTieringService> _instance = new(() => new FeatureTieringService());
    public static FeatureTieringService Instance => _instance.Value;

    private readonly List<TieredFeature> _features = new();

    private FeatureTieringService()
    {
        InitializeDefaultFeatures();
        Logger.Info("FeatureTieringService", "Feature tiering service initialized");
    }

    /// <summary>
    /// Gets all features.
    /// </summary>
    public IReadOnlyList<TieredFeature> GetAllFeatures()
    {
        return _features.ToList();
    }

    /// <summary>
    /// Gets features by tier.
    /// </summary>
    public IReadOnlyList<TieredFeature> GetFeaturesByTier(FeatureTier tier)
    {
        return _features.Where(f => f.Tier == tier && f.IsEnabled && !f.IsMarkedForRemoval).ToList();
    }

    /// <summary>
    /// Gets available features for a user.
    /// </summary>
    public IReadOnlyList<TieredFeature> GetAvailableFeatures(bool isProUser, bool enableExperimental)
    {
        return _features.Where(f =>
        {
            if (!f.IsEnabled || f.IsMarkedForRemoval) return false;
            if (f.Tier == FeatureTier.Core) return true;
            if (f.Tier == FeatureTier.Pro) return isProUser;
            if (f.Tier == FeatureTier.Experimental) return enableExperimental;
            return false;
        }).ToList();
    }

    /// <summary>
    /// Registers a feature.
    /// </summary>
    public void RegisterFeature(TieredFeature feature)
    {
        _features.Add(feature);
        Logger.Info("FeatureTieringService", $"Feature registered: {feature.Name} ({feature.Tier})");
    }

    /// <summary>
    /// Updates feature telemetry.
    /// </summary>
    public void UpdateTelemetry(string featureId, int? bugCount = null, double? adoptionRate = null, int? ticketCount = null)
    {
        var feature = _features.FirstOrDefault(f => f.Id == featureId);
        if (feature == null) return;

        if (bugCount.HasValue) feature.CurrentBugCount = bugCount.Value;
        if (adoptionRate.HasValue) feature.AdoptionRate = adoptionRate.Value;
        if (ticketCount.HasValue) feature.SupportTicketCount = ticketCount.Value;

        // Check kill criteria
        EvaluateKillCriteria(feature);
    }

    /// <summary>
    /// Marks a feature for removal.
    /// </summary>
    public void MarkForRemoval(string featureId, string reason)
    {
        var feature = _features.FirstOrDefault(f => f.Id == featureId);
        if (feature == null) return;

        feature.IsMarkedForRemoval = true;
        feature.RemovalReason = reason;
        feature.IsEnabled = false;

        Logger.Warning("FeatureTieringService", $"Feature marked for removal: {feature.Name} - {reason}");
    }

    /// <summary>
    /// Evaluates all features against kill criteria.
    /// </summary>
    public List<TieredFeature> EvaluateAllKillCriteria()
    {
        var markedForRemoval = new List<TieredFeature>();

        foreach (var feature in _features.Where(f => !f.IsMarkedForRemoval && f.KillCriteria != null))
        {
            if (EvaluateKillCriteria(feature))
            {
                markedForRemoval.Add(feature);
            }
        }

        return markedForRemoval;
    }

    /// <summary>
    /// Gets tier summary.
    /// </summary>
    public TierSummary GetTierSummary()
    {
        return new TierSummary
        {
            CoreCount = _features.Count(f => f.Tier == FeatureTier.Core),
            ProCount = _features.Count(f => f.Tier == FeatureTier.Pro),
            ExperimentalCount = _features.Count(f => f.Tier == FeatureTier.Experimental),
            EnabledCount = _features.Count(f => f.IsEnabled),
            MarkedForRemovalCount = _features.Count(f => f.IsMarkedForRemoval),
            FeaturesAtRisk = EvaluateAllKillCriteria().Select(f => f.Name).ToList()
        };
    }

    /// <summary>
    /// Exports tier configuration.
    /// </summary>
    public string ExportConfiguration()
    {
        var export = new
        {
            ExportedAt = DateTime.Now,
            Features = _features.Select(f => new
            {
                f.Id,
                f.Name,
                Tier = f.Tier.ToString(),
                f.IsEnabled,
                f.IsMarkedForRemoval,
                f.RemovalReason,
                KillCriteria = f.KillCriteria != null ? new
                {
                    f.KillCriteria.MaxBugCount,
                    f.KillCriteria.MinAdoptionRate,
                    f.KillCriteria.MaxSupportTickets,
                    f.KillCriteria.SunsetDate
                } : null
            })
        };

        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }

    private bool EvaluateKillCriteria(TieredFeature feature)
    {
        if (feature.KillCriteria == null) return false;

        var reasons = new List<string>();

        if (feature.CurrentBugCount > feature.KillCriteria.MaxBugCount)
        {
            reasons.Add($"Bug count ({feature.CurrentBugCount}) exceeds threshold ({feature.KillCriteria.MaxBugCount})");
        }

        if (feature.AdoptionRate < feature.KillCriteria.MinAdoptionRate)
        {
            reasons.Add($"Adoption rate ({feature.AdoptionRate:F1}%) below minimum ({feature.KillCriteria.MinAdoptionRate:F1}%)");
        }

        if (feature.SupportTicketCount > feature.KillCriteria.MaxSupportTickets)
        {
            reasons.Add($"Support tickets ({feature.SupportTicketCount}) exceed threshold ({feature.KillCriteria.MaxSupportTickets})");
        }

        if (feature.KillCriteria.SunsetDate.HasValue && DateTime.Now > feature.KillCriteria.SunsetDate.Value)
        {
            reasons.Add("Sunset date reached");
        }

        if (reasons.Any())
        {
            MarkForRemoval(feature.Id, string.Join("; ", reasons));
            return true;
        }

        return false;
    }

    private void InitializeDefaultFeatures()
    {
        // Core features
        RegisterFeature(new TieredFeature
        {
            Id = "keepawake",
            Name = "Keep-Awake",
            Tier = FeatureTier.Core,
            Description = "Prevent system sleep",
            LaunchDate = DateTime.Now.AddYears(-1)
        });

        RegisterFeature(new TieredFeature
        {
            Id = "typething",
            Name = "TypeThing",
            Tier = FeatureTier.Core,
            Description = "Automated typing",
            LaunchDate = DateTime.Now.AddYears(-1)
        });

        // Pro features
        RegisterFeature(new TieredFeature
        {
            Id = "schedules",
            Name = "Smart Schedules",
            Tier = FeatureTier.Pro,
            Description = "Automated activation schedules",
            LaunchDate = DateTime.Now.AddMonths(-3),
            KillCriteria = new KillCriteria { MinAdoptionRate = 15, MaxBugCount = 3, MaxSupportTickets = 5 }
        });

        // Experimental features
        RegisterFeature(new TieredFeature
        {
            Id = "ai_suggestions",
            Name = "AI Suggestions",
            Tier = FeatureTier.Experimental,
            Description = "AI-powered usage suggestions",
            LaunchDate = DateTime.Now.AddMonths(-1),
            KillCriteria = new KillCriteria { MinAdoptionRate = 10, MaxBugCount = 10, MaxSupportTickets = 20, SunsetDate = DateTime.Now.AddMonths(6) }
        });

        RegisterFeature(new TieredFeature
        {
            Id = "advanced_analytics",
            Name = "Advanced Analytics",
            Tier = FeatureTier.Experimental,
            Description = "Deep usage analytics",
            LaunchDate = DateTime.Now.AddMonths(-2),
            KillCriteria = new KillCriteria { MinAdoptionRate = 5, MaxBugCount = 5, MaxSupportTickets = 15, SunsetDate = DateTime.Now.AddMonths(3) }
        });
    }
}

/// <summary>
/// Tier summary statistics.
/// </summary>
public class TierSummary
{
    public int CoreCount { get; set; }
    public int ProCount { get; set; }
    public int ExperimentalCount { get; set; }
    public int EnabledCount { get; set; }
    public int MarkedForRemovalCount { get; set; }
    public List<string> FeaturesAtRisk { get; set; } = new();
}
