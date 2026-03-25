using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// A sleek, centered overlay HUD for visual feedback on hotkeys and status changes.
/// </summary>
public partial class HUDWindow : Window
{
    private static HUDWindow? _instance;
    private static readonly object _lock = new();

    public HUDWindow()
    {
        InitializeComponent();
        this.Owner = null; // Centered relative to screen, not a window
        this.ShowInTaskbar = false;
        this.Topmost = true;
    }

    public static void ShowStatus(string title, string status, string icon = "🔔")
    {
        // Don't interrupt gamers
        if (GamingModeService.Instance.IsGaming) return;

        Application.Current?.Dispatcher?.Invoke(() =>
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new HUDWindow();
                }

                _instance.TitleText.Text = title.ToUpperInvariant();
                _instance.StatusText.Text = status;
                _instance.IconText.Text = icon;

                if (!_instance.IsVisible)
                {
                    _instance.Show();
                    _instance.Activate();
                }

                var sb = _instance.Resources["ShowHideStoryboard"] as Storyboard;
                if (sb != null)
                {
                    sb.Completed += (s, e) =>
                    {
                        // Hide the window after animation completes to avoid hit-test blocking
                        _instance.Visibility = Visibility.Collapsed;
                    };

                    _instance.Visibility = Visibility.Visible;
                    sb.Begin(_instance, true);
                }
            }
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        
        // Final position adjustments to ensure true center on multi-monitor
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        this.Left = (screenWidth - this.Width) / 2;
        this.Top = (screenHeight - this.Height) / 2;
    }
}
