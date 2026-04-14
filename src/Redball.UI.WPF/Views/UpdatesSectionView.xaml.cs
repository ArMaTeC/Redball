using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
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
    private bool _isChecking;
    private readonly StringBuilder _logBuilder = new();
    private DateTime _lastLogUpdate = DateTime.MinValue;
    private bool _isInitialized;

    // Stage tracking for download progress
    private UpdateStage _currentDownloadStage = UpdateStage.Checking;

    public UpdatesSectionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureInitialized();
    }

    /// <summary>
    /// Ensures the view is initialized. Can be called externally when
    /// programmatically navigating to this tab before Loaded event fires.
    /// </summary>
    public void EnsureInitialized()
    {
        if (_isInitialized) return;

        LoadSettings();
        EnsureUpdateService();
        _isInitialized = true;
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
        config.UpdateServerUrl);
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
        if (_isChecking) return;
        _isChecking = true;

        try
        {
            EnsureUpdateService();
            if (_updateService == null) return;

            _cancellationTokenSource = new CancellationTokenSource();
            _logBuilder.Clear();
            _pendingUpdateInfo = null;

            ShowProgressPanel();
            ProgressTitleText.Text = "Checking for Updates";
            ResetStageIndicators();
            HighlightCheckStage(UpdateCheckStage.Connecting);

            var progress = new Progress<UpdateCheckProgress>(p =>
            {
                Dispatcher.Invoke(() => UpdateCheckProgressUI(p));
            });

            var updateInfo = await _updateService.CheckForUpdateAsync(true, progress, _cancellationTokenSource.Token);

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
            Logger.Error("UpdatesSectionView", "Manual update check failed", ex);
            ShowErrorResult($"Update check failed: {ex.Message}");
        }
        finally
        {
            _isChecking = false;
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

                // CRITICAL: Shut down the app after a short delay to allow the update script
                // (which is waiting for this process to exit) to begin the installation.
                await Task.Delay(3000);
                Application.Current.Shutdown();
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

        // Log throttling and size management
        if (!string.IsNullOrEmpty(progress.LogEntry))
        {
            var cleanedEntry = CleanAnsi(progress.LogEntry);
            if (!string.IsNullOrEmpty(cleanedEntry))
            {
                if (_logBuilder.Length > 0) _logBuilder.AppendLine();
                _logBuilder.Append(cleanedEntry);

                // Keep only the last 10,000 characters to prevent junk build-up
                if (_logBuilder.Length > 10000)
                {
                    _logBuilder.Remove(0, _logBuilder.Length - 5000);
                }

                // Throttle UI update - only update text if it's been at least 100ms
                // or if it's a critical stage change
                var now = DateTime.Now;
                if ((now - _lastLogUpdate).TotalMilliseconds > 100 || progress.Stage != _currentDownloadStage)
                {
                    LogTextBlock.Text = _logBuilder.ToString();
                    LogScrollViewer.ScrollToEnd();
                    _lastLogUpdate = now;
                }
            }
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

        // Stage: Applying (Staging is the final preparation before completion)
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
        FileVerificationPanel.Visibility = Visibility.Collapsed;
        VerifyFilesButton.Visibility = Visibility.Visible;
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

        // Clear All Progress Values
        ProgressBar.Value = 0;
        ProgressBar.IsIndeterminate = false;
        ProgressPercentText.Text = "";
        ProgressStatusText.Text = "Preparing...";
        ProgressSizeText.Text = "";
        ProgressSpeedText.Text = "";
        ProgressEtaText.Text = "";
        ProgressFileCountText.Text = "";
        LogTextBlock.Text = "";
        _logBuilder.Clear();
        _currentDownloadStage = UpdateStage.Checking;

        // Clear All Result Values (even though hidden, ensures no "stale" data flashes later)
        ResultTitleText.Text = "";
        ResultMessageText.Text = "";
        CurrentVersionCompareText.Text = "";
        NewVersionCompareText.Text = "";
        UpdateSizeText.Text = "";
        UpdateTypeText.Text = "";
        ReleaseNotesText.Text = "";
    }

    #endregion

    /// <summary>
    /// Public method for MainWindow to trigger an update check programmatically.
    /// </summary>
    public async Task StartUpdateCheckFromExternalAsync()
    {
        await StartUpdateCheckAsync();
    }

    /// <summary>
    /// Shows update available UI from auto-check without showing popup dialog.
    /// Called by MainWindow when auto-check detects an update.
    /// </summary>
    public void ShowUpdateAvailableFromAutoCheck(UpdateInfo updateInfo)
    {
        _pendingUpdateInfo = updateInfo;
        ShowUpdateAvailableResult(updateInfo);
        // Ensure the update available panel is visible
        UpdateAvailablePanel.Visibility = Visibility.Visible;
    }

    private static string CleanAnsi(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        // Basic ANSI stripping regex: \x1B\[[0-9;]*[a-zA-Z]
        return System.Text.RegularExpressions.Regex.Replace(input, @"\x1B\[[0-9;]*[a-zA-Z]", "");
    }

    #region File Verification & Repair

    private List<FileRepairInfo> _filesNeedingRepair = new();

    private async void VerifyFilesButton_Click(object sender, RoutedEventArgs e)
    {
        await StartFileVerificationAsync();
    }

    private async void RepairFilesButton_Click(object sender, RoutedEventArgs e)
    {
        await StartFileRepairAsync();
    }

    private void DismissRepairButton_Click(object sender, RoutedEventArgs e)
    {
        FileVerificationPanel.Visibility = Visibility.Collapsed;
        _filesNeedingRepair.Clear();
    }

    private async Task StartFileVerificationAsync()
    {
        if (_isChecking) return;
        _isChecking = true;
        _filesNeedingRepair.Clear();

        try
        {
            EnsureUpdateService();
            if (_updateService == null) return;

            _cancellationTokenSource = new CancellationTokenSource();

            ShowProgressPanel();
            ProgressTitleText.Text = "Verifying File Integrity";
            ResetStageIndicators();
            HighlightCheckStage(UpdateCheckStage.Connecting);

            // Get the current version
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null)
            {
                ShowErrorResult("Could not determine current version");
                return;
            }

            var currentNormalized = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
            ProgressStatusText.Text = $"Fetching manifest for v{currentNormalized}...";
            ProgressBar.Value = 10;

            // Fetch manifest for current version from update server
            var manifest = await _updateService.FetchManifestForVersionAsync(currentNormalized, _cancellationTokenSource.Token);

            if (manifest == null)
            {
                ShowErrorResult("Could not fetch file manifest from update server.");
                return;
            }

            ProgressStatusText.Text = $"Checking {manifest.Files.Count} files...";
            ProgressBar.Value = 20;

            // Compare local files against manifest
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var filesChecked = 0;
            var filesMissing = 0;
            var filesCorrupted = 0;

            foreach (var fileEntry in manifest.Files)
            {
                if (_cancellationTokenSource.IsCancellationRequested) break;

                var localPath = Path.Combine(appDir, NormalizePath(fileEntry.Name));
                var repairInfo = new FileRepairInfo
                {
                    Name = fileEntry.Name,
                    ExpectedHash = fileEntry.Hash,
                    ExpectedSize = fileEntry.Size,
                    DownloadUrl = fileEntry.DownloadUrl
                };

                if (!File.Exists(localPath))
                {
                    repairInfo.Status = "Missing";
                    _filesNeedingRepair.Add(repairInfo);
                    filesMissing++;
                }
                else
                {
                    var localHash = await CalculateHashAsync(localPath);
                    if (!localHash.Equals(fileEntry.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        repairInfo.Status = "Corrupted";
                        _filesNeedingRepair.Add(repairInfo);
                        filesCorrupted++;
                    }
                }

                filesChecked++;
                var percent = 20 + (filesChecked * 70 / manifest.Files.Count);
                ProgressBar.Value = percent;
                ProgressStatusText.Text = $"Verified {filesChecked}/{manifest.Files.Count} files...";
                ProgressFileCountText.Text = $"File {filesChecked} of {manifest.Files.Count}";
            }

            ProgressBar.Value = 100;
            ProgressStatusText.Text = $"Verification complete: {_filesNeedingRepair.Count} files need repair";

            await Task.Delay(500); // Brief pause to show completion

            if (_filesNeedingRepair.Count > 0)
            {
                ShowFileVerificationResult(filesMissing, filesCorrupted);
            }
            else
            {
                // All files valid - show success
                ResultIcon.Text = "\uE73E";
                ResultIcon.Foreground = (Brush)FindResource("SuccessBrush");
                ResultTitleText.Text = "All Files Valid";
                ResultMessageText.Text = $"All {filesChecked} files verified successfully. No repairs needed.";
                UpdateAvailablePanel.Visibility = Visibility.Collapsed;
                FileVerificationPanel.Visibility = Visibility.Collapsed;
                VerifyFilesButton.Visibility = Visibility.Collapsed;
                ShowResultPanel();
            }
        }
        catch (OperationCanceledException)
        {
            ShowSettingsPanel();
        }
        catch (Exception ex)
        {
            Logger.Error("UpdatesSectionView", "File verification failed", ex);
            ShowErrorResult($"Verification failed: {ex.Message}");
        }
        finally
        {
            _isChecking = false;
        }
    }

    private async Task StartFileRepairAsync()
    {
        if (_filesNeedingRepair.Count == 0) return;

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();

            ShowProgressPanel();
            ProgressTitleText.Text = "Repairing Files";
            ResetStageIndicators();
            HighlightCheckStage(UpdateCheckStage.FetchingReleases);

            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var repaired = 0;
            var failed = 0;

            foreach (var file in _filesNeedingRepair)
            {
                if (_cancellationTokenSource.IsCancellationRequested) break;

                try
                {
                    var localPath = Path.Combine(appDir, NormalizePath(file.Name));
                    var dir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    ProgressStatusText.Text = $"Downloading {file.Name}...";

                    if (!string.IsNullOrEmpty(file.DownloadUrl))
                    {
                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromMinutes(5);
                        var data = await client.GetByteArrayAsync(file.DownloadUrl, _cancellationTokenSource.Token);
                        await File.WriteAllBytesAsync(localPath, data, _cancellationTokenSource.Token);

                        // Verify the downloaded file
                        var newHash = await CalculateHashAsync(localPath);
                        if (newHash.Equals(file.ExpectedHash, StringComparison.OrdinalIgnoreCase))
                        {
                            repaired++;
                        }
                        else
                        {
                            failed++;
                            Logger.Error("UpdatesSectionView", $"Downloaded file hash mismatch: {file.Name}");
                        }
                    }
                    else
                    {
                        failed++;
                        Logger.Error("UpdatesSectionView", $"No download URL for: {file.Name}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Logger.Error("UpdatesSectionView", $"Failed to repair {file.Name}", ex);
                }

                var percent = (repaired + failed) * 100 / _filesNeedingRepair.Count;
                ProgressBar.Value = percent;
            }

            // Show result
            ResultIcon.Text = failed == 0 ? "\uE73E" : "\uE7BA";
            ResultIcon.Foreground = failed == 0 ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("WarningBrush");
            ResultTitleText.Text = failed == 0 ? "Repair Complete" : "Partially Repaired";
            ResultMessageText.Text = $"Repaired {repaired} of {_filesNeedingRepair.Count} files." +
                (failed > 0 ? $" {failed} files failed to repair." : "");
            UpdateAvailablePanel.Visibility = Visibility.Collapsed;
            FileVerificationPanel.Visibility = Visibility.Collapsed;
            VerifyFilesButton.Visibility = Visibility.Collapsed;
            ShowResultPanel();

            _filesNeedingRepair.Clear();
        }
        catch (Exception ex)
        {
            Logger.Error("UpdatesSectionView", "File repair failed", ex);
            ShowErrorResult($"Repair failed: {ex.Message}");
        }
    }

    private void ShowFileVerificationResult(int missing, int corrupted)
    {
        ResultIcon.Text = "\uE7BA"; // Warning icon
        ResultIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        ResultTitleText.Text = "Files Need Repair";
        ResultMessageText.Text = $"Found {missing} missing and {corrupted} corrupted files.";

        VerificationSummaryText.Text = $"{_filesNeedingRepair.Count} files need to be downloaded and repaired to restore Redball to a working state.";
        FilesToRepairList.ItemsSource = _filesNeedingRepair;

        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        FileVerificationPanel.Visibility = Visibility.Visible;
        VerifyFilesButton.Visibility = Visibility.Collapsed;
        ShowResultPanel();
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static async Task<string> CalculateHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private class FileRepairInfo
    {
        public string Name { get; set; } = "";
        public string ExpectedHash { get; set; } = "";
        public long ExpectedSize { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string Status { get; set; } = "";
    }

    #endregion
}
