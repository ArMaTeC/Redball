using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Design token pipeline for Figma/JSON bidirectional synchronization.
/// Automates theme generation from design files and exports tokens to Figma.
/// </summary>
public sealed class DesignTokenPipelineService
{
    private readonly HttpClient _httpClient;
    private readonly string _figmaApiToken;
    private readonly string _figmaFileKey;
    
    public static DesignTokenPipelineService Instance { get; } = new();

    private DesignTokenPipelineService()
    {
        _httpClient = new HttpClient();
        _figmaApiToken = Environment.GetEnvironmentVariable("FIGMA_API_TOKEN") ?? "";
        _figmaFileKey = Environment.GetEnvironmentVariable("FIGMA_FILE_KEY") ?? "";
    }

    /// <summary>
    /// Imports design tokens from Figma file and generates theme files.
    /// </summary>
    public async Task<TokenSyncResult> ImportFromFigmaAsync()
    {
        if (string.IsNullOrEmpty(_figmaApiToken) || string.IsNullOrEmpty(_figmaFileKey))
        {
            return TokenSyncResult.Fail("Figma API token or file key not configured");
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _figmaApiToken);

            // Fetch Figma file
            var response = await _httpClient.GetAsync(
                $"https://api.figma.com/v1/files/{_figmaFileKey}?geometry=paths&plugin_data=shared");
            
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var figmaFile = JsonSerializer.Deserialize<FigmaFile>(json);

            if (figmaFile == null)
            {
                return TokenSyncResult.Fail("Failed to parse Figma file");
            }

            // Extract tokens from Figma styles
            var tokens = ExtractTokensFromFigma(figmaFile);
            
            // Generate theme files
            var themesDir = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "src", "Redball.UI.WPF", "Themes");
            
            foreach (var theme in tokens.Themes)
            {
                await GenerateThemeFileAsync(themesDir, theme);
            }

            // Save token manifest
            await SaveTokenManifestAsync(tokens);

            Logger.Info("DesignTokenPipeline", $"Imported {tokens.Colors.Count} colors, {tokens.Typography.Count} typography tokens from Figma");

