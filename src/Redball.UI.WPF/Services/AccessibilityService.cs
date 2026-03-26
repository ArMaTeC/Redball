using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Accessibility compliance level.
/// </summary>
public enum AccessibilityLevel
{
    A,
    AA,
    AAA
}

/// <summary>
/// Accessibility audit result.
/// </summary>
public class AccessibilityAuditResult
{
    public string ElementId { get; set; } = "";
    public string Issue { get; set; } = "";
    public string Severity { get; set; } = ""; // Error, Warning, Info
    public string WCAGCriterion { get; set; } = "";
    public string? Recommendation { get; set; }
}

/// <summary>
/// Accessibility configuration.
/// </summary>
public class AccessibilityConfig
{
    public bool HighContrastEnabled { get; set; }
    public bool ReducedMotion { get; set; }
    public double TextScale { get; set; } = 1.0;
    public bool ScreenReaderOptimized { get; set; }
    public bool KeyboardNavigationEnhanced { get; set; } = true;
    public AccessibilityLevel TargetCompliance { get; set; } = AccessibilityLevel.AA;
}

/// <summary>
/// Service for managing accessibility features and compliance.
/// Implements ui-4 from improve_me.txt: Accessibility baseline.
/// </summary>
public class AccessibilityService
{
    private static readonly Lazy<AccessibilityService> _instance = new(() => new AccessibilityService());
    public static AccessibilityService Instance => _instance.Value;

    private readonly AccessibilityConfig _config = new();
    private readonly List<FrameworkElement> _trackedElements = new();

    private AccessibilityService()
    {
        LoadSystemSettings();
        Logger.Info("AccessibilityService", "Accessibility service initialized");
    }

    /// <summary>
    /// Gets the current accessibility configuration.
    /// </summary>
    public AccessibilityConfig Config => _config;

    /// <summary>
    /// Registers an element for accessibility tracking.
    /// </summary>
    public void RegisterElement(FrameworkElement element, string automationId, string automationName, string? helpText = null)
    {
        if (element == null) return;

        // Set AutomationProperties
        AutomationProperties.SetAutomationId(element, automationId);
        AutomationProperties.SetName(element, automationName);

        if (!string.IsNullOrEmpty(helpText))
        {
            AutomationProperties.SetHelpText(element, helpText);
        }

        // Ensure focusable
        element.Focusable = true;

        // Add to tracked elements
        lock (_trackedElements)
        {
            _trackedElements.Add(element);
        }

        Logger.Debug("AccessibilityService", $"Element registered: {automationId} ({automationName})");
    }

    /// <summary>
    /// Applies focus ring style to an element.
    /// </summary>
    public void ApplyFocusRing(Control control)
    {
        if (control == null) return;

        control.FocusVisualStyle = null;

        // Set up focus visual style with high contrast color
        control.Resources["FocusVisualStyle"] = new Style(typeof(Control))
        {
            Setters =
            {
                new Setter(Control.BorderBrushProperty, new SolidColorBrush(Colors.Orange)),
                new Setter(Control.BorderThicknessProperty, new Thickness(2))
            }
        };
    }

    /// <summary>
    /// Audits contrast ratios for WCAG AA compliance.
    /// </summary>
    public List<AccessibilityAuditResult> AuditContrast()
    {
        var results = new List<AccessibilityAuditResult>();

        lock (_trackedElements)
        {
            foreach (var element in _trackedElements.OfType<Control>())
            {
                var bg = element.Background as SolidColorBrush;
                var fg = element.Foreground as SolidColorBrush;

                if (bg != null && fg != null)
                {
                    var ratio = CalculateContrastRatio(bg.Color, fg.Color);
                    var targetRatio = _config.TargetCompliance == AccessibilityLevel.AA ? 4.5 : 7.0;

                    if (ratio < targetRatio)
                    {
                        results.Add(new AccessibilityAuditResult
                        {
                            ElementId = AutomationProperties.GetAutomationId(element) ?? element.Name ?? "unknown",
                            Issue = $"Contrast ratio {ratio:F2} below {targetRatio:F1} requirement",
                            Severity = "Error",
                            WCAGCriterion = "1.4.3 Contrast (Minimum)",
                            Recommendation = "Increase text/background color contrast"
                        });
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Audits keyboard navigation.
    /// </summary>
    public List<AccessibilityAuditResult> AuditKeyboardNavigation()
    {
        var results = new List<AccessibilityAuditResult>();

        lock (_trackedElements)
        {
            foreach (var element in _trackedElements.OfType<Control>())
            {
                var automationId = AutomationProperties.GetAutomationId(element) ?? element.Name ?? "unknown";

                // Check if element is keyboard focusable
                if (!element.Focusable && element.IsTabStop)
                {
                    results.Add(new AccessibilityAuditResult
                    {
                        ElementId = automationId,
                        Issue = "Element is tab stop but not focusable",
                        Severity = "Error",
                        WCAGCriterion = "2.1.1 Keyboard",
                        Recommendation = "Set Focusable=true or remove from tab order"
                    });
                }

                // Check for automation name
                var name = AutomationProperties.GetName(element);
                if (string.IsNullOrEmpty(name))
                {
                    results.Add(new AccessibilityAuditResult
                    {
                        ElementId = automationId,
                        Issue = "Missing automation name (screen reader label)",
                        Severity = "Error",
                        WCAGCriterion = "4.1.2 Name, Role, Value",
                        Recommendation = "Set AutomationProperties.Name"
                    });
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Gets a summary of accessibility compliance.
    /// </summary>
    public AccessibilitySummary GetSummary()
    {
        var contrastIssues = AuditContrast();
        var keyboardIssues = AuditKeyboardNavigation();

        return new AccessibilitySummary
        {
            TargetLevel = _config.TargetCompliance,
            TotalElements = _trackedElements.Count,
            ContrastIssues = contrastIssues.Count,
            KeyboardIssues = keyboardIssues.Count,
            IsCompliant = contrastIssues.Count == 0 && keyboardIssues.Count == 0,
            HighContrastMode = _config.HighContrastEnabled,
            ScreenReaderOptimized = _config.ScreenReaderOptimized
        };
    }

    /// <summary>
    /// Updates configuration based on system accessibility settings.
    /// </summary>
    public void RefreshSystemSettings()
    {
        LoadSystemSettings();
        Logger.Info("AccessibilityService", "System accessibility settings refreshed");
    }

    private void LoadSystemSettings()
    {
        try
        {
            // Check high contrast
            _config.HighContrastEnabled = SystemParameters.HighContrast;

            // Check reduced motion
            _config.ReducedMotion = SystemParameters.ClientAreaAnimation;

            // Text scale (would read from system settings)
            _config.TextScale = 1.0;

            // Screen reader detection (simplified)
            _config.ScreenReaderOptimized = _config.HighContrastEnabled;
        }
        catch (Exception ex)
        {
            Logger.Warning("AccessibilityService", $"Failed to load system settings: {ex.Message}");
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
/// Accessibility compliance summary.
/// </summary>
public class AccessibilitySummary
{
    public AccessibilityLevel TargetLevel { get; set; }
    public int TotalElements { get; set; }
    public int ContrastIssues { get; set; }
    public int KeyboardIssues { get; set; }
    public bool IsCompliant { get; set; }
    public bool HighContrastMode { get; set; }
    public bool ScreenReaderOptimized { get; set; }

    public string Status => IsCompliant ? "Compliant" : "Non-Compliant";
}
