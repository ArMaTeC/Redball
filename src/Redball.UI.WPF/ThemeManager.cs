using System;
using System.Windows;
using System.Windows.Media;

namespace Redball.UI;

/// <summary>
/// Manages application themes (Dark/Light) for Redball v3.0
/// Supports Fluent Design System with acrylic/glass effects
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _currentTheme;

    public static Theme CurrentTheme { get; private set; } = Theme.Dark;

    public static void Initialize()
    {
        // Detect system theme preference
        var isDarkMode = IsSystemDarkMode();
        SetTheme(isDarkMode ? Theme.Dark : Theme.Light);
    }

    public static void SetTheme(Theme theme)
    {
        CurrentTheme = theme;

        // Remove current theme if exists
        if (_currentTheme != null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(_currentTheme);
        }

        // Load new theme
        var themeUri = theme == Theme.Dark
            ? new Uri("pack://application:,,,/Themes/DarkTheme.xaml")
            : new Uri("pack://application:,,,/Themes/LightTheme.xaml");

        _currentTheme = new ResourceDictionary { Source = themeUri };
        Application.Current.Resources.MergedDictionaries.Add(_currentTheme);

        // Apply accent colors
        ApplyAccentColor();
    }

    public static void ToggleTheme()
    {
        SetTheme(CurrentTheme == Theme.Dark ? Theme.Light : Theme.Dark);
    }

    private static void ApplyAccentColor()
    {
        // Redball brand color: #DC3545 (Bootstrap danger red)
        var accentBrush = new SolidColorBrush(Color.FromRgb(220, 53, 69));
        Application.Current.Resources["AccentBrush"] = accentBrush;
        Application.Current.Resources["AccentColor"] = accentBrush.Color;
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return true; // Default to dark
        }
    }

    // Predefined color palettes
    public static class Colors
    {
        // Primary (Active state)
        public static readonly System.Windows.Media.Color ActiveRed =
            System.Windows.Media.Color.FromRgb(220, 53, 69);

        // Timed state
        public static readonly System.Windows.Media.Color TimedOrange =
            System.Windows.Media.Color.FromRgb(253, 126, 20);

        // Paused state
        public static readonly System.Windows.Media.Color PausedGray =
            System.Windows.Media.Color.FromRgb(108, 117, 125);

        // Dark theme backgrounds
        public static readonly System.Windows.Media.Color DarkBackground =
            System.Windows.Media.Color.FromRgb(32, 32, 32);
        public static readonly System.Windows.Media.Color DarkSurface =
            System.Windows.Media.Color.FromRgb(45, 45, 45);

        // Light theme backgrounds
        public static readonly System.Windows.Media.Color LightBackground =
            System.Windows.Media.Color.FromRgb(250, 250, 250);
        public static readonly System.Windows.Media.Color LightSurface =
            System.Windows.Media.Color.FromRgb(255, 255, 255);
    }
}

public enum Theme
{
    Light,
    Dark
}
