using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Theme definition.
/// </summary>
public class ThemeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Color BackgroundColor { get; set; }
    public Color ForegroundColor { get; set; }
    public Color AccentColor { get; set; }
    public Color ControlBackground { get; set; }
    public Color ControlForeground { get; set; }
    public Color BorderColor { get; set; }
    public bool IsDark { get; set; }
    public bool IsHighContrast { get; set; }
}

/// <summary>
/// Theme QA test result for a control.
/// </summary>
public class ControlThemeResult
{
    public string ControlType { get; set; } = "";
    public string ThemeId { get; set; } = "";
    public bool BackgroundReadable { get; set; }
    public bool ForegroundReadable { get; set; }
    public bool BorderVisible { get; set; }
    public bool FocusVisible { get; set; }
    public double ContrastRatio { get; set; }
    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// Theme QA matrix service.
/// Implements ui-6 from improve_me.txt: Theme QA matrix for control readability across all 14 themes.
/// </summary>
public class ThemeQAMatrixService
{
    private static readonly Lazy<ThemeQAMatrixService> _instance = new(() => new ThemeQAMatrixService());
    public static ThemeQAMatrixService Instance => _instance.Value;

    private readonly List<ThemeDefinition> _themes = new();
    private readonly List<string> _controlTypes = new();
    private readonly List<ControlThemeResult> _testResults = new();

    private ThemeQAMatrixService()
    {
        InitializeDefaultThemes();
        InitializeControlTypes();
        Logger.Info("ThemeQAMatrixService", "Theme QA matrix service initialized");
    }

    /// <summary>
    /// Gets all themes.
    /// </summary>
    public IReadOnlyList<ThemeDefinition> GetThemes()
    {
        return _themes.ToList();
    }

    /// <summary>
    /// Runs QA tests for all theme/control combinations.
    /// </summary>
    public List<ControlThemeResult> RunFullQAMatrix()
    {
        _testResults.Clear();

        foreach (var theme in _themes)
        {
            foreach (var controlType in _controlTypes)
            {
                var result = TestControlInTheme(controlType, theme);
                _testResults.Add(result);
            }
        }

        Logger.Info("ThemeQAMatrixService", $"QA matrix complete: {_testResults.Count} tests, {_testResults.Count(r => !r.BackgroundReadable || !r.ForegroundReadable)} failures");
        return _testResults;
    }

    /// <summary>
    /// Tests a specific control in a specific theme.
    /// </summary>
    public ControlThemeResult TestControlInTheme(string controlType, ThemeDefinition theme)
    {
        var result = new ControlThemeResult
        {
            ControlType = controlType,
            ThemeId = theme.Id
        };

        // Calculate contrast ratios
        var bgContrast = CalculateContrastRatio(theme.ControlBackground, theme.ControlForeground);
        var fgContrast = CalculateContrastRatio(theme.ControlForeground, theme.BackgroundColor);

        result.ContrastRatio = Math.Min(bgContrast, fgContrast);

        // WCAG AA requires 4.5:1 for normal text
        result.BackgroundReadable = bgContrast >= 4.5;
        result.ForegroundReadable = fgContrast >= 4.5;

        // Check border visibility
        var borderContrast = CalculateContrastRatio(theme.BorderColor, theme.ControlBackground);
        result.BorderVisible = borderContrast >= 1.5;

        // Focus visibility (typically needs 3:1)
        var focusContrast = CalculateContrastRatio(theme.AccentColor, theme.ControlBackground);
        result.FocusVisible = focusContrast >= 3.0;

        // Collect issues
        if (!result.BackgroundReadable)
            result.Issues.Add($"Background/foreground contrast {bgContrast:F1}:1 below 4.5:1");
        if (!result.ForegroundReadable)
            result.Issues.Add($"Text/background contrast {fgContrast:F1}:1 below 4.5:1");
        if (!result.BorderVisible)
            result.Issues.Add($"Border contrast {borderContrast:F1}:1 below 1.5:1");
        if (!result.FocusVisible)
            result.Issues.Add($"Focus indicator contrast {focusContrast:F1}:1 below 3.0:1");

        return result;
    }

    /// <summary>
    /// Gets test results for a theme.
    /// </summary>
    public List<ControlThemeResult> GetResultsForTheme(string themeId)
    {
        return _testResults.Where(r => r.ThemeId == themeId).ToList();
    }

    /// <summary>
    /// Gets test results for a control.
    /// </summary>
    public List<ControlThemeResult> GetResultsForControl(string controlType)
    {
        return _testResults.Where(r => r.ControlType == controlType).ToList();
    }

