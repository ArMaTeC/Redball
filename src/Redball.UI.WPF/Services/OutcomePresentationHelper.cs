using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Redball.UI.Services;

public sealed class OutcomePresentation
{
    public string ValueText { get; init; } = string.Empty;
    public string HintText { get; init; } = string.Empty;
    public Brush ValueForeground { get; init; } = Brushes.Transparent;
    public Brush? HintForeground { get; init; }
    public FontWeight ValueFontWeight { get; init; } = FontWeights.Normal;
    public FontWeight? HintFontWeight { get; init; }
}

public sealed class DashboardOutcomeRowPresentation
{
    public string Label { get; init; } = string.Empty;
    public string ToolTip { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string HintText { get; init; } = string.Empty;
    public Brush ValueForeground { get; init; } = Brushes.Transparent;
    public Brush? HintForeground { get; init; }
    public FontWeight ValueFontWeight { get; init; } = FontWeights.Normal;
    public FontWeight? HintFontWeight { get; init; }
}

public static class OutcomePresentationHelper
{
    public static OutcomePresentation CreateOutcome(double rate, int successes, int attempts)
    {
        var sampleNote = attempts < 5 ? " low sample" : string.Empty;
        return new OutcomePresentation
        {
            ValueText = $"{rate:F0}% ({successes}/{attempts}){sampleNote}",
            ValueForeground = GetPrimaryBrush(rate, attempts),
            ValueFontWeight = FontWeights.SemiBold
        };
    }

    public static OutcomePresentation CreateOutcome(double rate, int successes, int attempts, string successLabel, string attemptLabel)
    {
        var sampleNote = attempts < 5 ? " - interpret carefully" : string.Empty;
        return new OutcomePresentation
        {
            ValueText = $"{rate:F0}% ({successes}/{attempts}){(attempts < 5 ? " low sample" : string.Empty)}",
            HintText = $"{successLabel}: {successes} of {attempts} {attemptLabel}{sampleNote}",
            ValueForeground = GetPrimaryBrush(rate, attempts),
            HintForeground = GetSecondaryBrush(rate, attempts),
            ValueFontWeight = FontWeights.Bold
        };
    }

    public static void ApplyOutcome(TextBlock valueText, OutcomePresentation presentation)
    {
        valueText.Text = presentation.ValueText;
        valueText.Foreground = presentation.ValueForeground;
        valueText.FontWeight = presentation.ValueFontWeight;
    }

    public static void ApplyOutcome(TextBlock valueText, TextBlock hintText, OutcomePresentation presentation)
    {
        valueText.Text = presentation.ValueText;
        valueText.Foreground = presentation.ValueForeground;
        valueText.FontWeight = presentation.ValueFontWeight;
        hintText.Text = presentation.HintText;
        if (presentation.HintForeground != null)
        {
            hintText.Foreground = presentation.HintForeground;
        }
        if (presentation.HintFontWeight.HasValue)
        {
            hintText.FontWeight = presentation.HintFontWeight.Value;
        }
    }

    public static DashboardOutcomeRowPresentation CreateRow(string label, string toolTip, double rate, int successes, int attempts, string successLabel, string attemptLabel)
    {
        var presentation = CreateOutcome(rate, successes, attempts, successLabel, attemptLabel);
        return new DashboardOutcomeRowPresentation
        {
            Label = label,
            ToolTip = toolTip,
            ValueText = presentation.ValueText,
            HintText = presentation.HintText,
            ValueForeground = presentation.ValueForeground,
            HintForeground = presentation.HintForeground,
            ValueFontWeight = presentation.ValueFontWeight,
            HintFontWeight = presentation.HintFontWeight
        };
    }

    private static Brush GetPrimaryBrush(double rate, int attempts)
    {
        if (attempts < 5)
        {
            return new SolidColorBrush(Color.FromRgb(217, 119, 6));
        }

        if (rate >= 80)
        {
            return new SolidColorBrush(Color.FromRgb(34, 197, 94));
        }

        if (rate < 50)
        {
            return new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }

        return new SolidColorBrush(Color.FromRgb(59, 130, 246));
    }

    private static Brush GetSecondaryBrush(double rate, int attempts)
    {
        if (attempts < 5)
        {
            return new SolidColorBrush(Color.FromRgb(245, 158, 11));
        }

        if (rate >= 80)
        {
            return new SolidColorBrush(Color.FromRgb(134, 239, 172));
        }

        if (rate < 50)
        {
            return new SolidColorBrush(Color.FromRgb(252, 165, 165));
        }

        return new SolidColorBrush(Color.FromRgb(147, 197, 253));
    }
}
