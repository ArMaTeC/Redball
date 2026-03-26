using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Value map entry linking features to KPIs.
/// </summary>
public class ValueMapEntry
{
    public string FeatureId { get; set; } = "";
    public string FeatureName { get; set; } = "";
    public string Quarter { get; set; } = ""; // "Q1-2026", "Q2-2026", etc.
    public List<string> LinkedKPIs { get; set; } = new();
    public double ExpectedImpact { get; set; } // 0-100
    public string SuccessMetric { get; set; } = "";
    public string? Owner { get; set; }
    public DateTime TargetDate { get; set; }
    public bool IsDelivered { get; set; }
    public double? ActualImpact { get; set; }
}

/// <summary>
/// KPI tracking data.
/// </summary>
public class KPIData
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // "retention", "activation", "conversion"
    public double TargetValue { get; set; }
    public double CurrentValue { get; set; }
    public string Unit { get; set; } = "";
    public List<(DateTime Date, double Value)> History { get; set; } = new();
}

/// <summary>
/// Quarterly value map service.
/// Implements strat-2 from improve_me.txt: Maintain quarterly value-map linking features to retention/activation/conversion KPIs.
/// </summary>
public class ValueMapService
{
    private static readonly Lazy<ValueMapService> _instance = new(() => new ValueMapService());
    public static ValueMapService Instance => _instance.Value;

    private readonly List<ValueMapEntry> _entries = new();
    private readonly Dictionary<string, KPIData> _kpis = new();

    private ValueMapService()
    {
        InitializeDefaultKPIs();
        InitializeCurrentQuarter();
        Logger.Info("ValueMapService", "Value map service initialized");
    }

    /// <summary>
    /// Adds a value map entry.
    /// </summary>
    public void AddEntry(ValueMapEntry entry)
    {
        _entries.Add(entry);
        Logger.Info("ValueMapService", $"Value map entry added: {entry.FeatureName} ({entry.Quarter})");
    }

    /// <summary>
    /// Gets entries for a quarter.
    /// </summary>
    public IReadOnlyList<ValueMapEntry> GetQuarterlyMap(string quarter)
    {
        return _entries.Where(e => e.Quarter == quarter).ToList();
    }

    /// <summary>
    /// Updates KPI value.
    /// </summary>
    public void UpdateKPI(string name, double value, DateTime? timestamp = null)
    {
        if (_kpis.TryGetValue(name, out var kpi))
        {
            kpi.CurrentValue = value;
            kpi.History.Add((timestamp ?? DateTime.Now, value));

            // Keep last 90 days
            var cutoff = DateTime.Now.AddDays(-90);
            kpi.History.RemoveAll(h => h.Date < cutoff);

            Logger.Info("ValueMapService", $"KPI updated: {name} = {value}{kpi.Unit}");
        }
    }

    /// <summary>
    /// Gets KPI summary.
    /// </summary>
    public KPIData? GetKPI(string name)
    {
        return _kpis.TryGetValue(name, out var kpi) ? kpi : null;
    }

    /// <summary>
    /// Gets all KPIs.
    /// </summary>
    public IReadOnlyDictionary<string, KPIData> GetAllKPIs()
    {
        return _kpis;
    }

    /// <summary>
    /// Generates quarterly report.
    /// </summary>
    public QuarterlyReport GenerateReport(string quarter)
    {
        var entries = GetQuarterlyMap(quarter);
        var delivered = entries.Count(e => e.IsDelivered);
        var totalImpact = entries.Where(e => e.ActualImpact.HasValue).Sum(e => e.ActualImpact!.Value);

        return new QuarterlyReport
        {
            Quarter = quarter,
            TotalFeatures = entries.Count,
            DeliveredFeatures = delivered,
            DeliveryRate = entries.Any() ? (double)delivered / entries.Count * 100 : 0,
            TotalExpectedImpact = entries.Sum(e => e.ExpectedImpact),
            ActualImpact = totalImpact,
            RetentionKPIs = _kpis.Where(k => k.Value.Type == "retention").Select(k => k.Value).ToList(),
            ActivationKPIs = _kpis.Where(k => k.Value.Type == "activation").Select(k => k.Value).ToList(),
            ConversionKPIs = _kpis.Where(k => k.Value.Type == "conversion").Select(k => k.Value).ToList(),
            FeatureDetails = entries.ToList()
        };
    }

