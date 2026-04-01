using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Analytics dashboard showing usage statistics.
/// All data is stored locally and user-controlled.
/// </summary>
public partial class AnalyticsDashboard : Window
{
    public AnalyticsDashboard()
    {
        InitializeComponent();
        LoadData();
        
        // Set initial checkbox state
        EnableAnalyticsCheck.IsChecked = ConfigService.Instance.Config.EnableTelemetry;
    }

    private void LoadData()
    {
        try
        {
            // Use singleton analytics service to read data
            var analytics = AnalyticsService.Instance;
            var summary = analytics.GetSummary();
            var leadingCategory = summary.CategoryTrends.FirstOrDefault();

            SessionsText.Text = summary.TotalSessions.ToString();
            SessionsTrendText.Text = $"Last 7d: {summary.RecentSessions} vs prior 7d: {summary.PriorSessions} ({FormatTrend(summary.SessionTrendPercent)})";
            
            // Format usage time
            var hours = (int)summary.TotalUsageTime.TotalHours;
            var minutes = summary.TotalUsageTime.Minutes;
            UsageTimeText.Text = $"{hours}h {minutes}m";
            var typeThingOutcome = OutcomePresentationHelper.CreateOutcome(summary.TypeThingSuccessRate, summary.TypeThingCompletions, summary.TypeThingAttempts);
            var settingsOutcome = OutcomePresentationHelper.CreateOutcome(summary.SettingsSaveSuccessRate, summary.SettingsSaves, summary.SettingsSaveAttempts);
            UsageOutcomeList.ItemsSource = new[]
            {
                new OutcomePresentation
                {
                    ValueText = $"TypeThing: {typeThingOutcome.ValueText}",
                    ValueForeground = typeThingOutcome.ValueForeground,
                    ValueFontWeight = typeThingOutcome.ValueFontWeight
                },
                new OutcomePresentation
                {
                    ValueText = $"Settings: {settingsOutcome.ValueText}",
                    ValueForeground = settingsOutcome.ValueForeground,
                    ValueFontWeight = settingsOutcome.ValueFontWeight
                }
            };
            
            // Member since
            MemberSinceText.Text = summary.FirstSeen.ToString("MMM yyyy");
            var onboardingOutcome = OutcomePresentationHelper.CreateOutcome(summary.OnboardingCompletionRate, summary.OnboardingCompletions, summary.OnboardingStarts);
            var diagnosticsOutcome = OutcomePresentationHelper.CreateOutcome(summary.DiagnosticsExportRate, summary.DiagnosticsExports, summary.DiagnosticsOpens);
            CoverageOutcomeList.ItemsSource = new[]
            {
                new OutcomePresentation
                {
                    ValueText = $"Onboarding: {onboardingOutcome.ValueText}",
                    ValueForeground = onboardingOutcome.ValueForeground,
                    ValueFontWeight = onboardingOutcome.ValueFontWeight
                },
                new OutcomePresentation
                {
                    ValueText = $"Diagnostics: {diagnosticsOutcome.ValueText}",
                    ValueForeground = diagnosticsOutcome.ValueForeground,
                    ValueFontWeight = diagnosticsOutcome.ValueFontWeight
                }
            };
            
            // Top categories
            FeaturesList.ItemsSource = summary.TopCategories.Select(feature => new
            {
                Name = feature.Name,
                Count = feature.Count,
                Share = summary.TotalFeatureEvents > 0
                    ? $"{(feature.Count * 100.0 / summary.TotalFeatureEvents):F0}%"
                    : "0%"
            }).ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("AnalyticsDashboard", "Failed to load analytics data", ex);
            SessionsText.Text = "-";
            UsageTimeText.Text = "-";
            MemberSinceText.Text = "-";
            SessionsTrendText.Text = "Trend unavailable";
            UsageOutcomeList.ItemsSource = new[] { new OutcomePresentation { ValueText = "Outcome context unavailable" } };
            CoverageOutcomeList.ItemsSource = new[] { new OutcomePresentation { ValueText = "Outcome context unavailable" } };
        }
    }

    private static string FormatTrend(double value)
    {
        if (value > 0)
        {
            return $"↑ {value:F0}%";
        }

        if (value < 0)
        {
            return $"↓ {Math.Abs(value):F0}%";
        }

        return "→ 0%";
    }

    private void EnableAnalyticsCheck_Checked(object sender, RoutedEventArgs e)
    {
        ConfigService.Instance.Config.EnableTelemetry = true;
        ConfigService.Instance.Save();
        Logger.Info("AnalyticsDashboard", "Analytics enabled by user");
    }

    private void EnableAnalyticsCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        ConfigService.Instance.Config.EnableTelemetry = false;
        ConfigService.Instance.Save();
        Logger.Info("AnalyticsDashboard", "Analytics disabled by user");
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "redball_analytics.json",
                Title = "Export Analytics Data"
            };

            if (dialog.ShowDialog() == true)
            {
                var analytics = AnalyticsService.Instance;
                var data = analytics.Export();
                File.WriteAllText(dialog.FileName, data);
                
                Logger.Info("AnalyticsDashboard", $"Analytics exported to: {dialog.FileName}");
                NotificationWindow.Show("Export Complete", "Analytics data exported successfully!", "\uE73E");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AnalyticsDashboard", "Failed to export analytics", ex);
            NotificationWindow.Show("Error", $"Export failed: {ex.Message}", "\uE783");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
