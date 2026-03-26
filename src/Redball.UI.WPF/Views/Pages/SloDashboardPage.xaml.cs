namespace Redball.UI.WPF.Views.Pages;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Redball.Core.Performance;
using Redball.UI.Services;

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
            UpdateResourceBudgets();
            UpdateMemoryPressure();
        }
        catch (Exception ex)
        {
            Logger.Error("SloDashboard", "Failed to refresh stats", ex);
        }
    }

    private void UpdateMemoryPressure()
    {
        try
        {
            var status = MemoryPressureService.Instance.GetCurrentStatus();
            var level = MemoryPressureService.Instance.CurrentPressureLevel;

            // Update pressure text
            var levelText = level switch
            {
                MemoryPressureLevel.Normal => "✓ Normal",
                MemoryPressureLevel.Moderate => "⚠ Moderate",
                MemoryPressureLevel.High => "⚠ High",
                MemoryPressureLevel.Critical => "❌ Critical",
                _ => "Unknown"
            };

            MemoryPressureText.Text = $"{levelText} ({status.UsedPercent:F1}% used)";
            MemoryPressureText.Foreground = level switch
            {
                MemoryPressureLevel.Normal => System.Windows.Media.Brushes.Green,
                MemoryPressureLevel.Moderate => System.Windows.Media.Brushes.Orange,
                MemoryPressureLevel.High => System.Windows.Media.Brushes.Red,
                MemoryPressureLevel.Critical => System.Windows.Media.Brushes.DarkRed,
                _ => System.Windows.Media.Brushes.Gray
            };

            // Update detailed stats
            MemoryUsedText.Text = $"Used: {status.UsedMB}MB";
            MemoryAvailableText.Text = $"Available: {status.AvailableMB}MB";
            WorkingSetText.Text = $"App: {status.WorkingSetMB}MB";
        }
        catch (Exception ex)
        {
            Logger.Error("SloDashboard", "Failed to update memory pressure", ex);
            MemoryPressureText.Text = "Error loading status";
            MemoryPressureText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private void UpdateResourceBudgets()
    {
        try
        {
            var report = ResourceBudgetService.Instance.GenerateReport();
            var summary = ResourceBudgetService.Instance.GetSummary();

            // Update status text
            BudgetStatusText.Text = report.IsHealthy
                ? $"✓ All {report.ServicesWithinBudget} services within budget"
                : $"⚠ {report.ServicesOverBudget} services over budget";
            BudgetStatusText.Foreground = report.IsHealthy
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Orange;

            // Clear and rebuild service list
            ServiceBudgetsPanel.Children.Clear();

            foreach (var (serviceName, (avgCpu, avgRam, withinBudget)) in summary.OrderBy(s => s.Key))
            {
                var budget = ResourceBudgetService.Instance.GetBudget(serviceName);
                if (budget == null) continue;

                var item = CreateBudgetItem(serviceName, budget, avgCpu, avgRam, withinBudget);
                ServiceBudgetsPanel.Children.Add(item);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("SloDashboard", "Failed to update resource budgets", ex);
            BudgetStatusText.Text = "Error loading budgets";
            BudgetStatusText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private System.Windows.UIElement CreateBudgetItem(string serviceName, ServiceResourceBudget budget, double avgCpu, long avgRam, bool withinBudget)
    {
        var grid = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(0, 2, 0, 2)
        };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(140) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(100) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(100) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

        // Service name
        var nameText = new System.Windows.Controls.TextBlock
        {
            Text = serviceName,
            FontWeight = System.Windows.FontWeights.SemiBold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            FontSize = 11
        };
        System.Windows.Controls.Grid.SetColumn(nameText, 0);
        grid.Children.Add(nameText);

        // Critical badge
        if (budget.IsCritical)
        {
            var criticalBadge = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.LightCoral,
                CornerRadius = new System.Windows.CornerRadius(2),
                Padding = new Thickness(3, 1, 3, 1),
                Margin = new Thickness(3, 0, 0, 0),
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = "CRIT",
                    FontSize = 9,
                    Foreground = System.Windows.Media.Brushes.DarkRed
                }
            };
            System.Windows.Controls.Grid.SetColumn(criticalBadge, 1);
            grid.Children.Add(criticalBadge);
        }

        // CPU usage
        var cpuExceeds = avgCpu > budget.MaxCpuPercent;
        var cpuText = new System.Windows.Controls.TextBlock
        {
            Text = $"CPU: {avgCpu:F1}%/{budget.MaxCpuPercent:F0}%",
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11,
            Foreground = cpuExceeds ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Gray
        };
        System.Windows.Controls.Grid.SetColumn(cpuText, 2);
        grid.Children.Add(cpuText);

        // RAM usage
        var ramExceeds = avgRam > budget.MaxRamMB;
        var ramText = new System.Windows.Controls.TextBlock
        {
            Text = $"RAM: {avgRam}MB/{budget.MaxRamMB}MB",
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11,
            Foreground = ramExceeds ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Gray
        };
        System.Windows.Controls.Grid.SetColumn(ramText, 3);
        grid.Children.Add(ramText);

        // Status indicator
        var statusText = new System.Windows.Controls.TextBlock
        {
            Text = withinBudget ? "✓" : "⚠",
            FontSize = 14,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = withinBudget ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Orange
        };
        System.Windows.Controls.Grid.SetColumn(statusText, 4);
        grid.Children.Add(statusText);

        return grid;
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
        var history = StartupTimingService.Instance.GetHistory().ToList();
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

    private void RunPerfTestsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = new PerformanceTestConfig
            {
                EnableStartupTests = true,
                EnableSoakTests = false,
                EnableLeakDetection = true,
                LeakTestDuration = TimeSpan.FromMinutes(5)
            };

            PerformanceTestService.Instance.StartTesting(config);
            MessageBox.Show("Performance tests started. Check logs for results.", "Tests Started", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start tests: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
