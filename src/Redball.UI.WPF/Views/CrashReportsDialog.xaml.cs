namespace Redball.UI.WPF.Views;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Redball.Core.Telemetry;

/// <summary>
/// Dialog to display crash reports.
/// </summary>
public partial class CrashReportsDialog : Window
{
    public CrashReportsDialog(List<CrashEnvelope> crashes)
    {
        InitializeComponent();
        DataContext = crashes;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
