using System;
using System.Collections.Generic;
using System.Linq;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// User persona definition for product strategy.
/// Implements strat-1 from improve_me.txt: Define 2 primary personas with outcome metrics.
/// </summary>
public class UserPersona
{
    /// <summary>
    /// Unique identifier for the persona.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Persona name (e.g., "Enterprise IT Admin").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Brief description of this user type.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Key characteristics and behaviors.
    /// </summary>
    public List<string> Characteristics { get; set; } = new();

    /// <summary>
    /// Primary goals this persona wants to achieve.
    /// </summary>
    public List<string> Goals { get; set; } = new();

    /// <summary>
    /// Pain points and frustrations.
    /// </summary>
    public List<string> PainPoints { get; set; } = new();

    /// <summary>
    /// Key jobs-to-be-done.
    /// </summary>
    public List<string> JobsToBeDone { get; set; } = new();

    /// <summary>
    /// Success metrics that matter to this persona.
    /// </summary>
    public Dictionary<string, string> SuccessMetrics { get; set; } = new();

    /// <summary>
    /// Estimated percentage of user base.
    /// </summary>
    public int UserPercentage { get; set; }

    /// <summary>
    /// Feature tier preference (Basic/Advanced/Experimental).
    /// </summary>
    public string PreferredTier { get; set; } = "Basic";

    /// <summary>
    /// Average session duration expectation.
    /// </summary>
    public TimeSpan TypicalSessionDuration { get; set; }
}

/// <summary>
/// North Star Metric definition and tracking.
/// Implements strat-5 from improve_me.txt: Formalize product north-star metric.
/// </summary>
public class NorthStarMetric
{
    /// <summary>
    /// Metric name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Human-readable definition.
    /// </summary>
    public string Definition { get; set; } = "";

    /// <summary>
    /// Why this metric matters.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Current value.
    /// </summary>
    public double CurrentValue { get; set; }

    /// <summary>
    /// Target value.
    /// </summary>
    public double TargetValue { get; set; }

    /// <summary>
    /// Unit of measurement (minutes, %, count, etc.).
    /// </summary>
    public string Unit { get; set; } = "";

    /// <summary>
    /// Historical data points.
    /// </summary>
    public List<(DateTime Date, double Value)> History { get; set; } = new();

    /// <summary>
    /// Input metrics that drive this north star.
    /// </summary>
    public List<string> InputMetrics { get; set; } = new();
}

/// <summary>
/// Service for managing product strategy, personas, and metrics.
/// </summary>
public class ProductStrategyService
{
    private static readonly Lazy<ProductStrategyService> _instance = new(() => new ProductStrategyService());
    public static ProductStrategyService Instance => _instance.Value;

    private readonly List<UserPersona> _personas = new();
    private readonly Dictionary<string, NorthStarMetric> _metrics = new();

    private ProductStrategyService()
    {
        InitializeDefaultPersonas();
        InitializeNorthStarMetric();
        Logger.Info("ProductStrategyService", "Product strategy service initialized");
    }

    /// <summary>
    /// Gets all defined personas.
    /// </summary>
    public IReadOnlyList<UserPersona> GetPersonas()
    {
        return _personas.ToList();
    }

