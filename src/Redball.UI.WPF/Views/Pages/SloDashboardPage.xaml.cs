namespace Redball.UI.WPF.Views.Pages;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Redball.Core.Performance;

/// <summary>
/// SLO dashboard for startup performance monitoring.
/// </summary>
public partial class SloDashboardPage : Page
{
    private readonly DispatcherTimer _refreshTimer;

    public SloDashboardPage()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += (s, e) => RefreshStats();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshStats();
        _refreshTimer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
    }

    private void RefreshStats()
    {
        try
        {
            var stats = StartupTimingService.Instance.GetSloStatistics(TimeSpan.FromDays(7));
            UpdateUI(stats);
        }
        catch (Exception ex)
        {
            Logger.Error("SloDashboard", "Failed to refresh stats", ex);
        }
    }

    private void UpdateUI(SloStatistics stats)
    {
        // SLO Targets
        ColdStartTargetText.Text = $"{StartupTimingService.ColdStartSloSeconds:F1}s";
        WarmStartTargetText.Text = $"{StartupTimingService.WarmStartSloSeconds:F1}s";

        // Current stats
        TotalStartsText.Text = stats.TotalStarts.ToString();
        ColdStartPassRateText.Text = $"{stats.ColdStartPassRate:P0}";
        WarmStartPassRateText.Text = $"{stats.WarmStartPassRate:P0}";

        // Color code pass rates
        ColdStartPassRateText.Foreground = stats.ColdStartPassRate >= 0.95
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.OrangeRed;

        WarmStartPassRateText.Foreground = stats.WarmStartPassRate >= 0.99
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.OrangeRed;

        // Average times
        AvgColdStartText.Text = $"{stats.AvgColdStartSeconds:F2}s";
        AvgWarmStartText.Text = $"{stats.AvgWarmStartSeconds:F2}s";

        // P95 times
        P95ColdStartText.Text = $"{stats.P95ColdStartSeconds:F2}s";
        P95WarmStartText.Text = $"{stats.P95WarmStartSeconds:F2}s";

        // Health indicator
        if (stats.IsHealthy)
        {
            HealthStatusText.Text = "Healthy";
            HealthStatusIcon.Text = "\uE8FB"; // Checkmark
            HealthStatusPanel.Background = System.Windows.Media.Brushes.LightGreen;
        }
        else if (stats.TotalStarts == 0)
        {
            HealthStatusText.Text = "No Data";
            HealthStatusIcon.Text = "\uE9CE"; // Unknown
            HealthStatusPanel.Background = System.Windows.Media.Brushes.LightGray;
        }
        else
        {
            HealthStatusText.Text = "Degraded";
            HealthStatusIcon.Text = "\uE7BA"; // Warning
            HealthStatusPanel.Background = System.Windows.Media.Brushes.LightYellow;
        }
    }

    private void ViewHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var history = StartupTimingService.Instance.GetHistory();
        var dialog = new StartupHistoryDialog(history);
        dialog.ShowDialog();
    }

    private void ExportReportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var stats = StartupTimingService.Instance.GetSloStatistics(TimeSpan.FromDays(7));
            var json = System.Text.Json.JsonSerializer.Serialize(stats, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"redball_slo_report_{DateTime.Now:yyyyMMdd}.json");

            System.IO.File.WriteAllText(path, json);
            MessageBox.Show($"Report exported to:\n{path}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
