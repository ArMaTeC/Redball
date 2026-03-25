namespace Redball.UI.WPF.Models;

using System;
using System.Collections.Generic;
using System.Windows.Input;

/// <summary>
/// Visibility tier for progressive disclosure of settings and commands.
/// </summary>
public enum VisibilityTier
{
    /// <summary>Always visible, essential for all users.</summary>
    Basic = 0,

    /// <summary>Advanced features, collapsed by default.</summary>
    Advanced = 1,

    /// <summary>Experimental/preview features, hidden unless explicitly enabled.</summary>
    Experimental = 2
}

/// <summary>
/// Definition of a setting with metadata for progressive disclosure and search.
/// </summary>
public sealed class SettingDefinition
{
    /// <summary>
    /// Unique identifier for the setting.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name for the setting.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Description/tooltip for the setting.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Category/section for grouping.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Visibility tier for progressive disclosure.
    /// </summary>
    public VisibilityTier Tier { get; init; } = VisibilityTier.Basic;

    /// <summary>
    /// Searchable tags for the command palette.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Command ID for jumping to this setting from the command palette.
    /// </summary>
    public string? CommandId { get; init; }

    /// <summary>
    /// Associated config property path.
    /// </summary>
    public string? ConfigPath { get; init; }

    /// <summary>
    /// Icon glyph (Segoe MDL2 Assets) for visual identification.
    /// </summary>
    public string? IconGlyph { get; init; }

    /// <summary>
    /// Keyboard shortcut for direct access.
    /// </summary>
    public KeyGesture? Shortcut { get; init; }

    /// <summary>
    /// Default value for the setting.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Whether this setting requires a restart to take effect.
    /// </summary>
    public bool RequiresRestart { get; init; }
}

/// <summary>
/// A searchable command for the command palette.
/// </summary>
public sealed class PaletteCommand
{
    /// <summary>
    /// Unique command identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display title.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Subtitle/description.
    /// </summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>
    /// Category for grouping.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Searchable keywords/tags.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Icon glyph.
    /// </summary>
    public string IconGlyph { get; init; } = "\uE8A7"; // Default: ActionCenter

    /// <summary>
    /// The action to execute when selected.
    /// </summary>
    public Action? Execute { get; init; }

    /// <summary>
    /// Navigation target if this command jumps to a page/section.
    /// </summary>
    public string? NavigateTo { get; init; }

    /// <summary>
    /// Whether this command is currently available.
    /// </summary>
    public Func<bool>? CanExecute { get; init; }

    /// <summary>
    /// Keyboard shortcut.
    /// </summary>
    public string? Shortcut { get; init; }
}
