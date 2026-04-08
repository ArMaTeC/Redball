using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Redball.UI.Services;
using System.Windows.Resources;

namespace Redball.UI.Views;

/// <summary>
/// Progress window shown during update check operations.
/// </summary>
public partial class UpdateCheckProgressWindow : Window
{
    private readonly Ellipse[] _stageIndicators;
    private UpdateCheckStage _currentStage = UpdateCheckStage.Connecting;

    public UpdateCheckProgressWindow()
    {
        InitializeComponent();
        _stageIndicators = new[] { Stage1, Stage2, Stage3, Stage4, Stage5 };
        UpdateStageDisplay(UpdateCheckStage.Connecting);
    }

    /// <summary>
    /// Updates the progress display based on the current check progress.
    /// </summary>
    public void UpdateProgress(UpdateCheckProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateProgress(progress));
            return;
        }

        // Update progress bar
        if (progress.Percentage > 0)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = progress.Percentage;
        }
        else
        {
            ProgressBar.IsIndeterminate = true;
        }

        // Update status text
        if (!string.IsNullOrEmpty(progress.StatusText))
        {
            StatusText.Text = progress.StatusText;
        }

        // Show file counter during hashing stage
        if (progress.Stage == UpdateCheckStage.HashingFiles && progress.TotalFilesToHash > 0)
        {
            CurrentFileText.Visibility = Visibility.Visible;
            CurrentFileText.Text = $"File {progress.FilesHashed} of {progress.TotalFilesToHash}";
        }
        else
        {
            CurrentFileText.Visibility = Visibility.Collapsed;
        }

        // Update stage display
        if (progress.Stage != _currentStage)
        {
            _currentStage = progress.Stage;
            UpdateStageDisplay(progress.Stage);
        }
    }

    private void UpdateStageDisplay(UpdateCheckStage stage)
    {
        // Get colors from theme resources
        var activeBrush = TryFindResource("AccentBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0xDC, 0x35, 0x45));
        var completedBrush = TryFindResource("SuccessBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45));
        var pendingBrush = TryFindResource("ForegroundDisabledBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

        int activeIndex = stage switch
        {
            UpdateCheckStage.Connecting => 0,
            UpdateCheckStage.FetchingReleases => 1,
            UpdateCheckStage.ParsingManifest => 2,
            UpdateCheckStage.ComparingVersions or UpdateCheckStage.HashingFiles => 3,
            UpdateCheckStage.CalculatingDiff or UpdateCheckStage.Complete => 4,
            _ => 0
        };

        for (int i = 0; i < _stageIndicators.Length; i++)
        {
            if (i < activeIndex)
                _stageIndicators[i].Fill = completedBrush;
            else if (i == activeIndex)
                _stageIndicators[i].Fill = activeBrush;
            else
                _stageIndicators[i].Fill = pendingBrush;
        }

        StageLabel.Text = stage switch
        {
            UpdateCheckStage.Connecting => "Connecting...",
            UpdateCheckStage.FetchingReleases => "Fetching releases...",
            UpdateCheckStage.ParsingManifest => "Reading manifest...",
            UpdateCheckStage.ComparingVersions => "Comparing versions...",
            UpdateCheckStage.HashingFiles => "Checking files...",
            UpdateCheckStage.CalculatingDiff => "Calculating changes...",
            UpdateCheckStage.Complete => "Complete",
            UpdateCheckStage.Failed => "Failed",
            _ => "Working..."
        };
    }
}
