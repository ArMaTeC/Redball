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
    private readonly EventHandler<bool> _activeStateChangedHandler;

    public MiniWidgetWindow()
    {
        InitializeComponent();
        RestorePosition();
        RefreshState();

        _activeStateChangedHandler = (_, _) => Dispatcher.BeginInvoke(RefreshState);
        KeepAwakeService.Instance.ActiveStateChanged += _activeStateChangedHandler;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += (_, _) => RefreshState();
        _refreshTimer.Start();
    }

    private void RestorePosition()
    {
        var config = ConfigService.Instance.Config;
        var left = config.MiniWidgetLeft;
        var top = config.MiniWidgetTop;

        if (left >= 0 && top >= 0 && IsPositionOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            // Default: bottom-right of primary screen with some padding
            SetDefaultPosition();
        }
    }

    private void SetDefaultPosition()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private static bool IsPositionOnScreen(double left, double top)
    {
        // Check if at least part of the widget overlaps the virtual screen
        // (covers all monitors). Use a margin so the widget is still grabbable.
        const double margin = 40;
        var virtualLeft = SystemParameters.VirtualScreenLeft - margin;
        var virtualTop = SystemParameters.VirtualScreenTop - margin;
        var virtualRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + margin;
        var virtualBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + margin;

        return left + 220 > virtualLeft && left < virtualRight &&
               top + 80 > virtualTop && top < virtualBottom;
    }

    private void SavePosition()
    {
        try
        {
            var config = ConfigService.Instance.Config;
            config.MiniWidgetLeft = Left;
            config.MiniWidgetTop = Top;
            ConfigService.Instance.Save();
        }
        catch (Exception ex)
        {
            Logger.Warning("MiniWidget", $"Failed to save position: {ex.Message}");
        }
    }

    /// <summary>
    /// Resets the widget to the default bottom-right position and saves.
    /// </summary>
    public void ResetPosition()
    {
        SetDefaultPosition();
        SavePosition();
        Activate();
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
        {
            DragMove();
            // Save position after user finishes dragging
            SavePosition();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        KeepAwakeService.Instance.ActiveStateChanged -= _activeStateChangedHandler;
        SavePosition();
        base.OnClosed(e);
    }
}
