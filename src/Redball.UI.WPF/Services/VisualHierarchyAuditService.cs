using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Visual hierarchy violation.
/// </summary>
public class HierarchyViolation
{
    public string ElementPath { get; set; } = "";
    public string Violation { get; set; } = "";
    public string Severity { get; set; } = ""; // Error, Warning, Info
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
}

/// <summary>
/// Hierarchy audit result.
/// </summary>
public class HierarchyAuditResult
{
    public string PanelName { get; set; } = "";
    public List<HierarchyViolation> Violations { get; set; } = new();
    public int ErrorCount => Violations.Count(v => v.Severity == "Error");
    public int WarningCount => Violations.Count(v => v.Severity == "Warning");
    public bool IsValid => ErrorCount == 0;
}

/// <summary>
/// Visual hierarchy guidelines.
/// </summary>
public static class VisualHierarchyGuidelines
{
    // Z-Index guidelines
    public const int BackgroundZIndex = 0;
    public const int ContentZIndex = 10;
    public const int ControlsZIndex = 20;
    public const int OverlayZIndex = 100;
    public const int ModalZIndex = 1000;

    // Font size hierarchy
    public static readonly Dictionary<string, int> FontSizeHierarchy = new()
    {
        ["Title"] = 6,
        ["Subtitle"] = 5,
        ["SectionHeader"] = 4,
        ["ControlLabel"] = 3,
        ["BodyText"] = 2,
        ["Caption"] = 1
    };

    // Spacing hierarchy
    public const double SectionSpacing = 24;
    public const double GroupSpacing = 16;
    public const double ControlSpacing = 8;
    public const double RelatedSpacing = 4;
}

/// <summary>
/// Service for auditing visual hierarchy.
/// Implements ui-2 from improve_me.txt: Enforce visual hierarchy audit for every panel.
/// </summary>
public class VisualHierarchyAuditService
{
    private static readonly Lazy<VisualHierarchyAuditService> _instance = new(() => new VisualHierarchyAuditService());
    public static VisualHierarchyAuditService Instance => _instance.Value;

    private readonly List<HierarchyAuditResult> _auditHistory = new();

    private VisualHierarchyAuditService()
    {
        Logger.Info("VisualHierarchyAuditService", "Visual hierarchy audit service initialized");
    }

    /// <summary>
    /// Audits a panel's visual hierarchy.
    /// </summary>
    public HierarchyAuditResult AuditPanel(Panel panel, string panelName)
    {
        var result = new HierarchyAuditResult { PanelName = panelName };

        // Check Z-Index ordering
        CheckZIndexOrdering(panel, result, "");

        // Check font size hierarchy
        CheckFontSizeHierarchy(panel, result, "");

        // Check spacing consistency
        CheckSpacingConsistency(panel, result, "");

        // Check contrast hierarchy
        CheckContrastHierarchy(panel, result, "");

        _auditHistory.Add(result);

        Logger.Info("VisualHierarchyAuditService",
            $"Panel audited: {panelName} - {result.ErrorCount} errors, {result.WarningCount} warnings");

        return result;
    }

    /// <summary>
    /// Audits any FrameworkElement.
    /// </summary>
    public HierarchyAuditResult AuditElement(FrameworkElement element, string elementName)
    {
        var result = new HierarchyAuditResult { PanelName = elementName };

        if (element is Panel panel)
        {
            return AuditPanel(panel, elementName);
        }

        // Check individual element properties
        CheckElementProperties(element, result, "");

        _auditHistory.Add(result);
        return result;
    }

    /// <summary>
    /// Gets audit history.
    /// </summary>
    public IReadOnlyList<HierarchyAuditResult> GetAuditHistory()
    {
        return _auditHistory.ToList();
    }

    /// <summary>
    /// Gets summary of all audits.
    /// </summary>
    public HierarchySummary GetSummary()
    {
        var totalPanels = _auditHistory.Count;
        var validPanels = _auditHistory.Count(r => r.IsValid);
        var totalErrors = _auditHistory.Sum(r => r.ErrorCount);
        var totalWarnings = _auditHistory.Sum(r => r.WarningCount);

        return new HierarchySummary
        {
            TotalPanelsAudited = totalPanels,
            ValidPanels = validPanels,
            InvalidPanels = totalPanels - validPanels,
            TotalErrors = totalErrors,
            TotalWarnings = totalWarnings,
            ComplianceRate = totalPanels > 0 ? (double)validPanels / totalPanels * 100 : 0,
            PanelsNeedingFix = _auditHistory
                .Where(r => !r.IsValid)
                .Select(r => r.PanelName)
                .ToList()
        };
    }