    /// <summary>
    /// Gets a specific persona by ID.
    /// </summary>
    public UserPersona? GetPersona(string id)
    {
        return _personas.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Gets the primary personas.
    /// </summary>
    public IReadOnlyList<UserPersona> GetPrimaryPersonas()
    {
        return _personas.Where(p => p.UserPercentage > 20).ToList();
    }

    /// <summary>
    /// Gets the north star metric.
    /// </summary>
    public NorthStarMetric? GetNorthStarMetric()
    {
        return _metrics.TryGetValue("productive_time", out var metric) ? metric : null;
    }

    /// <summary>
    /// Updates the north star metric value.
    /// </summary>
    public void UpdateNorthStarMetric(double value, DateTime? timestamp = null)
    {
        if (!_metrics.TryGetValue("productive_time", out var metric))
            return;

        metric.CurrentValue = value;
        metric.History.Add((timestamp ?? DateTime.Now, value));

        // Keep only last 90 days
        var cutoff = DateTime.Now.AddDays(-90);
        metric.History.RemoveAll(h => h.Date < cutoff);

        Logger.Info("ProductStrategyService", $"North Star updated: {metric.Name} = {value:F2}{metric.Unit}");
    }

    /// <summary>
    /// Gets strategy summary for analytics/reporting.
    /// </summary>
    public StrategySummary GetStrategySummary()
    {
        var northStar = GetNorthStarMetric();
        var retentionKPI = CalculateRetentionKPI();
        var activationKPI = CalculateActivationKPI();
        var conversionKPI = CalculateConversionKPI();

        return new StrategySummary
        {
            PrimaryPersonas = GetPrimaryPersonas().Select(p => p.Name).ToList(),
            NorthStarMetricName = northStar?.Name ?? "",
            NorthStarCurrentValue = northStar?.CurrentValue ?? 0,
            NorthStarTargetValue = northStar?.TargetValue ?? 0,
            NorthStarUnit = northStar?.Unit ?? "",
            NorthStarProgress = northStar != null && northStar.TargetValue > 0
                ? (northStar.CurrentValue / northStar.TargetValue * 100)
                : 0,
            RetentionRate = retentionKPI,
            ActivationRate = activationKPI,
            ConversionRate = conversionKPI,
            UserSegmentBreakdown = GetUserSegmentBreakdown()
        };
    }

    private void InitializeDefaultPersonas()
    {
        // Primary Persona 1: Enterprise IT Admin
        _personas.Add(new UserPersona
        {
            Id = "enterprise_admin",
            Name = "Enterprise IT Administrator",
            Description = "IT professional managing endpoint power policies across organization",
            Characteristics = new()
            {
                "Manages 100-5000 endpoints",
                "Security and compliance focused",
                "Uses Group Policy and MDM tools",
                "Reports to CISO/CIO",
                "Budget-conscious, ROI-driven",
                "Needs audit trails and telemetry"
            },
            Goals = new()
            {
                "Prevent unwanted system sleep during critical operations",
                "Enforce consistent power policies org-wide",
                "Minimize IT support tickets related to sleep/timeout",
                "Demonstrate cost savings from reduced interruptions",
                "Maintain security compliance (screen lock, etc.)"
            },
            PainPoints = new()
            {
                "Inconsistent sleep behavior across devices",
                "Users complaining about lost work due to unexpected sleep",
                "Difficulty diagnosing sleep/timeout issues remotely",
                "Lack of visibility into actual power usage patterns",
                "Managing exceptions for different user roles"
            },
            JobsToBeDone = new()
            {
                "Deploy power management policies at scale",
                "Monitor compliance and sleep prevention effectiveness",
                "Troubleshoot endpoint power issues quickly",
                "Generate reports on productivity impact",
                "Manage software updates with controlled rollout"
            },
            SuccessMetrics = new()
            {
                ["Policy Compliance Rate"] = ">95%",
                ["Mean Time To Resolve (MTTR)"] = "<4 hours",
                ["User Satisfaction Score"] = ">4.0/5",
                ["Support Ticket Reduction"] = "30% YoY"
            },
            UserPercentage = 35,
            PreferredTier = "Advanced",
            TypicalSessionDuration = TimeSpan.FromHours(8)
        });

        // Primary Persona 2: Power Remote Worker
        _personas.Add(new UserPersona
        {
            Id = "power_remote_worker",
            Name = "Power Remote Worker",
            Description = "Knowledge worker working from home with focus-intensive tasks",
            Characteristics = new()
            {
                "Works from home 3-5 days/week",
                "Deep work sessions 2-4 hours",
                "Multiple monitors, specialized setups",
                "Video calls, screen sharing frequently",
                "Tech-savvy, values productivity tools",
                "Willing to pay for quality tools"
            },
            Goals = new()
            {
                "Maintain focus without interruptions",
                "Prevent sleep during presentations and calls",
                "Automate power management intelligently",
                "Preserve battery when mobile",
                "Seamless experience across devices"
            },
            PainPoints = new()
            {
                "Screen sleeping during important video calls",
                "Lost work when stepping away briefly",
                "Complex manual sleep settings in Windows",
                "Inconsistent behaviour with docking stations",
                "No easy way to temporarily disable sleep"
            },
            JobsToBeDone = new()
            {
                "Quickly enable/disable sleep prevention",
                "Automate based on calendar/activity",
                "Type long text without manual entry",
                "Track productive time accurately",
                "Maintain focus during work sessions"
            },
            SuccessMetrics = new()
            {
                ["Focus Session Completion"] = ">80%",
                ["Sleep Interruption Rate"] = "<2%",
                ["Daily Active Usage"] = ">5 hours",
                ["Feature Discovery Rate"] = ">60%"
            },
            UserPercentage = 45,
            PreferredTier = "Basic",
            TypicalSessionDuration = TimeSpan.FromHours(6)
        });

        // Secondary Persona: Casual User
        _personas.Add(new UserPersona
        {
            Id = "casual_user",
            Name = "Casual User",
            Description = "Occasional user with basic sleep prevention needs",
            Characteristics = new()
            {
                "Uses app 1-2 times per week",
                "Simple needs, prefers defaults",
                "Price-sensitive or free user",
                "Discovers features organically"
            },
            Goals = new()
            {
                "Prevent sleep during downloads",
                "Simple one-click activation",
                "Minimal configuration required"
            },
            PainPoints = new()
            {
                "Too many options confuse them",
                "Forgets to turn off sleep prevention",
                "Doesn't understand advanced features"
            },
            JobsToBeDone = new()
            {
                "Quick activation from tray",
                "Set-and-forget operation"
            },
            SuccessMetrics = new()
            {
                ["Task Completion Rate"] = ">90%",
                ["Session Duration"] = "2-4 hours"
            },
            UserPercentage = 20,
            PreferredTier = "Basic",
            TypicalSessionDuration = TimeSpan.FromHours(2)
        });
    }

    private void InitializeNorthStarMetric()
    {
        _metrics["productive_time"] = new NorthStarMetric
        {
            Name = "Daily Productive Time Protected",
            Definition = "Average hours per day where system sleep was intelligently prevented during active work periods",
            Rationale = "Core value proposition: Redball exists to protect productive time from interruption. This metric captures the actual value delivered to users - uninterrupted work sessions where sleep would have caused friction or lost work.",
            CurrentValue = 0,
            TargetValue = 4.5, // 4.5 hours daily average
            Unit = "hours",
            InputMetrics = new()
            {
                "Active Session Duration",
                "Sleep Prevention Triggers",
                "Intelligent Activation Rate",
                "User Engagement Score",
                "Feature Adoption Rate"
            }
        };
    }

    private double CalculateRetentionKPI()
    {
        // Simplified - would integrate with actual analytics
        // Day 7 retention: users who used app again within 7 days
        var config = ConfigService.Instance.Config;
        return config.EnableTelemetry ? 0.72 : 0.65; // Placeholder
    }

    private double CalculateActivationKPI()
    {
        // Users who completed core activation (first sleep prevention)
        return 0.68; // Placeholder
    }

    private double CalculateConversionKPI()
    {
        // Free to paid conversion (if applicable)
        return 0.12; // Placeholder
    }

    private Dictionary<string, int> GetUserSegmentBreakdown()
    {
        return _personas.ToDictionary(
            p => p.Id,
            p => p.UserPercentage
        );
    }
}

/// <summary>
/// Strategy summary for reporting and dashboards.
/// </summary>
public class StrategySummary
{
    public List<string> PrimaryPersonas { get; set; } = new();
    public string NorthStarMetricName { get; set; } = "";
    public double NorthStarCurrentValue { get; set; }
    public double NorthStarTargetValue { get; set; }
    public string NorthStarUnit { get; set; } = "";
    public double NorthStarProgress { get; set; }
    public double RetentionRate { get; set; }
    public double ActivationRate { get; set; }
    public double ConversionRate { get; set; }
    public Dictionary<string, int> UserSegmentBreakdown { get; set; } = new();
}
