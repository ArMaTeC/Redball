using System.Diagnostics;
using System.Windows;
using Redball.UI.Services;

namespace Redball.UI.Views;

public partial class AboutWindow : Window
{
    private UpdateService? _updateService;
    private readonly AnalyticsService _analytics = new(ConfigService.Instance.Config.EnableTelemetry);

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
            cfg.VerifyUpdateSignature);
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

        // Check for updates
        var updateInfo = await _updateService.CheckForUpdateAsync();
        
        if (updateInfo == null)
        {
            _analytics.TrackFeature("update.not_available");
            NotificationWindow.Show("Up to Date", "Your version of Redball is already current.", "\uE73E"); 
            return;
        }

        _analytics.TrackFeature("update.available");

        // Download and install
        var progressWindow = new UpdateProgressWindow();
        progressWindow.Show();

        var progress = new Progress<UpdateDownloadProgress>(dp => progressWindow.UpdateProgress(dp));
        
        bool success = await _updateService.DownloadAndInstallAsync(updateInfo, progress);
        
        progressWindow.Close();

        if (success)
        {
            _analytics.TrackFeature("update.download_succeeded");
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
            NotificationWindow.Show("Update Failed", "Could not download or install the update. Please check the log for details.", "\uE783");
        }
    }
}