    private void CheckZIndexOrdering(Panel panel, HierarchyAuditResult result, string path)
    {
        var children = panel.Children.OfType<UIElement>().ToList();
        int? lastZIndex = null;

        foreach (var child in children)
        {
            var zIndex = Panel.GetZIndex(child);

            if (lastZIndex.HasValue && zIndex < lastZIndex.Value)
            {
                result.Violations.Add(new HierarchyViolation
                {
                    ElementPath = $"{path}/{child.GetType().Name}",
                    Violation = "Z-Index violation: child appears before parent in visual order",
                    Severity = "Warning",
                    Expected = $"Z-Index >= {lastZIndex.Value}",
                    Actual = $"Z-Index = {zIndex}"
                });
            }

            lastZIndex = zIndex;

            // Recurse into child panels
            if (child is Panel childPanel)
            {
                CheckZIndexOrdering(childPanel, result, $"{path}/{child.GetType().Name}");
            }
        }
    }

    private void CheckFontSizeHierarchy(Panel panel, HierarchyAuditResult result, string path)
    {
        // Check that there's a clear hierarchy of font sizes
        var textElements = panel.Children.OfType<TextBlock>().ToList();
        var fontSizes = textElements.Select(t => t.FontSize).Distinct().OrderByDescending(s => s).ToList();

        if (fontSizes.Count >= 2)
        {
            // Check that font sizes are sufficiently different (at least 2pt)
            for (int i = 0; i < fontSizes.Count - 1; i++)
            {
                var diff = fontSizes[i] - fontSizes[i + 1];
                if (diff < 2)
                {
                    result.Violations.Add(new HierarchyViolation
                    {
                        ElementPath = path,
                        Violation = "Font size hierarchy unclear: sizes too similar",
                        Severity = "Warning",
                        Expected = "At least 2pt difference between hierarchy levels",
                        Actual = $"{diff:F1}pt difference"
                    });
                }
            }
        }

        // Recurse
        foreach (var child in panel.Children.OfType<Panel>())
        {
            CheckFontSizeHierarchy(child, result, $"{path}/{child.GetType().Name}");
        }
    }

    private void CheckSpacingConsistency(Panel panel, HierarchyAuditResult result, string path)
    {
        if (panel is StackPanel stackPanel)
        {
            // Check margin consistency
            var margins = panel.Children.OfType<FrameworkElement>().Select(c => c.Margin).ToList();
            var uniqueMargins = margins.Distinct().Count();

            if (uniqueMargins > 3)
            {
                result.Violations.Add(new HierarchyViolation
                {
                    ElementPath = path,
                    Violation = "Too many different margin values: spacing not consistent",
                    Severity = "Info",
                    Expected = "2-3 consistent margin patterns",
                    Actual = $"{uniqueMargins} different margins"
                });
            }
        }

        // Recurse
        foreach (var child in panel.Children.OfType<Panel>())
        {
            CheckSpacingConsistency(child, result, $"{path}/{child.GetType().Name}");
        }
    }

    private void CheckContrastHierarchy(Panel panel, HierarchyAuditResult result, string path)
    {
        // Check that background and foreground have sufficient contrast
        if (panel.Background is SolidColorBrush bgBrush)
        {
            foreach (var child in panel.Children.OfType<Control>())
            {
                if (child.Foreground is SolidColorBrush fgBrush)
                {
                    var contrast = CalculateContrastRatio(bgBrush.Color, fgBrush.Color);
                    if (contrast < 4.5)
                    {
                        result.Violations.Add(new HierarchyViolation
                        {
                            ElementPath = $"{path}/{child.GetType().Name}",
                            Violation = "Contrast too low for readability",
                            Severity = "Error",
                            Expected = "Contrast ratio >= 4.5:1 (WCAG AA)",
                            Actual = $"Contrast ratio = {contrast:F1}:1"
                        });
                    }
                }
            }
        }

        // Recurse
        foreach (var child in panel.Children.OfType<Panel>())
        {
            CheckContrastHierarchy(child, result, $"{path}/{child.GetType().Name}");
        }
    }

    private void CheckElementProperties(FrameworkElement element, HierarchyAuditResult result, string path)
    {
        // Check minimum touch target size
        if (element is Control control)
        {
            if (control.ActualWidth < 44 || control.ActualHeight < 44)
            {
                result.Violations.Add(new HierarchyViolation
                {
                    ElementPath = path,
                    Violation = "Control smaller than recommended touch target size",
                    Severity = "Warning",
                    Expected = "Minimum 44x44 pixels",
                    Actual = $"{control.ActualWidth:F0}x{control.ActualHeight:F0} pixels"
                });
            }
        }
    }

    private double CalculateContrastRatio(Color foreground, Color background)
    {
        var lum1 = CalculateRelativeLuminance(foreground);
        var lum2 = CalculateRelativeLuminance(background);

        var lighter = Math.Max(lum1, lum2);
        var darker = Math.Min(lum1, lum2);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private double CalculateRelativeLuminance(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }
}

/// <summary>
/// Hierarchy audit summary.
/// </summary>
public class HierarchySummary
{
    public int TotalPanelsAudited { get; set; }
    public int ValidPanels { get; set; }
    public int InvalidPanels { get; set; }
    public int TotalErrors { get; set; }
    public int TotalWarnings { get; set; }
    public double ComplianceRate { get; set; }
    public List<string> PanelsNeedingFix { get; set; } = new();
}
