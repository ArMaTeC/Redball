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
    public SyncEventsDialog(List<SyncEvent> events)
    {
        InitializeComponent();
        DataContext = events;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