    /// <summary>
    /// Gets QA matrix summary.
    /// </summary>
    public QAMatrixSummary GetSummary()
    {
        var totalTests = _testResults.Count;
        var passedTests = _testResults.Count(r => r.BackgroundReadable && r.ForegroundReadable);
        var failedTests = totalTests - passedTests;

        var byTheme = _themes.ToDictionary(
            t => t.Name,
            t => _testResults
                .Where(r => r.ThemeId == t.Id)
                .Count(r => r.BackgroundReadable && r.ForegroundReadable) /
                (double)_controlTypes.Count * 100
        );

        var problematicCombinations = _testResults
            .Where(r => !r.BackgroundReadable || !r.ForegroundReadable)
            .Select(r => $"{r.ControlType} in {r.ThemeId}")
            .ToList();

        return new QAMatrixSummary
        {
            TotalThemes = _themes.Count,
            TotalControlTypes = _controlTypes.Count,
            TotalTests = totalTests,
            PassedTests = passedTests,
            FailedTests = failedTests,
            PassRate = totalTests > 0 ? (double)passedTests / totalTests * 100 : 0,
            PassRateByTheme = byTheme,
            ProblematicCombinations = problematicCombinations
        };
    }

    /// <summary>
    /// Adds a custom theme.
    /// </summary>
    public void AddTheme(ThemeDefinition theme)
    {
        _themes.Add(theme);
        Logger.Info("ThemeQAMatrixService", $"Theme added: {theme.Name}");
    }

    /// <summary>
    /// Gets contrast-safe themes for a control.
    /// </summary>
    public List<ThemeDefinition> GetSafeThemes(string controlType)
    {
        var safeThemeIds = _testResults
            .Where(r => r.ControlType == controlType && r.BackgroundReadable && r.ForegroundReadable)
            .Select(r => r.ThemeId)
            .Distinct();

        return _themes.Where(t => safeThemeIds.Contains(t.Id)).ToList();
    }