            return TokenSyncResult.Ok(tokens);
        }
        catch (Exception ex)
        {
            Logger.Error("DesignTokenPipeline", "Failed to import from Figma", ex);
            return TokenSyncResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Exports current design tokens to Figma file.
    /// </summary>
    public async Task<TokenSyncResult> ExportToFigmaAsync(DesignTokenCollection tokens)
    {
        if (string.IsNullOrEmpty(_figmaApiToken) || string.IsNullOrEmpty(_figmaFileKey))
        {
            return TokenSyncResult.Fail("Figma API token or file key not configured");
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _figmaApiToken);

            // Create or update styles in Figma
            var postData = new
            {
                styles = tokens.Colors.Select(c => new
                {
                    name = $"Colors/{c.Key}",
                    style_type = "FILL",
                    description = c.Value.Description
                }).ToList()
            };

            var json = JsonSerializer.Serialize(postData);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"https://api.figma.com/v1/files/{_figmaFileKey}/styles", content);

            response.EnsureSuccessStatusCode();

            Logger.Info("DesignTokenPipeline", $"Exported {tokens.Colors.Count} tokens to Figma");

            return TokenSyncResult.Ok(tokens);
        }
        catch (Exception ex)
        {
            Logger.Error("DesignTokenPipeline", "Failed to export to Figma", ex);
            return TokenSyncResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Validates current theme files against design tokens.
    /// </summary>
    public async Task<ValidationResult> ValidateThemesAsync()
    {
        var issues = new List<ValidationIssue>();
        
        try
        {
            // Load token manifest
            var manifestPath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "src", "Redball.UI.WPF", "Themes", "tokens.json");
            
            if (!File.Exists(manifestPath))
            {
                return new ValidationResult { IsValid = false, Issues = new List<ValidationIssue> { new() { Message = "Token manifest not found" } } };
            }

            var json = await File.ReadAllTextAsync(manifestPath);
            var tokens = JsonSerializer.Deserialize<DesignTokenCollection>(json);

            if (tokens == null)
            {
                return new ValidationResult { IsValid = false, Issues = new List<ValidationIssue> { new() { Message = "Failed to parse token manifest" } } };
            }

            // Validate each theme
            foreach (var theme in tokens.Themes)
            {
                var themePath = Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "src", "Redball.UI.WPF", "Themes", $"{theme.Name}.xaml");
                
                if (!File.Exists(themePath))
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Theme file missing: {theme.Name}.xaml",
                        Theme = theme.Name
                    });
                    continue;
                }

                var themeContent = await File.ReadAllTextAsync(themePath);
                
                // Check for missing colors
                foreach (var color in theme.Colors)
                {
                    if (!themeContent.Contains(color.Key))
                    {
                        issues.Add(new ValidationIssue
                        {
                            Severity = ValidationSeverity.Warning,
                            Message = $"Color token not found in theme: {color.Key}",
                            Theme = theme.Name,
                            Token = color.Key
                        });
                    }
                }
            }

            return new ValidationResult
            {
                IsValid = !issues.Any(i => i.Severity == ValidationSeverity.Error),
                Issues = issues,
                TotalTokens = tokens.Colors.Count + tokens.Typography.Count + tokens.Spacing.Count,
                TotalThemes = tokens.Themes.Count
            };
        }
        catch (Exception ex)
        {
            return new ValidationResult { IsValid = false, Issues = new List<ValidationIssue> { new() { Message = $"Validation failed: {ex.Message}" } } };
        }
    }

    /// <summary>
    /// Generates a theme file from token definitions.
    /// </summary>
    private async Task GenerateThemeFileAsync(string themesDir, ThemeDefinition theme)
    {
        var sb = new System.Text.StringBuilder();
        
        sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
        sb.AppendLine();
        sb.AppendLine($"    <!-- {theme.Name} Theme - Auto-generated from Figma -->");
        sb.AppendLine($"    <!-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
        sb.AppendLine();

        // Colors
        sb.AppendLine("    <!-- Colors -->");
        foreach (var color in theme.Colors)
        {
            sb.AppendLine($"    <Color x:Key=\"{color.Key}\">{color.Value}</Color>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"{color.Key}Brush\" Color=\"{{DynamicResource {color.Key}}}\" />");
        }

        sb.AppendLine();
        
        // Typography
        sb.AppendLine("    <!-- Typography -->");
        foreach (var font in theme.Typography)
        {
            sb.AppendLine($"    <FontFamily x:Key=\"{font.Key}\">{font.Value}</FontFamily>");
        }

        sb.AppendLine();
        
        // Spacing
        sb.AppendLine("    <!-- Spacing -->");
        foreach (var spacing in theme.Spacing)
        {
            sb.AppendLine($"    <Thickness x:Key=\"{spacing.Key}\">{spacing.Value}</Thickness>");
        }

        sb.AppendLine();
        sb.AppendLine("</ResourceDictionary>");

        var filePath = Path.Combine(themesDir, $"{theme.Name}.xaml");
        await File.WriteAllTextAsync(filePath, sb.ToString());

        Logger.Debug("DesignTokenPipeline", $"Generated theme file: {filePath}");
    }

    private async Task SaveTokenManifestAsync(DesignTokenCollection tokens)
    {
        var manifestPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "src", "Redball.UI.WPF", "Themes", "tokens.json");
        
        var json = JsonSerializer.Serialize(tokens, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, json);
    }

    private DesignTokenCollection ExtractTokensFromFigma(FigmaFile file)
    {
        var tokens = new DesignTokenCollection();
        
        // Extract from Figma styles
        foreach (var style in file.Styles.Values)
        {
            if (style.StyleType == "FILL")
            {
                // Extract color
                var color = ExtractColorFromStyle(file, style);
                if (color != null)
                {
                    tokens.Colors[style.Name.Replace("Colors/", "")] = color;
                }
            }
            else if (style.StyleType == "TEXT")
            {
                // Extract typography
                var font = ExtractFontFromStyle(file, style);
                if (font != null)
                {
                    tokens.Typography[style.Name.Replace("Typography/", "")] = font;
                }
            }
        }

        // Generate theme variants
        tokens.Themes = GenerateThemeVariants(tokens);

        return tokens;
    }

    private ColorToken? ExtractColorFromStyle(FigmaFile file, FigmaStyle style)
    {
        // Find node with this style
        var node = FindNodeWithStyle(file.Document, style.Key);
        if (node?.Fills?.FirstOrDefault() is not FigmaPaint fill) return null;

        return new ColorToken
        {
            Key = style.Name.Replace("Colors/", "").Replace("/", ""),
            Hex = FigmaColorToHex(fill.Color),
            Rgba = fill.Color,
            Description = style.Description
        };
    }

    private FontToken? ExtractFontFromStyle(FigmaFile file, FigmaStyle style)
    {
        var node = FindNodeWithStyle(file.Document, style.Key);
        if (node?.Style == null) return null;

        return new FontToken
        {
            Key = style.Name.Replace("Typography/", "").Replace("/", ""),
            Family = node.Style.FontFamily ?? "Segoe UI",
            Size = node.Style.FontSize,
            Weight = node.Style.FontWeight,
            LineHeight = node.Style.LineHeight
        };
    }

    private FigmaNode? FindNodeWithStyle(FigmaNode node, string styleKey)
    {
        if (node?.Styles?.Values.Contains(styleKey) == true)
            return node;

        if (node?.Children != null)
        {
            foreach (var child in node.Children)
            {
                var found = FindNodeWithStyle(child, styleKey);
                if (found != null) return found;
            }
        }

        return null;
    }

    private List<ThemeDefinition> GenerateThemeVariants(DesignTokenCollection tokens)
    {
        // Generate theme variants (Dark, Light, etc.)
        var themes = new List<ThemeDefinition>();

        // Extract base theme name from color names
        var baseColors = tokens.Colors.Where(c => !c.Key.Contains("Dark") && !c.Key.Contains("Light"));
        
        themes.Add(new ThemeDefinition
        {
            Name = "Dark",
            Colors = tokens.Colors.Where(c => !c.Key.Contains("Light")).ToDictionary(k => k.Key, v => v.Value),
            Typography = tokens.Typography,
            Spacing = tokens.Spacing
        });

        themes.Add(new ThemeDefinition
        {
            Name = "Light",
            Colors = tokens.Colors.Where(c => !c.Key.Contains("Dark")).ToDictionary(k => k.Key, v => v.Value),
            Typography = tokens.Typography,
            Spacing = tokens.Spacing
        });

        return themes;
    }

    private static string FigmaColorToHex(FigmaColor color)
    {
        var r = (int)(color.R * 255);
        var g = (int)(color.G * 255);
        var b = (int)(color.B * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}

// Data models

public class DesignTokenCollection
{
    public Dictionary<string, ColorToken> Colors { get; set; } = new();
    public Dictionary<string, FontToken> Typography { get; set; } = new();
    public Dictionary<string, SpacingToken> Spacing { get; set; } = new();
    public List<ThemeDefinition> Themes { get; set; } = new();
    public DateTime LastSync { get; set; }
}

public class ColorToken
{
    public string Key { get; set; } = "";
    public string Hex { get; set; } = "";
    public FigmaColor Rgba { get; set; } = new();
    public string Description { get; set; } = "";
}

public class FontToken
{
    public string Key { get; set; } = "";
    public string Family { get; set; } = "";
    public float Size { get; set; }
    public int Weight { get; set; }
    public object? LineHeight { get; set; }
}

public class SpacingToken
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class ThemeDefinition
{
    public string Name { get; set; } = "";
    public Dictionary<string, ColorToken> Colors { get; set; } = new();
    public Dictionary<string, FontToken> Typography { get; set; } = new();
    public Dictionary<string, SpacingToken> Spacing { get; set; } = new();
}

// Figma API models

public class FigmaFile
{
    [JsonPropertyName("document")]
    public FigmaNode Document { get; set; } = new();
    
    [JsonPropertyName("styles")]
    public Dictionary<string, FigmaStyle> Styles { get; set; } = new();
}

public class FigmaNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("children")]
    public List<FigmaNode> Children { get; set; } = new();
    
    [JsonPropertyName("styles")]
    public Dictionary<string, string> Styles { get; set; } = new();
    
    [JsonPropertyName("fills")]
    public List<FigmaPaint> Fills { get; set; } = new();
    
    [JsonPropertyName("style")]
    public FigmaTextStyle Style { get; set; } = new();
}

