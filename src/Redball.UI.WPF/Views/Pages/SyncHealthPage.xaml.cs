namespace Redball.UI.WPF.Views.Pages;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Redball.Core.Sync;
using Redball.UI.Services;

/// <summary>
/// Sync health dashboard showing outbox queue status and reconciliation state.
/// </summary>
public partial class SyncHealthPage : Page
{
    private readonly IOutboxStore _store;
    private readonly DispatcherTimer _refreshTimer;

    public SyncHealthPage()
    {
        InitializeComponent();

        // Get store from service locator (or create new for diagnostics)
        _store = Redball.UI.Services.ServiceLocator.OutboxStore ?? new SqliteOutboxStore();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += async (s, e) => await RefreshAsync();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        _refreshTimer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var stats = await _store.GetStatisticsAsync();
            UpdateUI(stats);
        }
        catch (Exception ex)
        {
            Logger.Error("SyncHealthPage", "Failed to refresh stats", ex);
        }
    }

    private void UpdateUI(SyncStatistics stats)
    {
        // Queue depth indicator
        QueueDepthText.Text = stats.PendingCount.ToString();
        QueueDepthBar.Value = Math.Min(stats.PendingCount, 100);

        // Status indicators
        InFlightText.Text = stats.InFlightCount.ToString();
        CompletedText.Text = stats.CompletedCount.ToString();
        DeadLetterText.Text = stats.DeadLetterCount.ToString();

        // Age indicator
        if (stats.OldestPendingAge.HasValue)
        {
            var age = stats.OldestPendingAge.Value;
            OldestPendingText.Text = age.TotalHours > 1
                ? $"{age.TotalHours:F1} hours"
                : $"{age.TotalMinutes:F0} minutes";

            // Color code by age
            if (age > TimeSpan.FromHours(1))
                OldestPendingText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            else if (age > TimeSpan.FromMinutes(30))
                OldestPendingText.Foreground = System.Windows.Media.Brushes.Orange;
            else
                OldestPendingText.Foreground = System.Windows.Media.Brushes.Green;
        }
        else
        {
            OldestPendingText.Text = "—";
            OldestPendingText.Foreground = System.Windows.Media.Brushes.Gray;
        }

        // Last success
        LastSuccessText.Text = stats.LastSuccessfulSync.HasValue
            ? stats.LastSuccessfulSync.Value.ToString("g")
            : "Never";

        // Average retries
        AvgRetriesText.Text = stats.AvgRetriesPerEvent.ToString("F1");

        // Overall health indicator
        UpdateHealthIndicator(stats);
    }

    private void UpdateHealthIndicator(SyncStatistics stats)
    {
        if (stats.DeadLetterCount > 0)
        {
            HealthStatusText.Text = "Needs Attention";
            HealthStatusIcon.Text = "\uE7BA"; // Warning
            HealthStatusPanel.Background = System.Windows.Media.Brushes.LightYellow;
        }
        else if (stats.PendingCount > 50)
        {
            HealthStatusText.Text = "Backlog";
            HealthStatusIcon.Text = "\uE7C5"; // Info
            HealthStatusPanel.Background = System.Windows.Media.Brushes.LightBlue;
        }
        else if (stats.PendingCount > 0)
        {
            HealthStatusText.Text = "Syncing";
            HealthStatusIcon.Text = "\uE895"; // Sync
            HealthStatusPanel.Background = System.Windows.Media.Brushes.LightGreen;
        }
        else
        {
            HealthStatusText.Text = "Healthy";
            HealthStatusIcon.Text = "\uE8FB"; // Checkmark
            HealthStatusPanel.Background = System.Windows.Media.Brushes.LightGreen;
        }
    }

    private async void ForceSyncButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ForceSyncButton.IsEnabled = false;
            ForceSyncButton.Content = "Syncing...";

            // TODO: Implement force sync when dispatcher is available
            // For now just refresh the display
            await Task.Delay(500); // Simulate work
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("SyncHealthPage", "Force sync failed", ex);
            System.Windows.MessageBox.Show($"Sync failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ForceSyncButton.IsEnabled = true;
            ForceSyncButton.Content = "Force Sync Now";
        }
    }

    private async void ViewDeadLetterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var events = await _store.GetEventsByStatusAsync(SyncEventStatus.DeadLetter, 50);
            var dialog = new SyncEventsDialog(events.ToList(), "Dead Letter Events");
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            Logger.Error("SyncHealthPage", "Failed to load dead letter events", ex);
        }
    }

    private async void PurgeCompletedButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Purge completed events older than 7 days?",
            "Confirm Purge",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var purged = await _store.PurgeCompletedAsync(TimeSpan.FromDays(7));
            System.Windows.MessageBox.Show($"Purged {purged} completed events.", "Purge Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("SyncHealthPage", "Purge failed", ex);
        }
    }
}
