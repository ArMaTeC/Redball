namespace Redball.UI.WPF.Views;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Redball.UI.Services;
using Redball.UI.WPF.Models;
using Redball.UI.WPF.Services;

/// <summary>
/// Command palette window for quick access to actions and settings (Ctrl+K).
/// Implements UX Progressive Disclosure from improve_me.txt.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private readonly CommandPaletteIndex _index;

    public CommandPaletteWindow()
    {
        InitializeComponent();
        _index = CommandPaletteIndex.Instance;

        // Load initial results
        UpdateResults();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateResults();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteSelected();
                e.Handled = true;
                break;
        }
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateResults();
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelected();
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Ensure selected item is visible
        if (ResultsList.SelectedItem != null)
        {
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateResults()
    {
        var query = SearchBox.Text ?? string.Empty;
        var category = (CategoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();

        var results = _index.Search(query);

        // Apply category filter if not "All"
        if (!string.IsNullOrEmpty(category) && category != "All")
        {
            results = results.Where(r => r.Category == category).ToList();
        }

        ResultsList.ItemsSource = results;

        // Auto-select first item
        if (results.Any())
        {
            ResultsList.SelectedIndex = 0;
        }
    }

    private void MoveSelection(int direction)
    {
        var count = ResultsList.Items.Count;
        if (count == 0) return;

        var newIndex = ResultsList.SelectedIndex + direction;

        if (newIndex < 0)
            newIndex = count - 1;
        else if (newIndex >= count)
            newIndex = 0;

        ResultsList.SelectedIndex = newIndex;
    }

    private void ExecuteSelected()
    {
        if (ResultsList.SelectedItem is not PaletteCommand command)
            return;

        // Check if can execute
        if (command.CanExecute?.Invoke() == false)
        {
            MessageBox.Show("This command is currently unavailable.", "Command Unavailable",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Close palette
        Close();

        // Execute action
        if (command.Execute != null)
        {
            try
            {
                command.Execute();
            }
            catch (Exception ex)
            {
                Logger.Error("CommandPalette", $"Command '{command.Id}' failed", ex);
                MessageBox.Show($"Command failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else if (!string.IsNullOrEmpty(command.NavigateTo))
        {
            // Navigate to page
            NavigateToPage(command.NavigateTo);
        }
    }

    private static void NavigateToPage(string pageName)
    {
        // This would be called on the main window
        // For now, just log it - actual implementation would depend on MainWindow navigation
        Logger.Info("CommandPalette", $"Navigate to: {pageName}");

        // In a real implementation:
        // Application.Current.MainWindow would be MainWindow
        // which has navigation methods
        if (Application.Current.MainWindow is MainWindow mainWindow)
        {
            mainWindow.NavigateToSection(pageName);
        }
    }
}
