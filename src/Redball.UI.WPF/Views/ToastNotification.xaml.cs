using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Redball.UI.Views;

public partial class ToastNotification : UserControl
{
    private readonly DispatcherTimer _autoCloseTimer;

    public ToastNotification(string title, string message, ToastType type = ToastType.Info, int autoCloseSec = 5)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;

        IconText.Text = type switch
        {
            ToastType.Success => "\uE73E",  // Checkmark
            ToastType.Warning => "\uE7BA",  // Warning
            ToastType.Error => "\uEA39",    // Error badge
            _ => "\uE946"                   // Info
        };

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoCloseSec) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            CloseToast();
        };
        _autoCloseTimer.Start();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        CloseToast();
    }

    private void CloseToast()
    {
        var popup = Parent as Popup;
        if (popup != null)
        {
            popup.IsOpen = false;
        }
    }
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}
