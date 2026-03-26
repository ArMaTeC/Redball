using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Redball.UI.Services;
using System.Windows.Media;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Design token definition.
/// </summary>
public class DesignToken
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = ""; // spacing, typography, color, elevation, motion
    public string Value { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>
/// Color palette.
/// </summary>
public class ColorPalette
{
    public string Name { get; set; } = "";
    public Color Primary { get; set; }
    public Color Secondary { get; set; }
    public Color Background { get; set; }
    public Color Surface { get; set; }
    public Color Error { get; set; }
    public Color OnPrimary { get; set; }
    public Color OnSecondary { get; set; }
    public Color OnBackground { get; set; }
    public Color OnSurface { get; set; }
    public Color OnError { get; set; }
}

/// <summary>
/// Typography scale.
/// </summary>
public class TypographyScale
{
    public string Name { get; set; } = "";
    public double FontSize { get; set; }
    public FontWeight FontWeight { get; set; }
    public double LineHeight { get; set; }
    public double LetterSpacing { get; set; }
}

/// <summary>
/// Elevation (shadow) definition.
/// </summary>
public class Elevation
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public double ShadowDepth { get; set; }
    public double BlurRadius { get; set; }
    public Color ShadowColor { get; set; }
}

/// <summary>
/// Motion (animation) definition.
/// </summary>
public class MotionSpec
{
    public string Name { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public string Easing { get; set; } = "ease-out";
    public double? Delay { get; set; }
}

/// <summary>
/// Tokenized design system service.
/// Implements ui-1 from improve_me.txt: Establish tokenized design system.
/// </summary>
public class DesignSystemService
{
    private static readonly Lazy<DesignSystemService> _instance = new(() => new DesignSystemService());
    public static DesignSystemService Instance => _instance.Value;

    private readonly Dictionary<string, DesignToken> _tokens = new();
    private readonly List<ColorPalette> _palettes = new();
    private readonly List<TypographyScale> _typography = new();
    private readonly List<Elevation> _elevations = new();
    private readonly List<MotionSpec> _motions = new();

    private DesignSystemService()
    {
        InitializeDefaultTokens();
        Logger.Info("DesignSystemService", "Design system service initialized");
    }

    /// <summary>
    /// Gets a token value.
    /// </summary>
    public string? GetToken(string name)
    {
        return _tokens.TryGetValue(name, out var token) ? token.Value : null;
    }

    /// <summary>
    /// Gets spacing value in pixels.
    /// </summary>
    public double GetSpacing(string tokenName)
    {
        var value = GetToken(tokenName);
        return value != null && double.TryParse(value, out var result) ? result : 8;
    }

    /// <summary>
    /// Gets color from palette.
    /// </summary>
    public Color GetColor(string paletteName, string colorName)
    {
        var palette = _palettes.FirstOrDefault(p => p.Name == paletteName);
        if (palette == null) return Colors.Gray;

        return colorName.ToLowerInvariant() switch
        {
            "primary" => palette.Primary,
            "secondary" => palette.Secondary,
            "background" => palette.Background,
            "surface" => palette.Surface,
            "error" => palette.Error,
            "onprimary" => palette.OnPrimary,
            "onsecondary" => palette.OnSecondary,
            "onbackground" => palette.OnBackground,
            "onsurface" => palette.OnSurface,
            "onerror" => palette.OnError,
            _ => Colors.Gray
        };
    }

    /// <summary>
    /// Gets typography scale.
    /// </summary>
    public TypographyScale? GetTypography(string name)
    {
        return _typography.FirstOrDefault(t => t.Name == name);
    }

    /// <summary>
    /// Gets elevation.
    /// </summary>
    public Elevation? GetElevation(int level)
    {
        return _elevations.FirstOrDefault(e => e.Level == level);
    }

    /// <summary>
    /// Gets motion specification.
    /// </summary>
    public MotionSpec? GetMotion(string name)
    {
        return _motions.FirstOrDefault(m => m.Name == name);
    }

    /// <summary>
    /// Gets all tokens by category.
    /// </summary>
    public IEnumerable<DesignToken> GetTokensByCategory(string category)
    {
        return _tokens.Values.Where(t => t.Category == category);
    }

