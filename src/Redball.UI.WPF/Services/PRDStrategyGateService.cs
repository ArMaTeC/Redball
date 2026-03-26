using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Strategy gate for PRD review.
/// </summary>
public class StrategyGate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsRequired { get; set; } = true;
    public bool IsPassed { get; set; }
    public string? Approver { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Notes { get; set; }
    public List<string> ChecklistItems { get; set; } = new();
}

/// <summary>
/// Product Requirements Document with strategy gates.
/// </summary>
public class ProductRequirementsDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Status { get; set; } = "Draft"; // Draft, InReview, Approved, Rejected
    public string? ProblemStatement { get; set; }
    public string? TargetPersona { get; set; }
    public string? SuccessMetric { get; set; }
    public List<StrategyGate> Gates { get; set; } = new();
    public List<string> LinkedFeatures { get; set; } = new();
    public string? Quarter { get; set; }
}

/// <summary>
/// Service for managing PRD strategy gates.
/// Implements strat-6 from improve_me.txt: Add strategy gates in PRD template.
/// </summary>
public class PRDStrategyGateService
{
    private static readonly Lazy<PRDStrategyGateService> _instance = new(() => new PRDStrategyGateService());
    public static PRDStrategyGateService Instance => _instance.Value;

    private readonly List<ProductRequirementsDocument> _prds = new();

    private PRDStrategyGateService()
    {
        Logger.Info("PRDStrategyGateService", "PRD strategy gate service initialized");
    }

    /// <summary>
    /// Creates a new PRD with default strategy gates.
    /// </summary>
    public ProductRequirementsDocument CreatePRD(string title, string author)
    {
        var prd = new ProductRequirementsDocument
        {
            Title = title,
            Author = author,
            Gates = CreateDefaultGates()
        };

        _prds.Add(prd);
        Logger.Info("PRDStrategyGateService", $"PRD created: {title}");

        return prd;
    }

