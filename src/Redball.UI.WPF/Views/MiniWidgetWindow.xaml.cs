using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Redball.UI.Services;

namespace Redball.UI.Views;

public partial class MiniWidgetWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;

    public MiniWidgetWindow()
    {
        InitializeComponent();
        RefreshState();

        KeepAwakeService.Instance.ActiveStateChanged += (_, _) =>
            Dispatcher.BeginInvoke(RefreshState);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += (_, _) => RefreshState();
        _refreshTimer.Start();
    }

    private void RefreshState()
    {
        var ka = KeepAwakeService.Instance;
        StatusText.Text = ka.GetStatusText();

        if (ka.IsActive)
        {
            StatusDot.Fill = ka.Until.HasValue
                ? new SolidColorBrush(Color.FromRgb(253, 126, 20))   // Orange for timed
                : new SolidColorBrush(Color.FromRgb(76, 175, 80));   // Green for active
            ToggleBtn.Content = "\uE769"; // Pause icon
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(108, 117, 125)); // Gray for paused
            ToggleBtn.Content = "\uE768"; // Play icon
        }
    }

    private void ToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        KeepAwakeService.Instance.Toggle();
        RefreshState();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        base.OnClosed(e);
    }
}
