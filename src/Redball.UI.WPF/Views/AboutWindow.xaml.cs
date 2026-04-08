using System.Diagnostics;
using System.Windows;
using Redball.UI.Services;

namespace Redball.UI.Views;

public partial class AboutWindow : Window
{
    private UpdateService? _updateService;
    private readonly AnalyticsService _analytics = AnalyticsService.Instance;

    public AboutWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        
        // Initialize update service
        var cfg = Services.ConfigService.Instance.Config;
        _updateService = new UpdateService(
            cfg.UpdateRepoOwner,
            cfg.UpdateRepoName,
            cfg.UpdateChannel,
            cfg.VerifyUpdateSignature,
            "https://redball.certrunnerx.com/");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Redball v{version?.Major}.{version?.Minor}.{version?.Build}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("github.opened");
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/ArMaTeC/Redball",
            UseShellExecute = true
        });
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        _analytics.TrackFeature("update.check_started");

        // Show checking dialog
        var checking = NotificationWindow.Show("Check for Updates", "Checking for updates...", "\uE896", true);
        if (!checking)
        {
            _analytics.TrackFeature("update.check_cancelled");
            return;
        }

        if (_updateService == null)
        {
            _analytics.TrackFeature("update.check_failed");
            NotificationWindow.Show("Update Failed", "Update service is unavailable. Please restart Redball and try again.", "\uE783");
            return;
        }

        // Check for updates
        var updateInfo = await _updateService.CheckForUpdateAsync();
        
        if (updateInfo == null)
        {
            _analytics.TrackFeature("update.not_available");
            NotificationWindow.Show("Up to Date", "You're running the latest version of Redball.", "\uE73E"); 
            return;
        }

        _analytics.TrackFeature("update.available");

        // Show the full changelog dialog instead of jumping straight to download
        if (Application.Current.MainWindow is MainWindow mw)
        {
            await mw.ShowUpdateAvailableDialogAsync(updateInfo);
        }
    }
}
