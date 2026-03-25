using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Product Metrics Dashboard showing key performance indicators and cohort analysis.
/// </summary>
public partial class ProductMetricsDashboard : Window
{
    public ProductMetricsDashboard()
    {
        InitializeComponent();
        LoadMetrics();
    }

    private void LoadMetrics()
    {
        try
        {
            var analytics = new AnalyticsService(true);
            var summary = analytics.GetSummary();
            var topCategory = summary.TopCategories.FirstOrDefault();
            var topCategoryTrend = summary.CategoryTrends.FirstOrDefault();

            // Calculate metrics
            ActiveUsersText.Text = summary.RecentSessions.ToString("N0");
            ActiveUsersTrend.Text = $"vs prior 7d: {summary.PriorSessions:N0} ({FormatTrend(summary.SessionTrendPercent)})";

            SessionDurationText.Text = $"{summary.AverageSessionDuration.TotalMinutes:F0}m";

            FeatureAdoptionText.Text = $"{summary.FeatureAdoptionRate:F0}%";
            FeatureAdoptionTrend.Text = topCategory != null
                ? $"Top category: {topCategory.Name} ({FormatTrend(topCategoryTrend?.TrendPercent ?? 0)})"
                : "Top: None";

            var retention = Math.Max(summary.RetentionDay7, summary.RetentionDay30);
            RetentionText.Text = $"{retention:F0}%";
            RetentionTrend.Text = summary.LastFeatureUse == default
                ? "No recent activity"
                : $"Last activity: {summary.LastFeatureUse.ToLocalTime():g}";

            JourneyOutcomesList.ItemsSource = new[]
            {
                OutcomePresentationHelper.CreateRow(
                    "TypeThing completion",
                    "Completed TypeThing runs divided by TypeThing starts.",
                    summary.TypeThingSuccessRate,
                    summary.TypeThingCompletions,
                    summary.TypeThingAttempts,
                    "Completions",
                    "starts"),
                OutcomePresentationHelper.CreateRow(
                    "Settings save success",
                    "Successful settings saves divided by all save attempts, including validation failures.",
                    summary.SettingsSaveSuccessRate,
                    summary.SettingsSaves,
                    summary.SettingsSaveAttempts,
                    "Successful saves",
                    "attempts"),
                OutcomePresentationHelper.CreateRow(
                    "Update success",
                    "Successful update downloads divided by update download attempts.",
                    summary.UpdateSuccessRate,
                    summary.UpdateSuccesses,
                    summary.UpdateAttempts,
                    "Successful downloads",
                    "attempts"),
                OutcomePresentationHelper.CreateRow(
                    "Diagnostics export rate",
                    "Diagnostics exports divided by diagnostics window opens.",
                    summary.DiagnosticsExportRate,
                    summary.DiagnosticsExports,
                    summary.DiagnosticsOpens,
                    "Exports",
                    "opens"),
                OutcomePresentationHelper.CreateRow(
                    "Onboarding completion",
                    "Completed onboarding flows divided by onboarding starts shown to the user.",
                    summary.OnboardingCompletionRate,
                    summary.OnboardingCompletions,
                    summary.OnboardingStarts,
                    "Completions",
                    "starts shown")
            };
            
            // Update category bars
            KeepAwakeBar.Value = 0;
            TypeThingBar.Value = 0;
            ScheduleBar.Value = 0;
            BatteryBar.Value = 0;
            NetworkBar.Value = 0;

            foreach (var category in summary.TopCategories)
            {
                var percentage = Math.Min(100, category.Count * 100.0 / Math.Max(1, summary.TotalFeatureEvents));
                UpdateFeatureBar(category.Name, percentage);
            }
            
            LastUpdatedText.Text = $"Last updated: {summary.LastUpdated.ToLocalTime():g}";
        }
        catch (Exception ex)
        {
            Logger.Error("ProductMetricsDashboard", "Failed to load metrics", ex);
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

    private void UpdateFeatureBar(string featureName, double percentage)
    {
        switch (featureName)
        {
            case "Core Usage":
                KeepAwakeBar.Value = percentage;
                break;
            case "TypeThing":
                TypeThingBar.Value = percentage;
                break;
            case "Onboarding":
            case "Settings":
                ScheduleBar.Value = percentage;
                break;
            case "Diagnostics":
                BatteryBar.Value = percentage;
                break;
            case "Updates":
            case "Insights":
            case "Other":
                NetworkBar.Value = percentage;
                break;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadMetrics();
    }

    private void ExportMetricsLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|JSON files (*.json)|*.json",
                FileName = $"redball_metrics_{DateTime.Now:yyyyMMdd}.csv",
                Title = "Export Metrics Report"
            };

            if (dialog.ShowDialog() == true)
            {
                var analytics = new AnalyticsService(true);
                var summary = analytics.GetSummary();
                
                var csv = "Metric,Value\n" +
                         $"Total Sessions,{summary.TotalSessions}\n" +
                         $"Total Usage Time (minutes),{summary.TotalUsageTime.TotalMinutes:F0}\n" +
                         $"Average Session Duration (minutes),{summary.AverageSessionDuration.TotalMinutes:F0}\n" +
                         $"Total Feature Events,{summary.TotalFeatureEvents}\n" +
                         $"Feature Adoption Rate,{summary.FeatureAdoptionRate:F0}%\n" +
                         $"Retention Day 7,{summary.RetentionDay7:F0}%\n" +
                         $"Retention Day 30,{summary.RetentionDay30:F0}%\n" +
                         $"TypeThing Success Rate,{summary.TypeThingSuccessRate:F0}% ({summary.TypeThingCompletions}/{summary.TypeThingAttempts})\n" +
                         $"Settings Save Success Rate,{summary.SettingsSaveSuccessRate:F0}% ({summary.SettingsSaves}/{summary.SettingsSaveAttempts})\n" +
                         $"Update Success Rate,{summary.UpdateSuccessRate:F0}% ({summary.UpdateSuccesses}/{summary.UpdateAttempts})\n" +
                         $"Diagnostics Export Rate,{summary.DiagnosticsExportRate:F0}% ({summary.DiagnosticsExports}/{summary.DiagnosticsOpens})\n" +
                         $"Onboarding Completion Rate,{summary.OnboardingCompletionRate:F0}% ({summary.OnboardingCompletions}/{summary.OnboardingStarts})\n" +
                         $"First Seen,{summary.FirstSeen:yyyy-MM-dd}\n" +
                         $"Last Updated,{summary.LastUpdated:yyyy-MM-dd HH:mm}\n" +
                         "\nCategory,Usage Count\n";
                
                foreach (var category in summary.TopCategories)
                {
                    csv += $"{category.Name},{category.Count}\n";
                }
                
                File.WriteAllText(dialog.FileName, csv);
                
                Logger.Info("ProductMetricsDashboard", $"Metrics exported to: {dialog.FileName}");
                NotificationWindow.Show("Export Complete", "Metrics report exported successfully!", "\uE73E");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ProductMetricsDashboard", "Failed to export metrics", ex);
            NotificationWindow.Show("Error", $"Export failed: {ex.Message}", "\uE783");
        }
    }

    private void FeedbackButton_Click(object sender, RoutedEventArgs e)
    {
        var feedbackWindow = new UserFeedbackSurvey();
        feedbackWindow.ShowDialog();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
