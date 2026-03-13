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

            // Calculate metrics
            ActiveUsersText.Text = summary.TotalSessions.ToString("N0");
            
            var avgDuration = summary.TotalSessions > 0 
                ? summary.TotalUsageTime.TotalMinutes / summary.TotalSessions 
                : 0;
            SessionDurationText.Text = $"{avgDuration:F0}m";
            
            // Feature adoption (percentage of users who used advanced features)
            var featureCount = summary.TopFeatures.Count;
            var adoption = featureCount > 0 ? Math.Min(100, featureCount * 20) : 0;
            FeatureAdoptionText.Text = $"{adoption:F0}%";
            
            // Calculate retention based on return visits
            var daysSinceFirst = (DateTime.UtcNow - summary.FirstSeen).TotalDays;
            var retention = daysSinceFirst > 0 
                ? Math.Min(100, (summary.TotalSessions / daysSinceFirst) * 30) 
                : 0;
            RetentionText.Text = $"{retention:F0}%";
            
            // Update feature bars
            foreach (var feature in summary.TopFeatures)
            {
                var percentage = Math.Min(100, feature.Count * 100 / Math.Max(1, summary.TotalSessions));
                UpdateFeatureBar(feature.Name, percentage);
            }
            
            LastUpdatedText.Text = $"Last updated: {DateTime.Now:g}";
        }
        catch (Exception ex)
        {
            Logger.Error("ProductMetricsDashboard", "Failed to load metrics", ex);
        }
    }

    private void UpdateFeatureBar(string featureName, double percentage)
    {
        switch (featureName)
        {
            case "KeepAwakeToggle":
                KeepAwakeBar.Value = percentage;
                break;
            case "TypeThing":
                TypeThingBar.Value = percentage;
                break;
            case "Schedule":
                ScheduleBar.Value = percentage;
                break;
            case "BatteryAware":
                BatteryBar.Value = percentage;
                break;
            case "NetworkAware":
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
                         $"First Seen,{summary.FirstSeen:yyyy-MM-dd}\n" +
                         $"Last Updated,{summary.LastUpdated:yyyy-MM-dd HH:mm}\n" +
                         "\nFeature,Usage Count\n";
                
                foreach (var feature in summary.TopFeatures)
                {
                    csv += $"{feature.Name},{feature.Count}\n";
                }
                
                File.WriteAllText(dialog.FileName, csv);
                
                Logger.Info("ProductMetricsDashboard", $"Metrics exported to: {dialog.FileName}");
                MessageBox.Show("Metrics report exported successfully!", "Export Complete", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ProductMetricsDashboard", "Failed to export metrics", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
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
