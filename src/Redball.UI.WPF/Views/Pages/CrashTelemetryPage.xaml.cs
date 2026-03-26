namespace Redball.UI.WPF.Views.Pages;

using System;
using System.Windows;
using System.Windows.Controls;
using Redball.Core.Telemetry;

/// <summary>
/// Crash telemetry and diagnostics consent/settings page.
/// </summary>
public partial class CrashTelemetryPage : Page
{
    public CrashTelemetryPage()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var service = CrashTelemetryService.Instance;
        ConsentCheckBox.IsChecked = service.ConsentGranted;
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        var service = CrashTelemetryService.Instance;
        var crashes = service.GetLocalCrashes(100);
        var pendingUploads = crashes.Count; // Simplified - would check queue folder in real impl

        if (service.ConsentGranted)
        {
            StatusText.Text = $"Crash reporting is enabled. {crashes.Count} crashes stored locally, {pendingUploads} pending upload.";
            StatusText.Foreground = System.Windows.Media.Brushes.Green;
        }
        else
        {
            StatusText.Text = "Crash reporting is disabled. Crashes are stored locally only.";
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        }
    }

    private void ConsentCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        CrashTelemetryService.Instance.ConsentGranted = true;
        UpdateStatusText();
    }

    private void ConsentCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        CrashTelemetryService.Instance.ConsentGranted = false;
        UpdateStatusText();
    }

    private async void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ExportDiagnosticsButton.IsEnabled = false;
            ExportDiagnosticsButton.Content = "Creating bundle...";

            var bundlePath = await CrashTelemetryService.Instance.CreateDiagnosticsBundleAsync();

            var result = MessageBox.Show(
                $"Diagnostics bundle created:\n{bundlePath}\n\nOpen containing folder?",
                "Diagnostics Export",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{bundlePath}\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create diagnostics bundle: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
            ExportDiagnosticsButton.Content = "Export Diagnostics Bundle";
        }
    }

    private void ViewRecentCrashesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var crashes = CrashTelemetryService.Instance.GetLocalCrashes(20).ToList();
            var dialog = new CrashReportsDialog(crashes);
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load crash reports: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PurgeOldCrashesButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Delete crash reports older than 30 days?",
            "Confirm Purge",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            var purged = CrashTelemetryService.Instance.PurgeOldCrashes(TimeSpan.FromDays(30));
            MessageBox.Show($"Deleted {purged} old crash reports.", "Purge Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateStatusText();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to purge old crashes: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
