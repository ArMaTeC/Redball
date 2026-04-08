using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Redball.UI.Services;

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

        // Update stage display
        if (progress.Stage != _currentStage)
        {
            _currentStage = progress.Stage;
            UpdateStageDisplay(progress.Stage);
        }
    }

    private void UpdateStageDisplay(UpdateCheckStage stage)
    {
        var activeColor = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
        var completedColor = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        var pendingColor = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

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
                _stageIndicators[i].Fill = completedColor;
            else if (i == activeIndex)
                _stageIndicators[i].Fill = activeColor;
            else
                _stageIndicators[i].Fill = pendingColor;
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