public class FigmaStyle
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("styleType")]
    public string StyleType { get; set; } = "";
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public class FigmaPaint
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("color")]
    public FigmaColor Color { get; set; } = new();
}

public class FigmaColor
{
    [JsonPropertyName("r")]
    public float R { get; set; }
    
    [JsonPropertyName("g")]
    public float G { get; set; }
    
    [JsonPropertyName("b")]
    public float B { get; set; }
    
    [JsonPropertyName("a")]
    public float A { get; set; } = 1;
}

public class FigmaTextStyle
{
    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "";
    
    [JsonPropertyName("fontSize")]
    public float FontSize { get; set; }
    
    [JsonPropertyName("fontWeight")]
    public int FontWeight { get; set; }
    
    [JsonPropertyName("lineHeight")]
    public object? LineHeight { get; set; }
}

// Result types

public record TokenSyncResult(bool Success, DesignTokenCollection? Tokens, string? Error)
{
    public static TokenSyncResult Ok(DesignTokenCollection tokens) => new(true, tokens, null);
    public static TokenSyncResult Fail(string error) => new(false, null, error);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationIssue> Issues { get; set; } = new();
    public int TotalTokens { get; set; }
    public int TotalThemes { get; set; }
}

public class ValidationIssue
{
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = "";
    public string? Theme { get; set; }
    public string? Token { get; set; }
}

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}
