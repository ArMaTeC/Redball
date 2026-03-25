// Copyright (c) ArMaTeC. All rights reserved.
// Licensed under the MIT License.

using System.Windows;

namespace Redball.UI.Views;

public partial class SecretEditorDialog : Window
{
    public string? SecretKey { get; set; }
    public string? SecretValue => ValuePasswordBox.Password;
    public bool IsEditMode { get; set; }

    public SecretEditorDialog()
    {
        InitializeComponent();
        Loaded += SecretEditorDialog_Loaded;
    }

    private void SecretEditorDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (IsEditMode && !string.IsNullOrEmpty(SecretKey))
        {
            KeyTextBox.Text = SecretKey;
            KeyTextBox.IsReadOnly = true;
            Title = "Edit Secret";
            ValuePasswordBox.Focus();
        }
        else
        {
            Title = "Add New Secret";
            KeyTextBox.Focus();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SecretKey = KeyTextBox.Text.Trim();
        if (string.IsNullOrEmpty(SecretKey))
        {
            MessageBox.Show("Secret key is required.", "Validation", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(ValuePasswordBox.Password))
        {
            MessageBox.Show("Secret value is required.", "Validation", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
