using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Redball.UI.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Redball.UI.Views;

public partial class SettingsWindow : Window
{
    private bool _isDirty;

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += SettingsWindow_Loaded;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadConfigIntoUI();
        SetVersionText();
        _isDirty = false;
    }

    private void SetVersionText()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = $"v{version?.Major}.{version?.Minor}.{version?.Build}";
        if (CurrentVersionText != null)
            CurrentVersionText.Text = $"Current Version: {versionString}";
        if (AboutVersionText != null)
            AboutVersionText.Text = $"Redball {versionString}";
    }

    private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        // Open GitHub releases page for now
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/ArMaTeC/Redball/releases",
            UseShellExecute = true
        });
    }

    private void LoadConfigIntoUI()
    {
        var cfg = ConfigService.Instance.Config;

        // Theme
        if (ThemeCombo != null)
        {
            ThemeCombo.SelectionChanged -= ThemeCombo_SelectionChanged;
            ThemeCombo.SelectedIndex = ThemeManager.CurrentTheme == Theme.Dark ? 1 : 2;
            ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;
        }

        // TypeThing hotkeys
        if (StartHotkeyBox != null) StartHotkeyBox.Text = cfg.TypeThingStartHotkey;
        if (StopHotkeyBox != null) StopHotkeyBox.Text = cfg.TypeThingStopHotkey;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && PromptUnsavedChanges() == MessageBoxResult.Cancel)
            return;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && PromptUnsavedChanges() == MessageBoxResult.Cancel)
            return;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveUIToConfig();
        var svc = ConfigService.Instance;
        var errors = svc.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "Validation Errors",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (svc.Save())
        {
            _isDirty = false;
            Close();
        }
        else
        {
            MessageBox.Show("Failed to save configuration.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveUIToConfig()
    {
        var cfg = ConfigService.Instance.Config;
        if (StartHotkeyBox != null) cfg.TypeThingStartHotkey = StartHotkeyBox.Text;
        if (StopHotkeyBox != null) cfg.TypeThingStopHotkey = StopHotkeyBox.Text;
    }

    private MessageBoxResult PromptUnsavedChanges()
    {
        return MessageBox.Show("You have unsaved changes. Close without saving?",
            "Unsaved Changes", MessageBoxButton.OKCancel, MessageBoxImage.Question);
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/ArMaTeC/Redball",
            UseShellExecute = true
        });
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo is null || ThemeCombo.SelectedIndex < 0) return;

        switch (ThemeCombo.SelectedIndex)
        {
            case 0: // System Default
                var isDark = ThemeManager.IsSystemDarkMode();
                ThemeManager.SetTheme(isDark ? Theme.Dark : Theme.Light);
                break;
            case 1: // Dark Mode
                ThemeManager.SetTheme(Theme.Dark);
                break;
            case 2: // Light Mode
                ThemeManager.SetTheme(Theme.Light);
                break;
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var textBox = sender as TextBox;
        if (textBox == null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore modifier-only presses
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
            return;

        var sb = new StringBuilder();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        sb.Append(key.ToString());

        textBox.Text = sb.ToString();
        _isDirty = true;
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Text = "Press a key combination...";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Text == "Press a key combination...")
            tb.Text = tb.Name == "StartHotkeyBox" ? "Ctrl+Shift+V" : "Ctrl+Shift+X";
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        // Hide all panels (null checks needed during XAML initialization)
        var panels = new[] { GeneralPanel, BehaviorPanel, FeaturesPanel, TypeThingPanel, UpdatesPanel, AboutPanel };
        foreach (var p in panels)
        {
            if (p != null) p.Visibility = Visibility.Collapsed;
        }

        // Determine which panel to show
        StackPanel? target = null;
        if (sender == GeneralTab) target = GeneralPanel;
        else if (sender == BehaviorTab) target = BehaviorPanel;
        else if (sender == FeaturesTab) target = FeaturesPanel;
        else if (sender == TypeThingTab) target = TypeThingPanel;
        else if (sender == UpdatesTab) target = UpdatesPanel;
        else if (sender == AboutTab) target = AboutPanel;

        // Show with fade-in animation
        if (target != null)
        {
            target.Opacity = 0;
            target.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            target.BeginAnimation(OpacityProperty, fadeIn);
        }
    }
}