    /// <summary>
    /// Exports value map to JSON.
    /// </summary>
    public string ExportToJson()
    {
        var export = new
        {
            ExportedAt = DateTime.Now,
            KPIs = _kpis,
            Entries = _entries
        };
        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }

    private void InitializeDefaultKPIs()
    {
        // Retention KPIs
        _kpis["day7_retention"] = new KPIData
        {
            Name = "Day 7 Retention",
            Type = "retention",
            TargetValue = 70,
            CurrentValue = 65,
            Unit = "%"
        };

        _kpis["day30_retention"] = new KPIData
        {
            Name = "Day 30 Retention",
            Type = "retention",
            TargetValue = 45,
            CurrentValue = 38,
            Unit = "%"
        };

        // Activation KPIs
        _kpis["activation_rate"] = new KPIData
        {
            Name = "First-Session Activation",
            Type = "activation",
            TargetValue = 80,
            CurrentValue = 72,
            Unit = "%"
        };

        _kpis["feature_discovery"] = new KPIData
        {
            Name = "Feature Discovery (3+ features)",
            Type = "activation",
            TargetValue = 60,
            CurrentValue = 45,
            Unit = "%"
        };

        // Conversion KPIs
        _kpis["trial_conversion"] = new KPIData
        {
            Name = "Trial to Paid Conversion",
            Type = "conversion",
            TargetValue = 15,
            CurrentValue = 12,
            Unit = "%"
        };

        _kpis["upgrade_rate"] = new KPIData
        {
            Name = "Free to Pro Upgrade",
            Type = "conversion",
            TargetValue = 8,
            CurrentValue = 5,
            Unit = "%"
        };
    }

    private void InitializeCurrentQuarter()
    {
        // Add sample entries for current quarter
        var currentQuarter = $"Q{(DateTime.Now.Month - 1) / 3 + 1}-{DateTime.Now.Year}";

        AddEntry(new ValueMapEntry
        {
            FeatureId = "ux-4",
            FeatureName = "Latency Masking Pattern",
            Quarter = currentQuarter,
            LinkedKPIs = new() { "activation_rate", "day7_retention" },
            ExpectedImpact = 5,
            SuccessMetric = "Session completion rate",
            Owner = "UX Team",
            TargetDate = DateTime.Now.AddMonths(1),
            IsDelivered = true,
            ActualImpact = 4.5
        });

        AddEntry(new ValueMapEntry
        {
            FeatureId = "ui-4",
            FeatureName = "Accessibility Baseline",
            Quarter = currentQuarter,
            LinkedKPIs = new() { "activation_rate" },
            ExpectedImpact = 3,
            SuccessMetric = "Screen reader compatibility",
            Owner = "UI Team",
            TargetDate = DateTime.Now.AddMonths(1),
            IsDelivered = true,
            ActualImpact = 3
        });

        AddEntry(new ValueMapEntry
        {
            FeatureId = "dist-1",
            FeatureName = "Staged Rollout Channels",
            Quarter = currentQuarter,
            LinkedKPIs = new() { "day7_retention", "day30_retention" },
            ExpectedImpact = 8,
            SuccessMetric = "Update success rate",
            Owner = "Platform Team",
            TargetDate = DateTime.Now.AddMonths(2),
            IsDelivered = true,
            ActualImpact = 7
        });
    }
}

/// <summary>
/// Quarterly report summary.
/// </summary>
public class QuarterlyReport
{
    public string Quarter { get; set; } = "";
    public int TotalFeatures { get; set; }
    public int DeliveredFeatures { get; set; }
    public double DeliveryRate { get; set; }
    public double TotalExpectedImpact { get; set; }
    public double ActualImpact { get; set; }
    public List<KPIData> RetentionKPIs { get; set; } = new();
    public List<KPIData> ActivationKPIs { get; set; } = new();
    public List<KPIData> ConversionKPIs { get; set; } = new();
    public List<ValueMapEntry> FeatureDetails { get; set; } = new();

    public double ImpactAchievement => TotalExpectedImpact > 0
        ? ActualImpact / TotalExpectedImpact * 100
        : 0;
}
