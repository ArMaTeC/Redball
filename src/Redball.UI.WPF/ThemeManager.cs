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
    private static bool _controlsLoaded;
    private static bool _isWatchingSystemTheme;

    public static Theme CurrentTheme { get; private set; } = Theme.Dark;

    /// <summary>
    /// Whether ThemeManager is currently following the system theme.
    /// True when the user's config theme is set to "System".
    /// </summary>
    public static bool IsFollowingSystemTheme { get; private set; }

    public static void Initialize()
    {
        // Detect system theme preference and follow it
        var isDarkMode = IsSystemDarkMode();
        SetTheme(isDarkMode ? Theme.Dark : Theme.Light);
        IsFollowingSystemTheme = true; // Re-enable after SetTheme resets it
    }

    /// <summary>
    /// Begin monitoring Windows theme changes. When the OS theme toggles
    /// between light and dark mode, the app theme is updated automatically
    /// if <see cref="IsFollowingSystemTheme"/> is true.
    /// </summary>
    public static void StartWatchingSystemTheme()
    {
        if (_isWatchingSystemTheme) return;
        _isWatchingSystemTheme = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Services.Logger.Info("ThemeManager", "Started watching system theme changes");
    }

    /// <summary>
    /// Stop monitoring Windows theme changes. Call this on app shutdown
    /// to prevent leaking the static event handler.
    /// </summary>
    public static void StopWatchingSystemTheme()
    {
        if (!_isWatchingSystemTheme) return;
        _isWatchingSystemTheme = false;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Services.Logger.Info("ThemeManager", "Stopped watching system theme changes");
    }

    private static void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        // Only react to General category changes which include theme switches
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
        if (!IsFollowingSystemTheme) return;

        var isDark = IsSystemDarkMode();
        var desired = isDark ? Theme.Dark : Theme.Light;
        if (desired == CurrentTheme) return;

        Services.Logger.Info("ThemeManager", $"System theme changed — switching to {desired}");

        // Marshal to the UI thread; preserve following flag after SetTheme resets it
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            SetTheme(desired);
            IsFollowingSystemTheme = true;
        });
    }

    /// <summary>
    /// Apply a theme resolved from config. When the config value is "System",
    /// call <see cref="ThemeFromString"/> first — it sets <see cref="IsFollowingSystemTheme"/>
    /// before this method runs, and this overload preserves the flag.
    /// </summary>
    public static void SetThemeFromConfig(string configValue)
    {
        var theme = ThemeFromString(configValue);
        // ThemeFromString already set IsFollowingSystemTheme for "System"
        var wasFollowing = IsFollowingSystemTheme;
        SetTheme(theme);
        IsFollowingSystemTheme = wasFollowing;
    }

    public static void SetTheme(Theme theme)
    {
        CurrentTheme = theme;
        IsFollowingSystemTheme = false; // Explicit theme set; stop auto-following

        // Load shared control styles once (was previously in App.xaml statically)
        if (!_controlsLoaded)
        {
            var controlsDict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/Controls.xaml")
            };
            Application.Current.Resources.MergedDictionaries.Add(controlsDict);
            _controlsLoaded = true;
        }

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
                // Dark theme - match DarkTheme.xaml values
                SetBrush(res, "BackgroundBrush", 32, 32, 32);
                SetBrush(res, "SurfaceBrush", 0x80, 45, 45, 45);
                SetBrush(res, "CardBrush", 0x70, 60, 60, 60);
                SetBrush(res, "ForegroundBrush", 224, 224, 224);
                SetBrush(res, "ForegroundSecondaryBrush", 176, 176, 176);
                SetBrush(res, "ForegroundDisabledBrush", 128, 128, 128);
                SetBrush(res, "AccentBrush", 220, 53, 69); // Bootstrap Danger Red
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255); // White on Red
                SetBrush(res, "AccentLightBrush", 227, 75, 92);
                SetBrush(res, "AccentDarkBrush", 181, 43, 58);
                SetBrush(res, "BorderBrush", 0x60, 64, 64, 64);
                SetBrush(res, "BorderLightBrush", 0x80, 80, 80, 80);
                SetBrush(res, "HoverBrush", 0x40, 100, 100, 100);
                break;

            case Theme.Light:
                // Light theme - match LightTheme.xaml values
                SetBrush(res, "BackgroundBrush", 250, 250, 250);
                SetBrush(res, "SurfaceBrush", 0xE0, 255, 255, 255);
                SetBrush(res, "CardBrush", 0xD0, 245, 245, 245);
                SetBrush(res, "ForegroundBrush", 33, 37, 41);
                SetBrush(res, "ForegroundSecondaryBrush", 108, 117, 125);
                SetBrush(res, "ForegroundDisabledBrush", 173, 181, 189);
                SetBrush(res, "AccentBrush", 220, 53, 69);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255); // White on Red
                SetBrush(res, "AccentLightBrush", 227, 75, 92);
                SetBrush(res, "AccentDarkBrush", 181, 43, 58);
                SetBrush(res, "BorderBrush", 0x70, 222, 226, 230);
                SetBrush(res, "BorderLightBrush", 0x90, 233, 236, 239);
                SetBrush(res, "HoverBrush", 0x20, 0, 0, 0);
                break;

            case Theme.MidnightBlue:
                SetBrush(res, "BackgroundBrush", 15, 20, 35);
                SetBrush(res, "SurfaceBrush", 0x80, 22, 30, 50);
                SetBrush(res, "CardBrush", 0x70, 30, 42, 68);
                SetBrush(res, "ForegroundBrush", 200, 210, 230);
                SetBrush(res, "ForegroundSecondaryBrush", 140, 155, 185);
                SetBrush(res, "AccentBrush", 70, 130, 230);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 100, 155, 240);
                SetBrush(res, "AccentDarkBrush", 50, 100, 190);
                SetBrush(res, "BorderBrush", 0x60, 40, 50, 60);
                SetBrush(res, "BorderLightBrush", 0x80, 60, 70, 80);
                SetBrush(res, "HoverBrush", 0x40, 80, 100, 120);
                break;

            case Theme.ForestGreen:
                SetBrush(res, "BackgroundBrush", 18, 28, 20);
                SetBrush(res, "SurfaceBrush", 0x80, 25, 40, 28);
                SetBrush(res, "CardBrush", 0x70, 35, 55, 38);
                SetBrush(res, "ForegroundBrush", 210, 230, 210);
                SetBrush(res, "ForegroundSecondaryBrush", 150, 180, 150);
                SetBrush(res, "AccentBrush", 76, 175, 80);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 102, 195, 106);
                SetBrush(res, "AccentDarkBrush", 56, 142, 60);
                SetBrush(res, "BorderBrush", 0x60, 40, 60, 40);
                SetBrush(res, "BorderLightBrush", 0x80, 60, 80, 60);
                SetBrush(res, "HoverBrush", 0x40, 80, 120, 80);
                break;

            case Theme.OceanBlue:
                SetBrush(res, "BackgroundBrush", 15, 25, 35);
                SetBrush(res, "SurfaceBrush", 0x80, 20, 35, 50);
                SetBrush(res, "CardBrush", 0x70, 28, 48, 65);
                SetBrush(res, "ForegroundBrush", 205, 225, 240);
                SetBrush(res, "ForegroundSecondaryBrush", 140, 170, 200);
                SetBrush(res, "AccentBrush", 0, 150, 200);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 30, 175, 220);
                SetBrush(res, "AccentDarkBrush", 0, 120, 170);
                SetBrush(res, "BorderBrush", 0x60, 35, 55, 75);
                SetBrush(res, "BorderLightBrush", 0x80, 48, 68, 90);
                SetBrush(res, "HoverBrush", 0x40, 60, 90, 110);
                break;

            case Theme.SunsetOrange:
                SetBrush(res, "BackgroundBrush", 35, 20, 15);
                SetBrush(res, "SurfaceBrush", 0x80, 48, 28, 20);
                SetBrush(res, "CardBrush", 0x70, 62, 38, 28);
                SetBrush(res, "ForegroundBrush", 240, 220, 205);
                SetBrush(res, "ForegroundSecondaryBrush", 200, 170, 145);
                SetBrush(res, "AccentBrush", 255, 120, 50);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 255, 145, 80);
                SetBrush(res, "AccentDarkBrush", 220, 95, 30);
                SetBrush(res, "BorderBrush", 0x60, 60, 40, 30);
                SetBrush(res, "BorderLightBrush", 0x80, 80, 60, 50);
                SetBrush(res, "HoverBrush", 0x40, 120, 80, 60);
                break;

            case Theme.RoyalPurple:
                SetBrush(res, "BackgroundBrush", 22, 15, 35);
                SetBrush(res, "SurfaceBrush", 0x80, 32, 22, 50);
                SetBrush(res, "CardBrush", 0x70, 45, 32, 68);
                SetBrush(res, "ForegroundBrush", 225, 210, 240);
                SetBrush(res, "ForegroundSecondaryBrush", 170, 150, 195);
                SetBrush(res, "AccentBrush", 156, 90, 220);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 178, 120, 235);
                SetBrush(res, "AccentDarkBrush", 130, 65, 190);
                SetBrush(res, "BorderBrush", 0x60, 58, 42, 82);
                SetBrush(res, "BorderLightBrush", 0x80, 72, 55, 98);
                SetBrush(res, "HoverBrush", 0x40, 90, 70, 110);
                break;

            case Theme.SlateGray:
                SetBrush(res, "BackgroundBrush", 30, 35, 40);
                SetBrush(res, "SurfaceBrush", 0x80, 42, 48, 55);
                SetBrush(res, "CardBrush", 0x70, 55, 62, 70);
                SetBrush(res, "ForegroundBrush", 220, 225, 230);
                SetBrush(res, "ForegroundSecondaryBrush", 160, 170, 180);
                SetBrush(res, "AccentBrush", 0, 188, 212);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 38, 206, 228);
                SetBrush(res, "AccentDarkBrush", 0, 151, 167);
                SetBrush(res, "BorderBrush", 0x60, 60, 60, 60);
                SetBrush(res, "BorderLightBrush", 0x80, 80, 80, 80);
                SetBrush(res, "HoverBrush", 0x40, 100, 100, 100);
                break;

            case Theme.RoseGold:
                SetBrush(res, "BackgroundBrush", 35, 22, 25);
                SetBrush(res, "SurfaceBrush", 0x80, 48, 30, 35);
                SetBrush(res, "CardBrush", 0x70, 62, 42, 48);
                SetBrush(res, "ForegroundBrush", 240, 215, 220);
                SetBrush(res, "ForegroundSecondaryBrush", 200, 165, 175);
                SetBrush(res, "AccentBrush", 230, 130, 150);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 240, 158, 175);
                SetBrush(res, "AccentDarkBrush", 200, 100, 120);
                SetBrush(res, "BorderBrush", 0x60, 70, 50, 50);
                SetBrush(res, "BorderLightBrush", 0x80, 90, 70, 70);
                SetBrush(res, "HoverBrush", 0x40, 120, 90, 90);
                break;

            case Theme.Cyberpunk:
                SetBrush(res, "BackgroundBrush", 10, 8, 18);
                SetBrush(res, "SurfaceBrush", 0x80, 18, 14, 30);
                SetBrush(res, "CardBrush", 0x70, 28, 22, 45);
                SetBrush(res, "ForegroundBrush", 0, 255, 230);
                SetBrush(res, "ForegroundSecondaryBrush", 0, 190, 180);
                SetBrush(res, "AccentBrush", 255, 0, 128);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 255, 50, 160);
                SetBrush(res, "AccentDarkBrush", 210, 0, 100);
                SetBrush(res, "BorderBrush", 0x60, 40, 40, 60);
                SetBrush(res, "BorderLightBrush", 0x80, 60, 60, 80);
                SetBrush(res, "HoverBrush", 0x40, 80, 80, 120);
                break;

            case Theme.Coffee:
                SetBrush(res, "BackgroundBrush", 30, 22, 16);
                SetBrush(res, "SurfaceBrush", 0x80, 42, 32, 24);
                SetBrush(res, "CardBrush", 0x70, 58, 45, 35);
                SetBrush(res, "ForegroundBrush", 235, 220, 200);
                SetBrush(res, "ForegroundSecondaryBrush", 185, 165, 140);
                SetBrush(res, "AccentBrush", 195, 140, 60);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 215, 165, 85);
                SetBrush(res, "AccentDarkBrush", 165, 115, 40);
                SetBrush(res, "BorderBrush", 0x60, 70, 55, 42);
                SetBrush(res, "BorderLightBrush", 0x80, 85, 68, 52);
                SetBrush(res, "HoverBrush", 0x40, 100, 80, 60);
                break;

            case Theme.ArcticFrost:
                // Light variant with icy blue tones
                SetBrush(res, "BackgroundBrush", 235, 242, 250);
                SetBrush(res, "SurfaceBrush", 0xE0, 245, 250, 255);
                SetBrush(res, "CardBrush", 0xD0, 225, 235, 248);
                SetBrush(res, "ForegroundBrush", 30, 50, 70);
                SetBrush(res, "ForegroundSecondaryBrush", 80, 105, 135);
                SetBrush(res, "AccentBrush", 50, 130, 200);
                SetBrush(res, "AccentForegroundBrush", 255, 255, 255);
                SetBrush(res, "AccentLightBrush", 80, 155, 220);
                SetBrush(res, "AccentDarkBrush", 30, 105, 175);
                SetBrush(res, "BorderBrush", 0x70, 195, 210, 230);
                SetBrush(res, "BorderLightBrush", 0x90, 210, 222, 238);
                SetBrush(res, "HoverBrush", 0x20, 150, 180, 200);
                break;

            case Theme.HighContrast:
                // WCAG 2.1 AA compliant high contrast theme
                // Pure black and white with high contrast accents
                SetBrush(res, "BackgroundBrush", 0, 0, 0);
                SetBrush(res, "SurfaceBrush", 0, 0, 0);
                SetBrush(res, "CardBrush", 20, 20, 20);
                SetBrush(res, "ForegroundBrush", 255, 255, 255);
                SetBrush(res, "ForegroundSecondaryBrush", 255, 255, 255);
                SetBrush(res, "AccentBrush", 255, 255, 0); // Yellow
                SetBrush(res, "AccentForegroundBrush", 0, 0, 0); // Black on Yellow
                SetBrush(res, "AccentLightBrush", 255, 255, 100);
                SetBrush(res, "AccentDarkBrush", 200, 200, 0);
                SetBrush(res, "BorderBrush", 0x60, 255, 255, 255);
                SetBrush(res, "BorderLightBrush", 0x80, 255, 255, 255);
                SetBrush(res, "HoverBrush", 0x40, 255, 255, 255);
                break;
        }
    }

    private static void SetBrush(ResourceDictionary res, string key, byte r, byte g, byte b)
    {
        res[key] = new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private static void SetBrush(ResourceDictionary res, string key, byte a, byte r, byte g, byte b)
    {
        res[key] = new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    /// <summary>
    /// Resolves the current system theme and enables auto-following.
    /// Used when config theme is "System".
    /// </summary>
    private static Theme ResolveSystemTheme()
    {
        // Flag will be preserved by SetThemeFromConfig; direct SetTheme calls reset it
        IsFollowingSystemTheme = true;
        return IsSystemDarkMode() ? Theme.Dark : Theme.Light;
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
            "System" => ResolveSystemTheme(),
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
            "HighContrast" => Theme.HighContrast,
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
    ArcticFrost,
    HighContrast
}
