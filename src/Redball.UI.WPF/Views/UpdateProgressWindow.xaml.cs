using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Window showing detailed update progress with a visual stage pipeline and scrollable log.
/// </summary>
public partial class UpdateProgressWindow : Window
{
    private UpdateStage _currentStage = UpdateStage.Checking;
    private readonly System.Text.StringBuilder _logBuilder = new();

    public UpdateProgressWindow()
    {
        InitializeComponent();
        HighlightStage(UpdateStage.Checking);
    }

    /// <summary>
    /// Allows the borderless window to be dragged.
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Redball.UI.Interop.NativeMethods.BeginNativeDrag(hwnd);
    }

    /// <summary>
    /// Updates the UI with detailed progress info.
    /// </summary>
    public void UpdateProgress(UpdateDownloadProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateProgress(progress));
            return;
        }

        // --- Stage pipeline ---
        if (progress.Stage != _currentStage)
        {
            _currentStage = progress.Stage;
            HighlightStage(_currentStage);
        }

        // --- Progress bar + percent label ---
        ProgressBar.Value = progress.Percentage;
        PercentLabel.Text = progress.Percentage > 0 ? $"{progress.Percentage}%" : "";

        // --- Status line ---
        StatusLabel.Text = progress.StatusText ?? "Updating...";

        // --- Speed / Size / ETA (only relevant during download) ---
        if (progress.Stage == UpdateStage.Downloading && progress.TotalBytes > 0)
        {
            double downloadedMb = progress.BytesReceived / 1024.0 / 1024.0;
            double totalMb = progress.TotalBytes / 1024.0 / 1024.0;
            SizeLabel.Text = $"{downloadedMb:F1} MB / {totalMb:F1} MB";

            if (progress.BytesPerSecond > 0)
            {
                double speedMb = progress.BytesPerSecond / 1024.0 / 1024.0;
                SpeedLabel.Text = speedMb >= 0.1
                    ? $"{speedMb:F1} MB/s"
                    : $"{(progress.BytesPerSecond / 1024.0):F1} KB/s";

                var remainingBytes = progress.TotalBytes - progress.BytesReceived;
                var secondsRemaining = remainingBytes / progress.BytesPerSecond;
                if (secondsRemaining > 0)
                {
                    var time = TimeSpan.FromSeconds(secondsRemaining);
                    EtaLabel.Text = time.TotalMinutes >= 1
                        ? $"ETA: {(int)time.TotalMinutes}m {time.Seconds}s"
                        : $"ETA: {time.Seconds}s";
                }
            }
            else
            {
                SpeedLabel.Text = "";
                EtaLabel.Text = "Calculating...";
            }
        }
        else
        {
            SizeLabel.Text = "";
            SpeedLabel.Text = "";
            EtaLabel.Text = "";
        }

        // --- File count label ---
        if (progress.TotalFiles > 0)
        {
            FileCountLabel.Text = $"File {progress.CurrentFile} / {progress.TotalFiles}";
        }
        else
        {
            FileCountLabel.Text = progress.IsDelta ? "Differential update" : "";
        }

        // --- Append to log with throttling and cleaning ---
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

                LogTextBlock.Text = _logBuilder.ToString();
                LogScrollViewer.ScrollToEnd();
            }
        }
    }

    private string CleanAnsi(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        // Basic regex to strip ANSI escape codes
        return System.Text.RegularExpressions.Regex.Replace(input, @"\x1B\[[^@-~]*[@-~]", "");
    }

    /// <summary>
    /// Visually highlights stages in the pipeline up to the current stage.
    /// Past stages are green, current stage glows with accent, future stages stay dim.
    /// </summary>
    private void HighlightStage(UpdateStage current)
    {
        var accentBrush = (Brush)FindResource("AccentBrush");
        var completedBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)); // green
        var completedFg = Brushes.White;
        var activeFg = Brushes.White;
        var dimBg = new SolidColorBrush(Color.FromArgb(0x33, 0x55, 0x55, 0x55));
        var dimFg = new SolidColorBrush(Color.FromArgb(0x77, 0xAA, 0xAA, 0xAA));
        var connectorActive = accentBrush;
        var connectorDim = dimBg;

        var failedBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));

        // Define the visual stage sequence
        var stages = new[]
        {
            (StageChecking, Connector1, UpdateStage.Checking),
            (StageDownloading, Connector2, UpdateStage.Downloading),
            (StagePatching, Connector3, UpdateStage.Patching),
            (StageVerifying, Connector4, UpdateStage.Verifying),
            (StageApplying, (Border?)null, UpdateStage.Applying)
        };

        // If current is Staging, it maps to Applying visually for the pipeline dots
        var effectiveCurrent = current;
        if (current == UpdateStage.Staging)
        {
            effectiveCurrent = UpdateStage.Applying;
        }

        bool reachedCurrent = false;

        foreach (var (indicator, connector, stageType) in stages)
        {
            var icon = (System.Windows.Controls.TextBlock)indicator.Child;

            if (stageType == effectiveCurrent)
            {
                reachedCurrent = true;
                indicator.Background = current == UpdateStage.Failed ? failedBrush : accentBrush;
                icon.Foreground = activeFg;
                if (connector != null)
                    connector.Background = connectorDim;
            }
            else if (!reachedCurrent && (int)stageType < (int)effectiveCurrent)
            {
                // Past / completed
                indicator.Background = completedBrush;
                icon.Foreground = completedFg;
                if (connector != null)
                    connector.Background = connectorActive;
            }
            else
            {
                // Future
                indicator.Background = dimBg;
                icon.Foreground = dimFg;
                if (connector != null)
                    connector.Background = connectorDim;
            }
        }

        // Handle terminal states
        if (current == UpdateStage.Complete)
        {
            foreach (var (indicator, connector, _) in stages)
            {
                SetStageCompleted(indicator, connector, completedBrush, completedFg);
            }
        }
        else if (current == UpdateStage.Failed)
        {
            TitleLabel.Text = "Update Failed";
            PercentLabel.Text = "";
        }
    }

    /// <summary>
    /// Sets a stage and its connector to the completed (green) state.
    /// </summary>
    private void SetStageCompleted(Border indicator, Border? connector, Brush completedBrush, Brush completedFg)
    {
        var icon = (System.Windows.Controls.TextBlock)indicator.Child;
        indicator.Background = completedBrush;
        icon.Foreground = completedFg;
        if (connector != null)
            connector.Background = completedBrush;
    }
}