    /// <summary>
    /// Gets a PRD by ID.
    /// </summary>
    public ProductRequirementsDocument? GetPRD(string id)
    {
        return _prds.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Gets all PRDs.
    /// </summary>
    public IReadOnlyList<ProductRequirementsDocument> GetAllPRDs()
    {
        return _prds.ToList();
    }

    /// <summary>
    /// Approves a strategy gate.
    /// </summary>
    public bool ApproveGate(string prdId, string gateId, string approver, string? notes = null)
    {
        var prd = GetPRD(prdId);
        if (prd == null) return false;

        var gate = prd.Gates.FirstOrDefault(g => g.Id == gateId);
        if (gate == null) return false;

        gate.IsPassed = true;
        gate.Approver = approver;
        gate.ApprovedAt = DateTime.Now;
        gate.Notes = notes;

        Logger.Info("PRDStrategyGateService", $"Gate approved: {gate.Name} by {approver}");

        // Check if all required gates are passed
        CheckPRDApproval(prd);

        return true;
    }

    /// <summary>
    /// Rejects a strategy gate.
    /// </summary>
    public bool RejectGate(string prdId, string gateId, string reason)
    {
        var prd = GetPRD(prdId);
        if (prd == null) return false;

        var gate = prd.Gates.FirstOrDefault(g => g.Id == gateId);
        if (gate == null) return false;

        gate.IsPassed = false;
        gate.Notes = reason;

        prd.Status = "Rejected";

        Logger.Warning("PRDStrategyGateService", $"Gate rejected: {gate.Name} - {reason}");
        return true;
    }

    /// <summary>
    /// Gets PRD approval status.
    /// </summary>
    public PRDApprovalStatus GetApprovalStatus(string prdId)
    {
        var prd = GetPRD(prdId);
        if (prd == null)
            return new PRDApprovalStatus { CanProceed = false, Error = "PRD not found" };

        var requiredGates = prd.Gates.Where(g => g.IsRequired).ToList();
        var passedRequired = requiredGates.Count(g => g.IsPassed);
        var optionalGates = prd.Gates.Where(g => !g.IsRequired).ToList();
        var passedOptional = optionalGates.Count(g => g.IsPassed);

        return new PRDApprovalStatus
        {
            TotalGates = prd.Gates.Count,
            RequiredGates = requiredGates.Count,
            PassedRequiredGates = passedRequired,
            OptionalGates = optionalGates.Count,
            PassedOptionalGates = passedOptional,
            CanProceed = passedRequired == requiredGates.Count && requiredGates.Any(),
            PendingGates = prd.Gates.Where(g => !g.IsPassed).Select(g => g.Name).ToList(),
            ApprovedGates = prd.Gates.Where(g => g.IsPassed).Select(g => g.Name).ToList()
        };
    }

    /// <summary>
    /// Exports PRD to JSON.
    /// </summary>
    public string ExportPRD(string prdId)
    {
        var prd = GetPRD(prdId);
        if (prd == null) return "{}";

        return JsonSerializer.Serialize(prd, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Gets PRD template with gates.
    /// </summary>
    public string GetPRDTemplate()
    {
        var template = new ProductRequirementsDocument
        {
            Title = "[Feature Name]",
            Author = "[Author Name]",
            ProblemStatement = "[Describe the problem this feature solves]",
            TargetPersona = "[Enterprise Admin / Power Remote Worker / Casual User]",
            SuccessMetric = "[North Star metric impact]",
            Quarter = "[Q1-2026]",
            Gates = CreateDefaultGates()
        };

        return JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
    }

    private List<StrategyGate> CreateDefaultGates()
    {
        return new List<StrategyGate>
        {
            new()
            {
                Id = "persona_alignment",
                Name = "Persona Alignment",
                Description = "Feature aligns with defined user personas and their goals",
                IsRequired = true,
                ChecklistItems = new()
                {
                    "Target persona identified",
                    "Persona goal addressed",
                    "Success metric defined"
                }
            },
            new()
            {
                Id = "kpi_linkage",
                Name = "KPI Linkage",
                Description = "Feature links to retention, activation, or conversion KPIs",
                IsRequired = true,
                ChecklistItems = new()
                {
                    "Primary KPI identified",
                    "Expected impact quantified",
                    "Measurement plan defined"
                }
            },
            new()
            {
                Id = "north_star",
                Name = "North Star Alignment",
                Description = "Feature contributes to Daily Productive Time Protected",
                IsRequired = true,
                ChecklistItems = new()
                {
                    "North star contribution explained",
                    "Input metric identified",
                    "Value hypothesis stated"
                }
            },
            new()
            {
                Id = "technical_feasibility",
                Name = "Technical Feasibility",
                Description = "Feature is technically feasible within constraints",
                IsRequired = true,
                ChecklistItems = new()
                {
                    "Architecture reviewed",
                    "Dependencies identified",
                    "Risk assessment complete"
                }
            },
            new()
            {
                Id = "ux_review",
                Name = "UX Design Review",
                Description = "UX design meets standards and accessibility requirements",
                IsRequired = false,
                ChecklistItems = new()
                {
                    "Design reviewed",
                    "Accessibility checked",
                    "Usability validated"
                }
            },
            new()
            {
                Id = "security_review",
                Name = "Security Review",
                Description = "Security implications reviewed and approved",
                IsRequired = true,
                ChecklistItems = new()
                {
                    "Threat model reviewed",
                    "Data handling checked",
                    "Compliance verified"
                }
            },
            new()
            {
                Id = "value_map",
                Name = "Value Map Integration",
                Description = "Feature added to quarterly value map",
                IsRequired = true,
                ChecklistItems = new()
                {
                    "Value map entry created",
                    "Linked KPIs identified",
                    "Owner assigned"
                }
            }
        };
    }

    private void CheckPRDApproval(ProductRequirementsDocument prd)
    {
        var requiredGates = prd.Gates.Where(g => g.IsRequired);
        var allPassed = requiredGates.All(g => g.IsPassed);

        if (allPassed && requiredGates.Any())
        {
            prd.Status = "Approved";
            Logger.Info("PRDStrategyGateService", $"PRD fully approved: {prd.Title}");
        }
        else if (prd.Status != "Rejected")
        {
            prd.Status = "InReview";
        }
    }
}

/// <summary>
/// PRD approval status.
/// </summary>
public class PRDApprovalStatus
{
    public int TotalGates { get; set; }
    public int RequiredGates { get; set; }
    public int PassedRequiredGates { get; set; }
    public int OptionalGates { get; set; }
    public int PassedOptionalGates { get; set; }
    public bool CanProceed { get; set; }
    public List<string> PendingGates { get; set; } = new();
    public List<string> ApprovedGates { get; set; } = new();
    public string? Error { get; set; }

    public double CompletionRate => TotalGates > 0
        ? (double)(PassedRequiredGates + PassedOptionalGates) / TotalGates * 100
        : 0;
}
