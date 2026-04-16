using System.Windows;
using System.Windows.Input;

namespace Redball.UI.Views;

/// <summary>
/// A modern, styled replacement for MessageBox that supports themes and icons.
/// </summary>
public partial class NotificationWindow : Window
{
    private bool _isCancelable;

    public NotificationWindow(string title, string message, string icon = "\uE946", bool isCancelable = false)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        IconText.Text = icon;
        _isCancelable = isCancelable;

        if (isCancelable)
        {
            CancelButton.Visibility = Visibility.Visible;
            OkButton.Content = "Continue";
        }

        // Draggable
        MouseDown += (s, e) => {
            if (e.ChangedButton == MouseButton.Left)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                Redball.UI.Interop.NativeMethods.BeginNativeDrag(hwnd);
            }
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Shows a styled notification window.
    /// </summary>
    public static bool Show(string title, string message, string icon = "\uE946", bool isCancelable = false)
    {
        var win = new NotificationWindow(title, message, icon, isCancelable);
        return win.ShowDialog() ?? false;
    }
}
