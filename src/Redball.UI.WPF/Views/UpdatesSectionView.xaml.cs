using System;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Updates section with embedded progress UI for check and download operations.
/// </summary>
public partial class UpdatesSectionView : UserControl
{
    private UpdateService? _updateService;
    private UpdateInfo? _pendingUpdateInfo;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly StringBuilder _logBuilder = new();

    // Stage tracking for download progress
    private UpdateStage _currentDownloadStage = UpdateStage.Checking;

    public UpdatesSectionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        EnsureUpdateService();
    }

    private void EnsureUpdateService()
    {
        if (_updateService != null) return;

        var config = ConfigService.Instance.Config;
        _updateService = new UpdateService(
            config.UpdateRepoOwner,
            config.UpdateRepoName,
            config.UpdateChannel ?? "stable",
            config.VerifyUpdateSignature,
            "https://redball.certrunnerx.com/");
    }

    private void LoadSettings()
    {
        var config = ConfigService.Instance.Config;

        UpdateChannelCombo.SelectedIndex = config.UpdateChannel?.ToLowerInvariant() switch
        {
            "beta" => 1,
            "disabled" => 2,
            _ => 0
        };

        VerifyUpdateSignatureCheck.IsChecked = config.VerifyUpdateSignature;
        AutoUpdateCheckEnabledCheck.IsChecked = config.AutoUpdateCheckEnabled;
        AutoUpdateIntervalSlider.Value = Math.Max(30, config.AutoUpdateCheckIntervalMinutes);
        UpdateAutoUpdateIntervalText((int)AutoUpdateIntervalSlider.Value);
        CurrentVersionText.Text = $"Current version: {GetCurrentVersionText()}";
    }

    private static string GetCurrentVersionText()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "Unknown";
    }

    private void UpdateAutoUpdateIntervalText(int minutes)
    {
        var hours = minutes / 60.0;
        AutoUpdateIntervalText.Text = hours >= 1
            ? $"Check every: {hours:F1} hours"
            : $"Check every: {minutes} minutes";
    }

    #region Event Handlers

    private void UpdateChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var config = ConfigService.Instance.Config;
        config.UpdateChannel = UpdateChannelCombo.SelectedIndex switch
        {
            1 => "beta",
            2 => "disabled",
            _ => "stable"
        };
        ConfigService.Instance.Save();

        // Recreate update service with new channel
        _updateService = null;
        EnsureUpdateService();
    }

    private void VerifyUpdateSignatureCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var config = ConfigService.Instance.Config;
        config.VerifyUpdateSignature = VerifyUpdateSignatureCheck.IsChecked == true;
        ConfigService.Instance.Save();
    }

    private void AutoUpdateCheckEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var config = ConfigService.Instance.Config;
        config.AutoUpdateCheckEnabled = AutoUpdateCheckEnabledCheck.IsChecked == true;
        ConfigService.Instance.Save();
    }

    private void AutoUpdateIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        var minutes = (int)AutoUpdateIntervalSlider.Value;
        UpdateAutoUpdateIntervalText(minutes);
        var config = ConfigService.Instance.Config;
        config.AutoUpdateCheckIntervalMinutes = minutes;
        ConfigService.Instance.Save();
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await StartUpdateCheckAsync();
    }

    private void CancelUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        ShowSettingsPanel();
    }

    private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdateInfo != null)
        {
            await StartDownloadAsync(_pendingUpdateInfo);
        }
    }

    private void DismissUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingUpdateInfo = null;
        ShowSettingsPanel();
    }

    private void BackToSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingUpdateInfo = null;
        ShowSettingsPanel();
    }

    #endregion

    #region Update Check

    private async Task StartUpdateCheckAsync()
    {
        EnsureUpdateService();
        if (_updateService == null) return;

        _cancellationTokenSource = new CancellationTokenSource();
        _logBuilder.Clear();

        ShowProgressPanel();
        ProgressTitleText.Text = "Checking for Updates";
        ResetStageIndicators();
        HighlightCheckStage(UpdateCheckStage.Connecting);

        var progress = new Progress<UpdateCheckProgress>(p =>
        {
            Dispatcher.Invoke(() => UpdateCheckProgressUI(p));
        });

        try
        {
            var updateInfo = await _updateService.CheckForUpdateAsync(progress, _cancellationTokenSource.Token);

            if (_cancellationTokenSource.IsCancellationRequested)
            {
                ShowSettingsPanel();
                return;
            }

            if (updateInfo == null)
            {
                ShowUpToDateResult();
            }
            else
            {
                _pendingUpdateInfo = updateInfo;
                ShowUpdateAvailableResult(updateInfo);
            }
        }
        catch (OperationCanceledException)
        {
            ShowSettingsPanel();
        }
        catch (Exception ex)
        {
            ShowErrorResult($"Update check failed: {ex.Message}");
        }
    }

    private void UpdateCheckProgressUI(UpdateCheckProgress progress)
    {
        ProgressBar.IsIndeterminate = progress.Percentage <= 0;
        ProgressBar.Value = progress.Percentage;
        ProgressPercentText.Text = progress.Percentage > 0 ? $"{progress.Percentage}%" : "";
        ProgressStatusText.Text = progress.StatusText ?? "Checking...";

        if (progress.Stage == UpdateCheckStage.HashingFiles && progress.TotalFilesToHash > 0)
        {
            ProgressFileCountText.Text = $"File {progress.FilesHashed} of {progress.TotalFilesToHash}";
        }
        else
        {
            ProgressFileCountText.Text = "";
        }

        HighlightCheckStage(progress.Stage);
    }

    private void HighlightCheckStage(UpdateCheckStage stage)
    {
        var accentBrush = (Brush)FindResource("AccentBrush");
        var completedBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
        var dimBg = new SolidColorBrush(Color.FromArgb(0x33, 0x55, 0x55, 0x55));
        var dimFg = new SolidColorBrush(Color.FromArgb(0x77, 0xAA, 0xAA, 0xAA));
        var whiteBrush = Brushes.White;

        // Map check stages to download stages for UI consistency
        // Stage 1 (Checking) = Connecting + Fetching
        // Stage 2 (Downloading) = Parsing + Comparing
        // Stage 3+ are not used during check

        var stage1Active = stage is UpdateCheckStage.Connecting or UpdateCheckStage.FetchingReleases;
        var stage1Done = stage > UpdateCheckStage.FetchingReleases;

        var stage2Active = stage is UpdateCheckStage.ParsingManifest or UpdateCheckStage.ComparingVersions or UpdateCheckStage.HashingFiles or UpdateCheckStage.CalculatingDiff;
        var stage2Done = stage == UpdateCheckStage.Complete;

        // Stage 1 - Check
        StageChecking.Background = stage1Active ? accentBrush : (stage1Done ? completedBrush : dimBg);
        ((TextBlock)StageChecking.Child).Foreground = stage1Active || stage1Done ? whiteBrush : dimFg;
        Connector1.Background = stage1Done ? completedBrush : dimBg;

        // Stage 2 - Download (used for comparison/hashing phase)
        StageDownloading.Background = stage2Active ? accentBrush : (stage2Done ? completedBrush : dimBg);
        ((TextBlock)StageDownloading.Child).Foreground = stage2Active || stage2Done ? whiteBrush : dimFg;
        Connector2.Background = stage2Done ? completedBrush : dimBg;

        // Reset others
        StagePatching.Background = dimBg;
        ((TextBlock)StagePatching.Child).Foreground = dimFg;
        Connector3.Background = dimBg;

        StageVerifying.Background = dimBg;
        ((TextBlock)StageVerifying.Child).Foreground = dimFg;
        Connector4.Background = dimBg;

        StageApplying.Background = dimBg;
        ((TextBlock)StageApplying.Child).Foreground = dimFg;
    }

    #endregion

    #region Download/Install

    private async Task StartDownloadAsync(UpdateInfo updateInfo)
    {
        if (_updateService == null) return;

        _cancellationTokenSource = new CancellationTokenSource();
        _logBuilder.Clear();

        ShowProgressPanel();
        ProgressTitleText.Text = "Downloading Update";
        ResetStageIndicators();

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            Dispatcher.Invoke(() => UpdateDownloadProgressUI(p));
        });

        try
        {
            var success = await _updateService.DownloadAndInstallAsync(updateInfo, progress, _cancellationTokenSource.Token);

            if (_cancellationTokenSource.IsCancellationRequested)
            {
                ShowSettingsPanel();
                return;
            }

            if (success)
            {
                ShowInstallCompleteResult();
            }
            else
            {
                ShowErrorResult("Update failed. Check the log for details.");
            }
        }
        catch (OperationCanceledException)
        {
            ShowSettingsPanel();
        }
        catch (Exception ex)
        {
            ShowErrorResult($"Update failed: {ex.Message}");
        }
    }

    private void UpdateDownloadProgressUI(UpdateDownloadProgress progress)
    {
        ProgressBar.Value = progress.Percentage;
        ProgressPercentText.Text = progress.Percentage > 0 ? $"{progress.Percentage}%" : "";
        ProgressStatusText.Text = progress.StatusText ?? "Updating...";

        // Speed / Size / ETA during download
        if (progress.Stage == UpdateStage.Downloading && progress.TotalBytes > 0)
        {
            double downloadedMb = progress.BytesReceived / 1024.0 / 1024.0;
            double totalMb = progress.TotalBytes / 1024.0 / 1024.0;
            ProgressSizeText.Text = $"{downloadedMb:F1} MB / {totalMb:F1} MB";

            if (progress.BytesPerSecond > 0)
            {
                double speedMb = progress.BytesPerSecond / 1024.0 / 1024.0;
                ProgressSpeedText.Text = speedMb >= 0.1
                    ? $"{speedMb:F1} MB/s"
                    : $"{(progress.BytesPerSecond / 1024.0):F1} KB/s";

                var remainingBytes = progress.TotalBytes - progress.BytesReceived;
                var secondsRemaining = remainingBytes / progress.BytesPerSecond;
                if (secondsRemaining > 0)
                {
                    var time = TimeSpan.FromSeconds(secondsRemaining);
                    ProgressEtaText.Text = time.TotalMinutes >= 1
                        ? $"ETA: {(int)time.TotalMinutes}m {time.Seconds}s"
                        : $"ETA: {time.Seconds}s";
                }
            }
            else
            {
                ProgressSpeedText.Text = "";
                ProgressEtaText.Text = "Calculating...";
            }
        }
        else
        {
            ProgressSizeText.Text = "";
            ProgressSpeedText.Text = "";
            ProgressEtaText.Text = "";
        }

        // File count
        if (progress.TotalFiles > 0)
        {
            ProgressFileCountText.Text = $"File {progress.CurrentFile} / {progress.TotalFiles}";
        }
        else
        {
            ProgressFileCountText.Text = progress.IsDelta ? "Differential update" : "";
        }

        // Log
        if (!string.IsNullOrEmpty(progress.LogEntry))
        {
            if (_logBuilder.Length > 0) _logBuilder.AppendLine();
            _logBuilder.Append(progress.LogEntry);
            LogTextBlock.Text = _logBuilder.ToString();
            LogScrollViewer.ScrollToEnd();
        }

        // Stage highlighting
        if (progress.Stage != _currentDownloadStage)
        {
            _currentDownloadStage = progress.Stage;
            HighlightDownloadStage(progress.Stage);
        }
    }

    private void HighlightDownloadStage(UpdateStage stage)
    {
        var accentBrush = (Brush)FindResource("AccentBrush");
        var completedBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
        var dimBg = new SolidColorBrush(Color.FromArgb(0x33, 0x55, 0x55, 0x55));
        var dimFg = new SolidColorBrush(Color.FromArgb(0x77, 0xAA, 0xAA, 0xAA));
        var whiteBrush = Brushes.White;
        var failedBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));

        bool reachedCurrent = false;

        // Stage: Checking
        UpdateSingleStage(StageChecking, Connector1, UpdateStage.Checking, stage,
            accentBrush, completedBrush, dimBg, whiteBrush, dimFg, ref reachedCurrent, failedBrush);

        // Stage: Downloading
        UpdateSingleStage(StageDownloading, Connector2, UpdateStage.Downloading, stage,
            accentBrush, completedBrush, dimBg, whiteBrush, dimFg, ref reachedCurrent, failedBrush);

        // Stage: Patching
        UpdateSingleStage(StagePatching, Connector3, UpdateStage.Patching, stage,
            accentBrush, completedBrush, dimBg, whiteBrush, dimFg, ref reachedCurrent, failedBrush);

        // Stage: Verifying
        UpdateSingleStage(StageVerifying, Connector4, UpdateStage.Verifying, stage,
            accentBrush, completedBrush, dimBg, whiteBrush, dimFg, ref reachedCurrent, failedBrush);

        // Stage: Applying
        if (stage == UpdateStage.Applying || stage == UpdateStage.Staging)
        {
            StageApplying.Background = accentBrush;
            ((TextBlock)StageApplying.Child).Foreground = whiteBrush;
        }
        else if (stage > UpdateStage.Applying || stage == UpdateStage.Complete)
        {
            StageApplying.Background = completedBrush;
            ((TextBlock)StageApplying.Child).Foreground = whiteBrush;
        }
        else
        {
            StageApplying.Background = dimBg;
            ((TextBlock)StageApplying.Child).Foreground = dimFg;
        }

        if (stage == UpdateStage.Complete)
        {
            Connector1.Background = completedBrush;
            Connector2.Background = completedBrush;
            Connector3.Background = completedBrush;
            Connector4.Background = completedBrush;
        }
        else if (stage == UpdateStage.Failed)
        {
            ProgressTitleText.Text = "Update Failed";
            ProgressPercentText.Text = "";
        }
    }

    private void UpdateSingleStage(Border indicator, Border? connector, UpdateStage stage, UpdateStage current,
        Brush accentBrush, Brush completedBrush, Brush dimBg, Brush completedFg, Brush dimFg,
        ref bool reachedCurrent, Brush failedBrush)
    {
        var icon = (TextBlock)indicator.Child;
        if (stage == current)
        {
            reachedCurrent = true;
            indicator.Background = current == UpdateStage.Failed ? failedBrush : accentBrush;
            icon.Foreground = Brushes.White;
            if (connector != null) connector.Background = dimBg;
        }
        else if (!reachedCurrent)
        {
            indicator.Background = completedBrush;
            icon.Foreground = completedFg;
            if (connector != null) connector.Background = accentBrush;
        }
        else
        {
            indicator.Background = dimBg;
            icon.Foreground = dimFg;
            if (connector != null) connector.Background = dimBg;
        }
    }

    #endregion

    #region UI State Management

    private void ShowSettingsPanel()
    {
        SettingsPanel.Visibility = Visibility.Visible;
        ProgressPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowProgressPanel()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowResultPanel()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
    }

    private void ShowUpToDateResult()
    {
        ResultIcon.Text = "\uE73E"; // Checkmark
        ResultIcon.Foreground = (Brush)FindResource("SuccessBrush");
        ResultTitleText.Text = "Up to Date";
        ResultMessageText.Text = "You're running the latest version of Redball.";
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        ShowResultPanel();
    }

    private void ShowUpdateAvailableResult(UpdateInfo info)
    {
        ResultIcon.Text = "\uE896"; // Download icon
        ResultIcon.Foreground = (Brush)FindResource("AccentBrush");
        ResultTitleText.Text = "Update Available";
        ResultMessageText.Text = $"A new version of Redball is ready to download and install.";

        // Version comparison display
        CurrentVersionCompareText.Text = GetCurrentVersionText();
        NewVersionCompareText.Text = info.VersionDisplay;

        // Update details
        var totalBytes = info.TotalDownloadBytes;
        if (totalBytes > 0)
        {
            var sizeText = totalBytes >= 1024 * 1024 * 1024
                ? $"{totalBytes / 1024.0 / 1024.0 / 1024.0:F2} GB"
                : totalBytes >= 1024 * 1024
                    ? $"{totalBytes / 1024.0 / 1024.0:F1} MB"
                    : $"{totalBytes / 1024.0:F0} KB";
            UpdateSizeText.Text = $"Download size: {sizeText}";
        }
        else
        {
            UpdateSizeText.Text = "Download size: Calculating...";
        }

        UpdateTypeText.Text = info.IsDeltaUpdate ? "Update type: Differential (faster)" : "Update type: Full Update";

        // Release notes with fallback
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes)
            ? "No release notes available for this version."
            : info.ReleaseNotes;

        UpdateAvailablePanel.Visibility = Visibility.Visible;
        ShowResultPanel();
    }

    private void ShowInstallCompleteResult()
    {
        ResultIcon.Text = "\uE73E"; // Checkmark
        ResultIcon.Foreground = (Brush)FindResource("SuccessBrush");
        ResultTitleText.Text = "Update Installing";
        ResultMessageText.Text = "The update is being installed. Redball will restart automatically.";
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        ShowResultPanel();
    }

    private void ShowErrorResult(string message)
    {
        ResultIcon.Text = "\uE783"; // Error icon
        ResultIcon.Foreground = (Brush)FindResource("ErrorBrush");
        ResultTitleText.Text = "Update Error";
        ResultMessageText.Text = message;
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        ShowResultPanel();
    }

    private void ResetStageIndicators()
    {
        var dimBg = new SolidColorBrush(Color.FromArgb(0x33, 0x55, 0x55, 0x55));
        var dimFg = new SolidColorBrush(Color.FromArgb(0x77, 0xAA, 0xAA, 0xAA));

        StageChecking.Background = dimBg;
        ((TextBlock)StageChecking.Child).Foreground = dimFg;
        Connector1.Background = dimBg;

        StageDownloading.Background = dimBg;
        ((TextBlock)StageDownloading.Child).Foreground = dimFg;
        Connector2.Background = dimBg;

        StagePatching.Background = dimBg;
        ((TextBlock)StagePatching.Child).Foreground = dimFg;
        Connector3.Background = dimBg;

        StageVerifying.Background = dimBg;
        ((TextBlock)StageVerifying.Child).Foreground = dimFg;
        Connector4.Background = dimBg;

        StageApplying.Background = dimBg;
        ((TextBlock)StageApplying.Child).Foreground = dimFg;

        ProgressBar.Value = 0;
        ProgressPercentText.Text = "";
        ProgressStatusText.Text = "Preparing...";
        ProgressSizeText.Text = "";
        ProgressSpeedText.Text = "";
        ProgressEtaText.Text = "";
        ProgressFileCountText.Text = "";
        LogTextBlock.Text = "";
        _logBuilder.Clear();
        _currentDownloadStage = UpdateStage.Checking;
    }

    #endregion

    /// <summary>
    /// Public method for MainWindow to trigger an update check programmatically.
    /// </summary>
    public async Task StartUpdateCheckFromExternalAsync()
    {
        await StartUpdateCheckAsync();
    }
}
