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

        // Load base theme (Dark or Light)
        var baseUri = IsLightVariant(theme)
            ? new Uri("pack://application:,,,/Themes/LightTheme.xaml")
            : new Uri("pack://application:,,,/Themes/DarkTheme.xaml");

        _currentTheme = new ResourceDictionary { Source = baseUri };
        Application.Current.Resources.MergedDictionaries.Add(_currentTheme);

        // Apply variant-specific color overrides
        ApplyThemeVariant(theme);
    }

    public static void ToggleTheme()
    {
        SetTheme(CurrentTheme == Theme.Dark ? Theme.Light : Theme.Dark);
    }

    private static bool IsLightVariant(Theme theme)
    {
        return theme == Theme.Light || theme == Theme.ArcticFrost;
    }

    private static void ApplyThemeVariant(Theme theme)
    {
        var res = Application.Current.Resources;

        switch (theme)
        {
            case Theme.Dark:
            case Theme.Light:
                // Base themes use default colors from XAML, just set accent
                SetBrush(res, "AccentBrush", 220, 53, 69);
                SetBrush(res, "AccentLightBrush", 227, 75, 92);
                SetBrush(res, "AccentDarkBrush", 181, 43, 58);
                break;

            case Theme.MidnightBlue:
                SetBrush(res, "BackgroundBrush", 15, 20, 35);
                SetBrush(res, "SurfaceBrush", 22, 30, 50);
                SetBrush(res, "CardBrush", 30, 42, 68);
                SetBrush(res, "ForegroundBrush", 200, 210, 230);
                SetBrush(res, "ForegroundSecondaryBrush", 140, 155, 185);
                SetBrush(res, "AccentBrush", 70, 130, 230);
                SetBrush(res, "AccentLightBrush", 100, 155, 240);
                SetBrush(res, "AccentDarkBrush", 50, 100, 190);
                SetBrush(res, "BorderBrush", 40, 55, 85);
                SetBrush(res, "BorderLightBrush", 55, 70, 100);
                break;

            case Theme.ForestGreen:
                SetBrush(res, "BackgroundBrush", 18, 28, 20);
                SetBrush(res, "SurfaceBrush", 25, 40, 28);
                SetBrush(res, "CardBrush", 35, 55, 38);
                SetBrush(res, "ForegroundBrush", 210, 230, 210);
                SetBrush(res, "ForegroundSecondaryBrush", 150, 180, 150);
                SetBrush(res, "AccentBrush", 76, 175, 80);
                SetBrush(res, "AccentLightBrush", 102, 195, 106);
                SetBrush(res, "AccentDarkBrush", 56, 142, 60);
                SetBrush(res, "BorderBrush", 45, 65, 48);
                SetBrush(res, "BorderLightBrush", 58, 80, 60);
                break;

            case Theme.OceanBlue:
                SetBrush(res, "BackgroundBrush", 15, 25, 35);
                SetBrush(res, "SurfaceBrush", 20, 35, 50);
                SetBrush(res, "CardBrush", 28, 48, 65);
                SetBrush(res, "ForegroundBrush", 205, 225, 240);
                SetBrush(res, "ForegroundSecondaryBrush", 140, 170, 200);
                SetBrush(res, "AccentBrush", 0, 150, 200);
                SetBrush(res, "AccentLightBrush", 30, 175, 220);
                SetBrush(res, "AccentDarkBrush", 0, 120, 170);
                SetBrush(res, "BorderBrush", 35, 55, 75);
                SetBrush(res, "BorderLightBrush", 48, 68, 90);
                break;

            case Theme.SunsetOrange:
                SetBrush(res, "BackgroundBrush", 35, 20, 15);
                SetBrush(res, "SurfaceBrush", 48, 28, 20);
                SetBrush(res, "CardBrush", 62, 38, 28);
                SetBrush(res, "ForegroundBrush", 240, 220, 205);
                SetBrush(res, "ForegroundSecondaryBrush", 200, 170, 145);
                SetBrush(res, "AccentBrush", 255, 120, 50);
                SetBrush(res, "AccentLightBrush", 255, 145, 80);
                SetBrush(res, "AccentDarkBrush", 220, 95, 30);
                SetBrush(res, "BorderBrush", 75, 48, 35);
                SetBrush(res, "BorderLightBrush", 90, 60, 45);
                break;

            case Theme.RoyalPurple:
                SetBrush(res, "BackgroundBrush", 22, 15, 35);
                SetBrush(res, "SurfaceBrush", 32, 22, 50);
                SetBrush(res, "CardBrush", 45, 32, 68);
                SetBrush(res, "ForegroundBrush", 225, 210, 240);
                SetBrush(res, "ForegroundSecondaryBrush", 170, 150, 195);
                SetBrush(res, "AccentBrush", 156, 90, 220);
                SetBrush(res, "AccentLightBrush", 178, 120, 235);
                SetBrush(res, "AccentDarkBrush", 130, 65, 190);
                SetBrush(res, "BorderBrush", 58, 42, 82);
                SetBrush(res, "BorderLightBrush", 72, 55, 98);
                break;

            case Theme.SlateGray:
                SetBrush(res, "BackgroundBrush", 30, 35, 40);
                SetBrush(res, "SurfaceBrush", 42, 48, 55);
                SetBrush(res, "CardBrush", 55, 62, 70);
                SetBrush(res, "ForegroundBrush", 220, 225, 230);
                SetBrush(res, "ForegroundSecondaryBrush", 160, 170, 180);
                SetBrush(res, "AccentBrush", 0, 188, 212);
                SetBrush(res, "AccentLightBrush", 38, 206, 228);
                SetBrush(res, "AccentDarkBrush", 0, 151, 167);
                SetBrush(res, "BorderBrush", 65, 72, 80);
                SetBrush(res, "BorderLightBrush", 80, 88, 96);
                break;

            case Theme.RoseGold:
                SetBrush(res, "BackgroundBrush", 35, 22, 25);
                SetBrush(res, "SurfaceBrush", 48, 30, 35);
                SetBrush(res, "CardBrush", 62, 42, 48);
                SetBrush(res, "ForegroundBrush", 240, 215, 220);
                SetBrush(res, "ForegroundSecondaryBrush", 200, 165, 175);
                SetBrush(res, "AccentBrush", 230, 130, 150);
                SetBrush(res, "AccentLightBrush", 240, 158, 175);
                SetBrush(res, "AccentDarkBrush", 200, 100, 120);
                SetBrush(res, "BorderBrush", 78, 52, 58);
                SetBrush(res, "BorderLightBrush", 92, 65, 72);
                break;

            case Theme.Cyberpunk:
                SetBrush(res, "BackgroundBrush", 10, 8, 18);
                SetBrush(res, "SurfaceBrush", 18, 14, 30);
                SetBrush(res, "CardBrush", 28, 22, 45);
                SetBrush(res, "ForegroundBrush", 0, 255, 230);
                SetBrush(res, "ForegroundSecondaryBrush", 0, 190, 180);
                SetBrush(res, "AccentBrush", 255, 0, 128);
                SetBrush(res, "AccentLightBrush", 255, 50, 160);
                SetBrush(res, "AccentDarkBrush", 210, 0, 100);
                SetBrush(res, "BorderBrush", 40, 30, 60);
                SetBrush(res, "BorderLightBrush", 55, 42, 78);
                break;

            case Theme.Coffee:
                SetBrush(res, "BackgroundBrush", 30, 22, 16);
                SetBrush(res, "SurfaceBrush", 42, 32, 24);
                SetBrush(res, "CardBrush", 58, 45, 35);
                SetBrush(res, "ForegroundBrush", 235, 220, 200);
                SetBrush(res, "ForegroundSecondaryBrush", 185, 165, 140);
                SetBrush(res, "AccentBrush", 195, 140, 60);
                SetBrush(res, "AccentLightBrush", 215, 165, 85);
                SetBrush(res, "AccentDarkBrush", 165, 115, 40);
                SetBrush(res, "BorderBrush", 70, 55, 42);
                SetBrush(res, "BorderLightBrush", 85, 68, 52);
                break;

            case Theme.ArcticFrost:
                // Light variant with icy blue tones
                SetBrush(res, "BackgroundBrush", 235, 242, 250);
                SetBrush(res, "SurfaceBrush", 245, 250, 255);
                SetBrush(res, "CardBrush", 225, 235, 248);
                SetBrush(res, "ForegroundBrush", 30, 50, 70);
                SetBrush(res, "ForegroundSecondaryBrush", 80, 105, 135);
                SetBrush(res, "AccentBrush", 50, 130, 200);
                SetBrush(res, "AccentLightBrush", 80, 155, 220);
                SetBrush(res, "AccentDarkBrush", 30, 105, 175);
                SetBrush(res, "BorderBrush", 195, 210, 230);
                SetBrush(res, "BorderLightBrush", 210, 222, 238);
                break;
        }
    }

    private static void SetBrush(ResourceDictionary res, string key, byte r, byte g, byte b)
    {
        res[key] = new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    public static bool IsSystemDarkMode()
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

    /// <summary>
    /// Maps a theme name string (from config) to Theme enum
    /// </summary>
    public static Theme ThemeFromString(string name)
    {
        return name switch
        {
            "System" => IsSystemDarkMode() ? Theme.Dark : Theme.Light,
            "Light" => Theme.Light,
            "Dark" => Theme.Dark,
            "MidnightBlue" => Theme.MidnightBlue,
            "ForestGreen" => Theme.ForestGreen,
            "OceanBlue" => Theme.OceanBlue,
            "SunsetOrange" => Theme.SunsetOrange,
            "RoyalPurple" => Theme.RoyalPurple,
            "SlateGray" => Theme.SlateGray,
            "RoseGold" => Theme.RoseGold,
            "Cyberpunk" => Theme.Cyberpunk,
            "Coffee" => Theme.Coffee,
            "ArcticFrost" => Theme.ArcticFrost,
            _ => Theme.Dark
        };
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
    Dark,
    MidnightBlue,
    ForestGreen,
    OceanBlue,
    SunsetOrange,
    RoyalPurple,
    SlateGray,
    RoseGold,
    Cyberpunk,
    Coffee,
    ArcticFrost
}