    /// <summary>
    /// Exports design system to JSON.
    /// </summary>
    public string ExportToJson()
    {
        var export = new
        {
            Tokens = _tokens.Values,
            Palettes = _palettes.Select(p => new
            {
                p.Name,
                Primary = p.Primary.ToString(),
                Secondary = p.Secondary.ToString(),
                Background = p.Background.ToString(),
                Surface = p.Surface.ToString()
            }),
            Typography = _typography,
            Elevations = _elevations,
            Motion = _motions
        };

        return System.Text.Json.JsonSerializer.Serialize(export, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private void InitializeDefaultTokens()
    {
        // Spacing tokens (8px base grid)
        AddToken("spacing-xs", "4", "spacing", "Extra small spacing");
        AddToken("spacing-sm", "8", "spacing", "Small spacing");
        AddToken("spacing-md", "16", "spacing", "Medium spacing");
        AddToken("spacing-lg", "24", "spacing", "Large spacing");
        AddToken("spacing-xl", "32", "spacing", "Extra large spacing");
        AddToken("spacing-2xl", "48", "spacing", "2x large spacing");
        AddToken("spacing-3xl", "64", "spacing", "3x large spacing");

        // Default color palette
        _palettes.Add(new ColorPalette
        {
            Name = "default",
            Primary = Color.FromRgb(220, 53, 69), // Redball red
            Secondary = Color.FromRgb(108, 117, 125),
            Background = Color.FromRgb(248, 249, 250),
            Surface = Colors.White,
            Error = Color.FromRgb(220, 53, 69),
            OnPrimary = Colors.White,
            OnSecondary = Colors.White,
            OnBackground = Color.FromRgb(33, 37, 41),
            OnSurface = Color.FromRgb(33, 37, 41),
            OnError = Colors.White
        });

        // Dark palette
        _palettes.Add(new ColorPalette
        {
            Name = "dark",
            Primary = Color.FromRgb(220, 53, 69),
            Secondary = Color.FromRgb(108, 117, 125),
            Background = Color.FromRgb(33, 37, 41),
            Surface = Color.FromRgb(52, 58, 64),
            Error = Color.FromRgb(248, 215, 218),
            OnPrimary = Colors.White,
            OnSecondary = Colors.White,
            OnBackground = Color.FromRgb(248, 249, 250),
            OnSurface = Color.FromRgb(248, 249, 250),
            OnError = Color.FromRgb(33, 37, 41)
        });

        // Typography scale
        _typography.Add(new TypographyScale { Name = "h1", FontSize = 32, FontWeight = FontWeights.Light, LineHeight = 1.2, LetterSpacing = -0.5 });
        _typography.Add(new TypographyScale { Name = "h2", FontSize = 24, FontWeight = FontWeights.Light, LineHeight = 1.3, LetterSpacing = -0.5 });
        _typography.Add(new TypographyScale { Name = "h3", FontSize = 20, FontWeight = FontWeights.Normal, LineHeight = 1.4, LetterSpacing = 0 });
        _typography.Add(new TypographyScale { Name = "h4", FontSize = 16, FontWeight = FontWeights.Normal, LineHeight = 1.4, LetterSpacing = 0.25 });
        _typography.Add(new TypographyScale { Name = "body", FontSize = 14, FontWeight = FontWeights.Normal, LineHeight = 1.5, LetterSpacing = 0.5 });
        _typography.Add(new TypographyScale { Name = "caption", FontSize = 12, FontWeight = FontWeights.Normal, LineHeight = 1.5, LetterSpacing = 0.4 });
        _typography.Add(new TypographyScale { Name = "button", FontSize = 14, FontWeight = FontWeights.Medium, LineHeight = 1.5, LetterSpacing = 1.25 });

        // Elevation levels
        _elevations.Add(new Elevation { Name = "none", Level = 0, ShadowDepth = 0, BlurRadius = 0, ShadowColor = Color.FromArgb(0, 0, 0, 0) });
        _elevations.Add(new Elevation { Name = "low", Level = 1, ShadowDepth = 2, BlurRadius = 4, ShadowColor = Color.FromArgb(26, 0, 0, 0) });
        _elevations.Add(new Elevation { Name = "medium", Level = 2, ShadowDepth = 4, BlurRadius = 8, ShadowColor = Color.FromArgb(26, 0, 0, 0) });
        _elevations.Add(new Elevation { Name = "high", Level = 3, ShadowDepth = 8, BlurRadius = 16, ShadowColor = Color.FromArgb(26, 0, 0, 0) });
        _elevations.Add(new Elevation { Name = "highest", Level = 4, ShadowDepth = 16, BlurRadius = 32, ShadowColor = Color.FromArgb(26, 0, 0, 0) });

        // Motion specs
        _motions.Add(new MotionSpec { Name = "fast", Duration = TimeSpan.FromMilliseconds(150), Easing = "ease-out" });
        _motions.Add(new MotionSpec { Name = "normal", Duration = TimeSpan.FromMilliseconds(300), Easing = "ease-in-out" });
        _motions.Add(new MotionSpec { Name = "slow", Duration = TimeSpan.FromMilliseconds(500), Easing = "ease-in-out" });
        _motions.Add(new MotionSpec { Name = "emphasis", Duration = TimeSpan.FromMilliseconds(250), Easing = "cubic-bezier(0.4, 0, 0.2, 1)" });
    }

    private void AddToken(string name, string value, string category, string description)
    {
        _tokens[name] = new DesignToken
        {
            Name = name,
            Category = category,
            Value = value,
            Description = description
        };
    }
}