    private void InitializeDefaultThemes()
    {
        // Light themes
        _themes.Add(new ThemeDefinition
        {
            Id = "light_default",
            Name = "Light Default",
            IsDark = false,
            BackgroundColor = Colors.White,
            ForegroundColor = Color.FromRgb(33, 37, 41),
            AccentColor = Color.FromRgb(220, 53, 69),
            ControlBackground = Colors.White,
            ControlForeground = Color.FromRgb(33, 37, 41),
            BorderColor = Color.FromRgb(222, 226, 230)
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "light_blue",
            Name = "Light Blue",
            IsDark = false,
            BackgroundColor = Colors.White,
            ForegroundColor = Color.FromRgb(33, 37, 41),
            AccentColor = Color.FromRgb(0, 123, 255),
            ControlBackground = Colors.White,
            ControlForeground = Color.FromRgb(33, 37, 41),
            BorderColor = Color.FromRgb(0, 123, 255)
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "light_green",
            Name = "Light Green",
            IsDark = false,
            BackgroundColor = Colors.White,
            ForegroundColor = Color.FromRgb(33, 37, 41),
            AccentColor = Color.FromRgb(40, 167, 69),
            ControlBackground = Colors.White,
            ControlForeground = Color.FromRgb(33, 37, 41),
            BorderColor = Color.FromRgb(40, 167, 69)
        });

        // Dark themes
        _themes.Add(new ThemeDefinition
        {
            Id = "dark_default",
            Name = "Dark Default",
            IsDark = true,
            BackgroundColor = Color.FromRgb(33, 37, 41),
            ForegroundColor = Color.FromRgb(248, 249, 250),
            AccentColor = Color.FromRgb(220, 53, 69),
            ControlBackground = Color.FromRgb(52, 58, 64),
            ControlForeground = Color.FromRgb(248, 249, 250),
            BorderColor = Color.FromRgb(73, 80, 87)
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "dark_blue",
            Name = "Dark Blue",
            IsDark = true,
            BackgroundColor = Color.FromRgb(15, 30, 50),
            ForegroundColor = Color.FromRgb(230, 240, 255),
            AccentColor = Color.FromRgb(0, 123, 255),
            ControlBackground = Color.FromRgb(30, 50, 80),
            ControlForeground = Color.FromRgb(230, 240, 255),
            BorderColor = Color.FromRgb(0, 123, 255)
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "dark_amoled",
            Name = "AMOLED Dark",
            IsDark = true,
            BackgroundColor = Colors.Black,
            ForegroundColor = Colors.White,
            AccentColor = Color.FromRgb(220, 53, 69),
            ControlBackground = Color.FromRgb(20, 20, 20),
            ControlForeground = Colors.White,
            BorderColor = Color.FromRgb(60, 60, 60)
        });

        // High contrast themes
        _themes.Add(new ThemeDefinition
        {
            Id = "high_contrast_black",
            Name = "High Contrast Black",
            IsDark = true,
            IsHighContrast = true,
            BackgroundColor = Colors.Black,
            ForegroundColor = Colors.White,
            AccentColor = Colors.Yellow,
            ControlBackground = Colors.Black,
            ControlForeground = Colors.White,
            BorderColor = Colors.White
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "high_contrast_white",
            Name = "High Contrast White",
            IsDark = false,
            IsHighContrast = true,
            BackgroundColor = Colors.White,
            ForegroundColor = Colors.Black,
            AccentColor = Colors.Black,
            ControlBackground = Colors.White,
            ControlForeground = Colors.Black,
            BorderColor = Colors.Black
        });

        // Specialty themes
        _themes.Add(new ThemeDefinition
        {
            Id = "sepia",
            Name = "Sepia",
            IsDark = false,
            BackgroundColor = Color.FromRgb(244, 236, 220),
            ForegroundColor = Color.FromRgb(90, 75, 55),
            AccentColor = Color.FromRgb(139, 90, 43),
            ControlBackground = Color.FromRgb(244, 236, 220),
            ControlForeground = Color.FromRgb(90, 75, 55),
            BorderColor = Color.FromRgb(200, 180, 150)
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "solarized_dark",
            Name = "Solarized Dark",
            IsDark = true,
            BackgroundColor = Color.FromRgb(0, 43, 54),
            ForegroundColor = Color.FromRgb(131, 148, 150),
            AccentColor = Color.FromRgb(211, 54, 130),
            ControlBackground = Color.FromRgb(7, 54, 66),
            ControlForeground = Color.FromRgb(131, 148, 150),
            BorderColor = Color.FromRgb(88, 110, 117)
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "solarized_light",
            Name = "Solarized Light",
            IsDark = false,
            BackgroundColor = Color.FromRgb(253, 246, 227),
            ForegroundColor = Color.FromRgb(101, 123, 131),
            AccentColor = Color.FromRgb(211, 54, 130),
            ControlBackground = Colors.White,
            ControlForeground = Color.FromRgb(101, 123, 131),
            BorderColor = Color.FromRgb(147, 161, 161)
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "nord",
            Name = "Nord",
            IsDark = true,
            BackgroundColor = Color.FromRgb(46, 52, 64),
            ForegroundColor = Color.FromRgb(216, 222, 233),
            AccentColor = Color.FromRgb(136, 192, 208),
            ControlBackground = Color.FromRgb(59, 66, 82),
            ControlForeground = Color.FromRgb(216, 222, 233),
            BorderColor = Color.FromRgb(76, 86, 106)
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "dracula",
            Name = "Dracula",
            IsDark = true,
            BackgroundColor = Color.FromRgb(40, 42, 54),
            ForegroundColor = Color.FromRgb(248, 248, 242),
            AccentColor = Color.FromRgb(255, 121, 198),
            ControlBackground = Color.FromRgb(68, 71, 90),
            ControlForeground = Color.FromRgb(248, 248, 242),
            BorderColor = Color.FromRgb(98, 114, 164)
        });

        _themes.Add(new ThemeDefinition
        {
            Id = "monokai",
            Name = "Monokai",
            IsDark = true,
            BackgroundColor = Color.FromRgb(39, 40, 34),
            ForegroundColor = Color.FromRgb(248, 248, 242),
            AccentColor = Color.FromRgb(253, 151, 31),
            ControlBackground = Color.FromRgb(49, 50, 44),
            ControlForeground = Color.FromRgb(248, 248, 242),
            BorderColor = Color.FromRgb(117, 113, 94)
        });
    }

    private void InitializeControlTypes()
    {
        _controlTypes.AddRange(new[]
        {
            "Button",
            "TextBox",
            "ComboBox",
            "CheckBox",
            "RadioButton",
            "Slider",
            "ToggleSwitch",
            "ListBox",
            "Menu",
            "TreeView",
            "DataGrid",
            "TabControl"
        });
    }

    private double CalculateContrastRatio(Color foreground, Color background)
    {
        var lum1 = CalculateRelativeLuminance(foreground);
        var lum2 = CalculateRelativeLuminance(background);

        var lighter = Math.Max(lum1, lum2);
        var darker = Math.Min(lum1, lum2);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private double CalculateRelativeLuminance(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }
}

/// <summary>
/// QA matrix summary.
/// </summary>
public class QAMatrixSummary
{
    public int TotalThemes { get; set; }
    public int TotalControlTypes { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    public double PassRate { get; set; }
    public Dictionary<string, double> PassRateByTheme { get; set; } = new();
    public List<string> ProblematicCombinations { get; set; } = new();
}
