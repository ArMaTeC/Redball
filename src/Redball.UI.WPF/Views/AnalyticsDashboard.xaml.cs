using System;
using System.Diagnostics;
using System.IO;
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
            // Create temporary analytics service to read data
            var analytics = new AnalyticsService(true);
            var summary = analytics.GetSummary();

            SessionsText.Text = summary.TotalSessions.ToString();
            
            // Format usage time
            var hours = (int)summary.TotalUsageTime.TotalHours;
            var minutes = summary.TotalUsageTime.Minutes;
            UsageTimeText.Text = $"{hours}h {minutes}m";
            
            // Member since
            MemberSinceText.Text = summary.FirstSeen.ToString("MMM yyyy");
            
            // Top features
            FeaturesList.ItemsSource = summary.TopFeatures;
        }
        catch (Exception ex)
        {
            Logger.Error("AnalyticsDashboard", "Failed to load analytics data", ex);
            SessionsText.Text = "-";
            UsageTimeText.Text = "-";
            MemberSinceText.Text = "-";
        }
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
                var analytics = new AnalyticsService(true);
                var data = analytics.Export();
                File.WriteAllText(dialog.FileName, data);
                
                Logger.Info("AnalyticsDashboard", $"Analytics exported to: {dialog.FileName}");
                MessageBox.Show("Analytics data exported successfully!", "Export Complete", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("AnalyticsDashboard", "Failed to export analytics", ex);
            MessageBox.Show($"Export failed: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
