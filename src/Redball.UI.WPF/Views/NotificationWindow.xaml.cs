using System.Windows;
using System.Windows.Input;

namespace Redball.UI.Views;

/// <summary>
/// A modern, styled notification window that replaces standard MessageBox.
/// </summary>
public partial class NotificationWindow : Window
{
    public NotificationWindow(string title, string message, string icon = "\uE946")
    {
        InitializeComponent();
        TitleLabel.Text = title;
        MessageLabel.Text = message;
        IconText.Text = icon;
        
        // Make it draggable
        MouseDown += (s, e) => {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Shows a styled notification window.
    /// </summary>
    public static bool Show(string title, string message, string icon = "\uE946")
    {
        var win = new NotificationWindow(title, message, icon);
        return win.ShowDialog() ?? false;
    }
}
