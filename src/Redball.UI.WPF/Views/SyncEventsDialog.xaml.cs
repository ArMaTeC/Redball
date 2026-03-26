namespace Redball.UI.WPF.Views;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Redball.Core.Sync;

/// <summary>
/// Dialog to display sync events.
/// </summary>
public partial class SyncEventsDialog : Window
{
    public SyncEventsDialog(List<SyncEvent> events, string? title = null)
    {
        InitializeComponent();
        DataContext = events;
        if (!string.IsNullOrEmpty(title))
        {
            Title = title;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
