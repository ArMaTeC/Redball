namespace Redball.UI.WPF.Views;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Redball.Core.Performance;

/// <summary>
/// Dialog to display startup timing history.
/// </summary>
public partial class StartupHistoryDialog : Window
{
    public StartupHistoryDialog(List<StartupSnapshot> history)
    {
        InitializeComponent();
        DataContext = history;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
