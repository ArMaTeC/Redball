using System;
using System.Windows;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Window to show update download progress with speed and ETA.
/// </summary>
public partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow()
    {
        InitializeComponent();
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

        ProgressBar.Value = progress.Percentage;
        StatusLabel.Text = progress.StatusText ?? "Downloading update...";
        
        double downloadedMb = progress.BytesReceived / 1024.0 / 1024.0;
        double totalMb = progress.TotalBytes / 1024.0 / 1024.0;
        SizeLabel.Text = $"{downloadedMb:F1} MB / {totalMb:F1} MB";

        if (progress.BytesPerSecond > 0)
        {
            double speedMb = progress.BytesPerSecond / 1024.0 / 1024.0;
            if (speedMb >= 0.1)
                SpeedLabel.Text = $"{speedMb:F1} MB/s";
            else
                SpeedLabel.Text = $"{(progress.BytesPerSecond / 1024.0):F1} KB/s";

            var remainingBytes = progress.TotalBytes - progress.BytesReceived;
            var secondsRemaining = remainingBytes / progress.BytesPerSecond;
            
            if (secondsRemaining > 0)
            {
                var time = TimeSpan.FromSeconds(secondsRemaining);
                EtaLabel.Text = time.TotalMinutes >= 1
                    ? $"Estimated time remaining: {(int)time.TotalMinutes}m {time.Seconds}s"
                    : $"Estimated time remaining: {time.Seconds}s";
            }
        }
        else
        {
            SpeedLabel.Text = "0 KB/s";
            EtaLabel.Text = "Calculating time...";
        }
    }
}
