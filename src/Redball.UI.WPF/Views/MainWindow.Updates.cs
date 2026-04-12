using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Partial class handling background update checking with changelog dialog.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _updateCheckTimer;
    private DispatcherTimer? _startupUpdateCheckTimer;
    private string? _skippedVersion;
    private bool _isShowingUpdateDialog;
    private bool _updateTimerStarted;

    private void EnsureUpdateService()
    {
        if (_updateService != null)
        {
            return;
        }

        var config = ConfigService.Instance.Config;
        _updateService = new UpdateService(
            config.UpdateRepoOwner,
            config.UpdateRepoName,
            config.UpdateChannel ?? "stable",
            config.VerifyUpdateSignature,
            config.UpdateServerUrl);
    }

    /// <summary>
    /// Starts the background update check timer if enabled in config.
    /// Call this from OnWindowLoaded after services are initialized.
    /// </summary>
    private void StartAutoUpdateCheck()
    {
        if (_updateTimerStarted)
        {
            Logger.Debug("MainWindow", "Auto update check timer already started, skipping");
            return;
        }

        StopAutoUpdateCheck();
        _updateTimerStarted = true;

        var config = ConfigService.Instance.Config;

        if (!config.AutoUpdateCheckEnabled)
        {
            Logger.Info("MainWindow", "Auto update check is disabled in config");
            return;
        }

        if (string.Equals(config.UpdateChannel, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Info("MainWindow", "Update channel is disabled, skipping auto update check");
            return;
        }

        var intervalMinutes = Math.Max(30, config.AutoUpdateCheckIntervalMinutes);
        Logger.Info("MainWindow", $"Starting auto update check timer (interval: {intervalMinutes} min)");

        _updateCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(intervalMinutes)
        };
        _updateCheckTimer.Tick += async (_, _) => await PerformAutoUpdateCheckAsync();
        _updateCheckTimer.Start();

        // Also run the first check after a short delay (30 seconds) so the app has time to fully initialize
        _startupUpdateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _startupUpdateCheckTimer.Tick += async (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            _startupUpdateCheckTimer = null;
            await PerformAutoUpdateCheckAsync();
        };
        _startupUpdateCheckTimer.Start();
    }

    private async System.Threading.Tasks.Task PerformAutoUpdateCheckAsync()
    {
        if (_isShowingUpdateDialog)
        {
            Logger.Debug("MainWindow", "Update dialog already showing, skipping auto check");
            return;
        }

        try
        {
            // Ensure update service is available
            EnsureUpdateService();

            if (_updateService == null)
            {
                Logger.Debug("MainWindow", "Update service not available for auto check");
                return;
            }

            Logger.Debug("MainWindow", "Running auto update check...");
            _analytics.TrackFeature("update.auto_check");

            var updateInfo = await _updateService.CheckForUpdateAsync();

            if (updateInfo == null)
            {
                Logger.Debug("MainWindow", "Auto update check: already on latest version");
                return;
            }

            // Check if user previously skipped this version
            if (_skippedVersion != null && _skippedVersion == updateInfo.VersionDisplay)
            {
                Logger.Debug("MainWindow", $"Auto update check: user skipped {_skippedVersion}, not prompting");
                return;
            }

            Logger.Info("MainWindow", $"Auto update check: new version available ({updateInfo.VersionDisplay})");
            _analytics.TrackFeature("update.auto_found");

            // Show update in the Updates tab instead of popup dialog
            await ShowUpdateInUpdatesSectionAsync(updateInfo);
        }
        catch (Exception ex)
        {
            Logger.Debug("MainWindow", $"Auto update check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows update available UI in the Updates section (non-intrusive, no popup).
    /// Used by auto-check to show update without interrupting user.
    /// </summary>
    private async System.Threading.Tasks.Task ShowUpdateInUpdatesSectionAsync(UpdateInfo updateInfo)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                // Navigate to Updates section
                ShowUpdates();

                // Pass the update info to the Updates section view
                if (UpdatesPanel is Views.UpdatesSectionView updatesSection)
                {
                    updatesSection.ShowUpdateAvailableFromAutoCheck(updateInfo);
                    Logger.Info("MainWindow", "Update available shown in Updates section");
                }
                else
                {
                    // Fallback to dialog if Updates section not available
                    Logger.Warning("MainWindow", "Updates section not available, falling back to dialog");
                    _ = ShowUpdateAvailableDialogAsync(updateInfo);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", "Failed to show update in section", ex);
            }
        });
    }

    /// <summary>
    /// Shows the update-available dialog with stacked changelogs.
    /// Can be called from manual check (kept for backward compatibility).
    /// </summary>
    internal async System.Threading.Tasks.Task ShowUpdateAvailableDialogAsync(UpdateInfo updateInfo)
    {
        if (_isShowingUpdateDialog) return;
        _isShowingUpdateDialog = true;

        try
        {
            if (_updateService == null) return;

            // Fetch all changelogs between current and latest version
            var changelogs = await _updateService.GetChangelogBetweenVersionsAsync(
                updateInfo.CurrentVersion,
                updateInfo.LatestVersion);

            // Show the dialog on the UI thread
            await Dispatcher.InvokeAsync(() =>
            {
                var dialog = new UpdateAvailableWindow(updateInfo, _updateService, changelogs);
                dialog.Owner = IsVisible ? this : null;
                dialog.ShowDialog();

                if (dialog.SkipThisVersion)
                {
                    _skippedVersion = updateInfo.VersionDisplay;
                    Logger.Info("MainWindow", $"User chose to skip version {_skippedVersion}");
                    _analytics.TrackFeature("update.skipped_version");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to show update dialog", ex);
        }
        finally
        {
            _isShowingUpdateDialog = false;
        }
    }

    private void StopAutoUpdateCheck()
    {
        _updateCheckTimer?.Stop();
        _updateCheckTimer = null;
        _startupUpdateCheckTimer?.Stop();
        _startupUpdateCheckTimer = null;
        _updateTimerStarted = false;
    }

    private async void MainCheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Delegate to the embedded UpdatesSectionView for integrated UI experience
            if (UpdatesPanel is Views.UpdatesSectionView updatesSection)
            {
                await updatesSection.StartUpdateCheckFromExternalAsync();
            }
            else
            {
                // Fallback if UpdatesSection is not initialized
                EnsureUpdateService();
                if (_updateService == null)
                {
                    NotificationService.Instance.ShowError("Updates", "Update service is not available.");
                    return;
                }

                _analytics.TrackFeature("update.manual_check");
                var updateInfo = await _updateService.CheckForUpdateAsync();
                if (updateInfo == null)
                {
                    NotificationService.Instance.ShowInfo("Updates", "You are already on the latest version.");
                    return;
                }

                await ShowUpdateAvailableDialogAsync(updateInfo);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Manual update check failed", ex);
            NotificationService.Instance.ShowError("Updates", "Failed to check for updates.");
        }
    }
}
