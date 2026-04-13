using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Redball.UI.Interop;

namespace Redball.UI.Views;

public partial class HUDWindow : Window
{
    private static HUDWindow? _currentInstance;
    private static readonly DispatcherTimer CloseTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    public HUDWindow(string title, string value, string icon)
    {
        InitializeComponent();
        TitleText.Text = title.ToUpper();
        ValueText.Text = value;
        IconText.Text = icon;

        Loaded += HUDWindow_Loaded;
        CloseTimer.Tick += CloseTimer_Tick;
    }

    public static void ShowStatus(string title, string value, string icon)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_currentInstance != null)
            {
                _currentInstance.TitleText.Text = title.ToUpper();
                _currentInstance.ValueText.Text = value;
                _currentInstance.IconText.Text = icon;
                CloseTimer.Stop();
                CloseTimer.Start();
                return;
            }

            _currentInstance = new HUDWindow(title, value, icon);
            _currentInstance.Show();
            CloseTimer.Start();
        });
    }

    private void HUDWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Position at top center of screen
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = (screenWidth - Width) / 2;
        Top = 60; // Slightly below the top edge

        // Apply Mica blur
        var hwnd = new WindowInteropHelper(this).Handle;
        var micaValue = (int)NativeMethods.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW;
        var darkMode = 1; // HUD is always dark for contrast
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref micaValue, sizeof(int));

        // Make click-through (WS_EX_TRANSPARENT | WS_EX_LAYERED)
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED));

        // Run entrance animation
        if (Resources["EntranceAnimation"] is Storyboard sb)
        {
            sb.Begin();
        }
    }

    private void CloseTimer_Tick(object? sender, EventArgs e)
    {
        CloseTimer.Stop();
        if (Resources["ExitAnimation"] is Storyboard sb)
        {
            sb.Completed += (_, _) => { Close(); _currentInstance = null; };
            sb.Begin();
        }
        else
        {
            Close();
            _currentInstance = null;
        }
    }
}
