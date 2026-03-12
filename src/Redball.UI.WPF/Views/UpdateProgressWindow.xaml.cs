using System.Windows;

namespace Redball.UI.Views;

/// <summary>
/// Window to show update download progress.
/// </summary>
public partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Updates the progress bar value (0-100).
    /// </summary>
    public void SetProgress(int percent)
    {
        if (Dispatcher.CheckAccess())
        {
            ProgressBar.Value = percent;
        }
        else
        {
            Dispatcher.Invoke(() => ProgressBar.Value = percent);
        }
    }
}
