using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Redball.UI.Services;

namespace Redball.UI.Views;

public partial class UpdateAvailableWindow : Window
{
    private readonly UpdateInfo _updateInfo;
    private readonly UpdateService _updateService;
    private readonly AnalyticsService _analytics = new(ConfigService.Instance.Config.EnableTelemetry);

    /// <summary>
    /// Set to true if the user chose "Don't remind me for this version".
    /// </summary>
    public bool SkipThisVersion { get; private set; }

    /// <summary>
    /// Set to true if the user clicked "Update Now".
    /// </summary>
    public bool UserChoseUpdate { get; private set; }

    public UpdateAvailableWindow(UpdateInfo updateInfo, UpdateService updateService, List<VersionChangelog> changelogs)
    {
        _updateInfo = updateInfo;
        _updateService = updateService;
        InitializeComponent();

        VersionSummaryText.Text = $"A new version of Redball is available: {updateInfo.VersionDisplay}  (you have v{updateInfo.CurrentVersion.Major}.{updateInfo.CurrentVersion.Minor}.{updateInfo.CurrentVersion.Build})";

        if (changelogs.Count == 0)
        {
            VersionCountText.Text = "No detailed changelog available.";
        }
        else if (changelogs.Count == 1)
        {
            VersionCountText.Text = "1 new version since your current release:";
        }
        else
        {
            VersionCountText.Text = $"{changelogs.Count} new versions since your current release:";
        }

        BuildChangelogUI(changelogs);
    }

    private void BuildChangelogUI(List<VersionChangelog> changelogs)
    {
        foreach (var entry in changelogs)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("CardBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(14)
            };

            var stack = new StackPanel();

            // Version header row
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var versionText = new TextBlock
            {
                Text = entry.VersionDisplay,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(versionText);

            if (entry.IsPreRelease)
            {
                var betaBadge = new Border
                {
                    Background = (Brush)FindResource("AccentBrush"),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(6, 1, 6, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                betaBadge.Child = new TextBlock
                {
                    Text = "Beta",
                    FontSize = 10,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Medium
                };
                headerPanel.Children.Add(betaBadge);
            }

            var dateText = new TextBlock
            {
                Text = entry.DateDisplay,
                FontSize = 11,
                Foreground = (Brush)FindResource("ForegroundSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            headerPanel.Children.Add(dateText);

            stack.Children.Add(headerPanel);

            // Release notes body
            if (!string.IsNullOrWhiteSpace(entry.ReleaseNotes))
            {
                var notesText = new TextBlock
                {
                    Text = entry.ReleaseNotes.Trim(),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("ForegroundSecondaryBrush"),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                stack.Children.Add(notesText);
            }
            else
            {
                var noNotes = new TextBlock
                {
                    Text = "No release notes for this version.",
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("ForegroundSecondaryBrush"),
                    Margin = new Thickness(0, 8, 0, 0),
                    Opacity = 0.6
                };
                stack.Children.Add(noNotes);
            }

            card.Child = stack;
            ChangelogPanel.Children.Add(card);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SkipThisVersion = DontRemindCheck.IsChecked == true;
        UserChoseUpdate = false;
        _analytics.TrackFeature("update.dialog_closed");
        DialogResult = false;
        Close();
    }

    private void RemindLaterButton_Click(object sender, RoutedEventArgs e)
    {
        SkipThisVersion = DontRemindCheck.IsChecked == true;
        UserChoseUpdate = false;
        _analytics.TrackFeature("update.remind_later");
        DialogResult = false;
        Close();
    }

    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("update.accepted");
        UserChoseUpdate = true;

        // Disable buttons to prevent double-click
        var updateBtn = (Button)sender;
        updateBtn.IsEnabled = false;
        updateBtn.Content = "Downloading...";

        var progressWindow = new UpdateProgressWindow();
        progressWindow.Show();

        var progress = new Progress<UpdateDownloadProgress>(dp => progressWindow.UpdateProgress(dp));
        bool success = await _updateService.DownloadAndInstallAsync(_updateInfo, progress);

        progressWindow.Close();

        if (success)
        {
            _analytics.TrackFeature("update.download_succeeded");
            DialogResult = true;
            Close();
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.ExitApplication();
                return;
            }

            Application.Current.Shutdown();
        }
        else
        {
            _analytics.TrackFeature("update.download_failed");
            updateBtn.IsEnabled = true;
            updateBtn.Content = "Update Now";
            NotificationWindow.Show("Update Failed", "Could not download or install the update. Please check your internet connection or the logs for details.", "\uE783");
        }
    }
}
