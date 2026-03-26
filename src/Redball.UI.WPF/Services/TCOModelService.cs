using System;
using System.Collections.Generic;
using System.Linq;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Cost component for TCO calculation.
/// </summary>
public class CostComponent
{
    public string Name { get; set; } = "";
    public double MonthlyCost { get; set; }
    public string Category { get; set; } = ""; // "infrastructure", "license", "support"
    public bool IsVariable { get; set; }
    public double? PerUserCost { get; set; }
}

/// <summary>
/// TCO threshold configuration.
/// </summary>
public class TCOThreshold
{
    public string Tier { get; set; } = ""; // "Free", "Pro", "Enterprise"
    public double MaxMonthlyCostPerUser { get; set; }
    public double MaxTotalMonthlyCost { get; set; }
    public double TargetMargin { get; set; }
}

/// <summary>
/// TCO calculation result.
/// </summary>
public class TCOResult
{
    public int UserCount { get; set; }
    public double TotalMonthlyCost { get; set; }
    public double CostPerUser { get; set; }
    public List<CostBreakdown> Breakdown { get; set; } = new();
    public bool WithinThresholds { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Cost breakdown item.
/// </summary>
public class CostBreakdown
{
    public string Category { get; set; } = "";
    public double MonthlyCost { get; set; }
    public double Percentage { get; set; }
}

/// <summary>
/// Serverless Total Cost of Ownership service.
/// Implements strat-4 from improve_me.txt: Publish serverless TCO model with cost thresholds per user.
/// </summary>
public class TCOModelService
{
    private static readonly Lazy<TCOModelService> _instance = new(() => new TCOModelService());
    public static TCOModelService Instance => _instance.Value;

    private readonly List<CostComponent> _costComponents = new();
    private readonly List<TCOThreshold> _thresholds = new();

    private TCOModelService()
    {
        InitializeDefaultCosts();
        InitializeDefaultThresholds();
        Logger.Info("TCOModelService", "TCO model service initialized");
    }

    /// <summary>
    /// Calculates TCO for a given user count.
    /// </summary>
    public TCOResult CalculateTCO(int userCount, string tier = "Pro")
    {
        var threshold = _thresholds.FirstOrDefault(t => t.Tier == tier);
        var result = new TCOResult { UserCount = userCount };

        var breakdown = new Dictionary<string, double>();

        foreach (var component in _costComponents)
        {
            double cost;
            if (component.IsVariable && component.PerUserCost.HasValue)
            {
                cost = component.MonthlyCost + (component.PerUserCost.Value * userCount);
            }
            else
            {
                cost = component.MonthlyCost;
            }

            if (!breakdown.ContainsKey(component.Category))
                breakdown[component.Category] = 0;

            breakdown[component.Category] += cost;
            result.TotalMonthlyCost += cost;
        }

        result.CostPerUser = userCount > 0 ? result.TotalMonthlyCost / userCount : 0;

        // Calculate percentages
        foreach (var kvp in breakdown)
        {
            result.Breakdown.Add(new CostBreakdown
            {
                Category = kvp.Key,
                MonthlyCost = kvp.Value,
                Percentage = result.TotalMonthlyCost > 0 ? kvp.Value / result.TotalMonthlyCost * 100 : 0
            });
        }

        // Check thresholds
        if (threshold != null)
        {
            result.WithinThresholds = result.CostPerUser <= threshold.MaxMonthlyCostPerUser &&
                                     result.TotalMonthlyCost <= threshold.MaxTotalMonthlyCost;

            if (result.CostPerUser > threshold.MaxMonthlyCostPerUser)
            {
                result.Warnings.Add($"Cost per user (${result.CostPerUser:F2}) exceeds threshold (${threshold.MaxMonthlyCostPerUser:F2})");
            }

            if (result.TotalMonthlyCost > threshold.MaxTotalMonthlyCost)
            {
                result.Warnings.Add($"Total cost (${result.TotalMonthlyCost:F2}) exceeds threshold (${threshold.MaxTotalMonthlyCost:F2})");
            }
        }

        Logger.Info("TCOModelService", $"TCO calculated: {userCount} users = ${result.TotalMonthlyCost:F2}/month");
        return result;
    }

    /// <summary>
    /// Adds a cost component.
    /// </summary>
    public void AddCostComponent(CostComponent component)
    {
        _costComponents.Add(component);
        Logger.Info("TCOModelService", $"Cost component added: {component.Name} (${component.MonthlyCost:F2})");
    }

    /// <summary>
    /// Sets threshold for a tier.
    /// </summary>
    public void SetThreshold(TCOThreshold threshold)
    {
        var existing = _thresholds.FirstOrDefault(t => t.Tier == threshold.Tier);
        if (existing != null)
        {
            _thresholds.Remove(existing);
        }
        _thresholds.Add(threshold);
    }

    /// <summary>
    /// Gets threshold for a tier.
    /// </summary>
    public TCOThreshold? GetThreshold(string tier)
    {
        return _thresholds.FirstOrDefault(t => t.Tier == tier);
    }

    /// <summary>
    /// Generates cost projection over time.
    /// </summary>
    public List<(int Month, int Users, double Cost, double CostPerUser)> ProjectGrowth(
        int startingUsers,
        double monthlyGrowthRate,
        int months)
    {
        var projections = new List<(int, int, double, double)>();
        var currentUsers = startingUsers;

        for (int month = 1; month <= months; month++)
        {
            var tco = CalculateTCO(currentUsers);
            projections.Add((month, currentUsers, tco.TotalMonthlyCost, tco.CostPerUser));
            currentUsers = (int)(currentUsers * (1 + monthlyGrowthRate));
        }

        return projections;
    }

    /// <summary>
    /// Gets breakeven analysis.
    /// </summary>
    public BreakevenAnalysis GetBreakevenAnalysis(double pricePerUser)
    {
        // Find user count where revenue = cost
        for (int users = 1; users <= 100000; users *= 2)
        {
            var tco = CalculateTCO(users);
            var revenue = pricePerUser * users;

            if (revenue >= tco.TotalMonthlyCost)
            {
                return new BreakevenAnalysis
                {
                    BreakevenUsers = users,
                    BreakevenMonthlyRevenue = revenue,
                    MonthlyCostAtBreakeven = tco.TotalMonthlyCost
                };
            }
        }

        return new BreakevenAnalysis { BreakevenUsers = -1 };
    }

    private void InitializeDefaultCosts()
    {
        // Azure Functions - serverless compute
        AddCostComponent(new CostComponent
        {
            Name = "Azure Functions",
            MonthlyCost = 0,
            Category = "infrastructure",
            IsVariable = true,
            PerUserCost = 0.10 // $0.10 per user/month
        });

        // Cosmos DB - serverless database
        AddCostComponent(new CostComponent
        {
            Name = "Cosmos DB Serverless",
            MonthlyCost = 0,
            Category = "infrastructure",
            IsVariable = true,
            PerUserCost = 0.25 // $0.25 per user/month
        });

        // Blob Storage
        AddCostComponent(new CostComponent
        {
            Name = "Azure Blob Storage",
            MonthlyCost = 5, // Base cost
            Category = "infrastructure",
            IsVariable = true,
            PerUserCost = 0.01
        });

        // Application Insights
        AddCostComponent(new CostComponent
        {
            Name = "Application Insights",
            MonthlyCost = 0,
            Category = "infrastructure",
            IsVariable = true,
            PerUserCost = 0.05
        });

        // Key Vault
        AddCostComponent(new CostComponent
        {
            Name = "Azure Key Vault",
            MonthlyCost = 0.03, // $0.03/10,000 operations
            Category = "infrastructure",
            IsVariable = false
        });

        // Static base costs
        AddCostComponent(new CostComponent
        {
            Name = "Azure DevOps",
            MonthlyCost = 6, // Basic plan
            Category = "infrastructure",
            IsVariable = false
        });
    }

    private void InitializeDefaultThresholds()
    {
        SetThreshold(new TCOThreshold
        {
            Tier = "Free",
            MaxMonthlyCostPerUser = 0.50,
            MaxTotalMonthlyCost = 5000,
            TargetMargin = 0
        });

        SetThreshold(new TCOThreshold
        {
            Tier = "Pro",
            MaxMonthlyCostPerUser = 1.00,
            MaxTotalMonthlyCost = 10000,
            TargetMargin = 0.70
        });

        SetThreshold(new TCOThreshold
        {
            Tier = "Enterprise",
            MaxMonthlyCostPerUser = 2.00,
            MaxTotalMonthlyCost = 50000,
            TargetMargin = 0.80
        });
    }
}

/// <summary>
/// Breakeven analysis result.
/// </summary>
public class BreakevenAnalysis
{
    public int BreakevenUsers { get; set; }
    public double BreakevenMonthlyRevenue { get; set; }
    public double MonthlyCostAtBreakeven { get; set; }

    public bool HasBreakeven => BreakevenUsers > 0;
}
