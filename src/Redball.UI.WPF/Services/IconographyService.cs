using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Icon definition with unified semantics.
/// </summary>
public class UnifiedIcon
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Semantics { get; set; } = ""; // "active", "inactive", "warning", "error", "info"
    public string? TrayVariant { get; set; }
    public string? MainWindowVariant { get; set; }
    public string? WidgetVariant { get; set; }
    public Color PrimaryColor { get; set; }
    public Color SecondaryColor { get; set; }
    public bool IsAnimated { get; set; }
}

/// <summary>
/// Status semantic mapping.
/// </summary>
public class StatusSemantic
{
    public string Status { get; set; } = ""; // "keepawake_active", "keepawake_timed", "keepawake_paused"
    public string IconId { get; set; } = "";
    public Color IndicatorColor { get; set; }
    public string TooltipTemplate { get; set; } = "";
    public string? SoundCue { get; set; }
}

/// <summary>
/// Service for unified iconography and status semantics.
/// Implements ui-5 from improve_me.txt: Unify iconography and status semantics across tray/main/widget.
/// </summary>
public class IconographyService
{
    private static readonly Lazy<IconographyService> _instance = new(() => new IconographyService());
    public static IconographyService Instance => _instance.Value;

    private readonly List<UnifiedIcon> _icons = new();
    private readonly List<StatusSemantic> _statusSemantics = new();

    private IconographyService()
    {
        InitializeDefaultIcons();
        InitializeStatusSemantics();
        Logger.Info("IconographyService", "Iconography service initialized");
    }

    /// <summary>
    /// Gets an icon by ID.
    /// </summary>
    public UnifiedIcon? GetIcon(string id)
    {
        return _icons.FirstOrDefault(i => i.Id == id);
    }

    /// <summary>
    /// Gets the appropriate icon variant for a context.
    /// </summary>
    public string? GetIconForContext(string iconId, string context)
    {
        var icon = GetIcon(iconId);
        if (icon == null) return null;

        return context.ToLowerInvariant() switch
        {
            "tray" => icon.TrayVariant,
            "mainwindow" or "main" => icon.MainWindowVariant,
            "widget" or "miniwidget" => icon.WidgetVariant,
            _ => icon.MainWindowVariant
        };
    }

    /// <summary>
    /// Gets status semantic for a state.
    /// </summary>
    public StatusSemantic? GetStatusSemantic(string status)
    {
        return _statusSemantics.FirstOrDefault(s => s.Status == status);
    }

    /// <summary>
    /// Resolves icon and status for current state.
    /// </summary>
    public ResolvedStatus ResolveStatus(string appState, bool isTray = false)
    {
        var semantic = GetStatusSemantic(appState);
        if (semantic == null)
        {
            return new ResolvedStatus
            {
                IconPath = "default.png",
                Color = Colors.Gray,
                Tooltip = "Unknown state"
            };
        }

        var icon = GetIcon(semantic.IconId);
        var context = isTray ? "tray" : "main";
        var iconPath = GetIconForContext(semantic.IconId, context);

        return new ResolvedStatus
        {
            IconPath = iconPath ?? "default.png",
            Color = semantic.IndicatorColor,
            Tooltip = semantic.TooltipTemplate,
            SoundCue = semantic.SoundCue,
            IsAnimated = icon?.IsAnimated ?? false
        };
    }

    /// <summary>
    /// Registers a unified icon.
    /// </summary>
    public void RegisterIcon(UnifiedIcon icon)
    {
        _icons.Add(icon);
        Logger.Info("IconographyService", $"Icon registered: {icon.Name}");
    }

    /// <summary>
    /// Registers a status semantic.
    /// </summary>
    public void RegisterStatusSemantic(StatusSemantic semantic)
    {
        _statusSemantics.Add(semantic);
        Logger.Info("IconographyService", $"Status semantic registered: {semantic.Status}");
    }

    /// <summary>
    /// Gets all icon definitions.
    /// </summary>
    public IReadOnlyList<UnifiedIcon> GetAllIcons()
    {
        return _icons.ToList();
    }

    /// <summary>
    /// Gets icon audit summary.
    /// </summary>
    public IconAuditSummary GetAuditSummary()
    {
        var trayCoverage = _icons.Count(i => !string.IsNullOrEmpty(i.TrayVariant));
        var mainCoverage = _icons.Count(i => !string.IsNullOrEmpty(i.MainWindowVariant));
        var widgetCoverage = _icons.Count(i => !string.IsNullOrEmpty(i.WidgetVariant));

        return new IconAuditSummary
        {
            TotalIcons = _icons.Count,
            TrayCoverage = trayCoverage,
            MainWindowCoverage = mainCoverage,
            WidgetCoverage = widgetCoverage,
            TrayCoveragePercent = _icons.Any() ? (double)trayCoverage / _icons.Count * 100 : 0,
            MainWindowCoveragePercent = _icons.Any() ? (double)mainCoverage / _icons.Count * 100 : 0,
            WidgetCoveragePercent = _icons.Any() ? (double)widgetCoverage / _icons.Count * 100 : 0,
            StatusSemantics = _statusSemantics.Count,
            MissingVariants = _icons
                .Where(i => string.IsNullOrEmpty(i.TrayVariant) || string.IsNullOrEmpty(i.WidgetVariant))
                .Select(i => i.Name)
                .ToList()
        };
    }

    private void InitializeDefaultIcons()
    {
        _icons.Add(new UnifiedIcon
        {
            Id = "redball_active",
            Name = "Redball Active",
            Semantics = "active",
            TrayVariant = "Assets/Icons/tray_active.ico",
            MainWindowVariant = "Assets/Icons/app_active.png",
            WidgetVariant = "Assets/Icons/widget_active.png",
            PrimaryColor = Color.FromRgb(220, 53, 69),
            SecondaryColor = Colors.White,
            IsAnimated = false
        });

        _icons.Add(new UnifiedIcon
        {
            Id = "redball_timed",
            Name = "Redball Timed",
            Semantics = "timed",
            TrayVariant = "Assets/Icons/tray_timed.ico",
            MainWindowVariant = "Assets/Icons/app_timed.png",
            WidgetVariant = "Assets/Icons/widget_timed.png",
            PrimaryColor = Color.FromRgb(253, 126, 20),
            SecondaryColor = Colors.White,
            IsAnimated = true
        });

        _icons.Add(new UnifiedIcon
        {
            Id = "redball_paused",
            Name = "Redball Paused",
            Semantics = "inactive",
            TrayVariant = "Assets/Icons/tray_paused.ico",
            MainWindowVariant = "Assets/Icons/app_paused.png",
            WidgetVariant = "Assets/Icons/widget_paused.png",
            PrimaryColor = Color.FromRgb(108, 117, 125),
            SecondaryColor = Colors.White,
            IsAnimated = false
        });

        _icons.Add(new UnifiedIcon
        {
            Id = "status_warning",
            Name = "Status Warning",
            Semantics = "warning",
            TrayVariant = "Assets/Icons/tray_warning.ico",
            MainWindowVariant = "Assets/Icons/warning.png",
            WidgetVariant = "Assets/Icons/widget_warning.png",
            PrimaryColor = Color.FromRgb(255, 193, 7),
            SecondaryColor = Colors.Black,
            IsAnimated = true
        });

        _icons.Add(new UnifiedIcon
        {
            Id = "status_error",
            Name = "Status Error",
            Semantics = "error",
            TrayVariant = "Assets/Icons/tray_error.ico",
            MainWindowVariant = "Assets/Icons/error.png",
            WidgetVariant = "Assets/Icons/widget_error.png",
            PrimaryColor = Color.FromRgb(220, 53, 69),
            SecondaryColor = Colors.White,
            IsAnimated = true
        });
    }

    private void InitializeStatusSemantics()
    {
        _statusSemantics.Add(new StatusSemantic
        {
            Status = "keepawake_active",
            IconId = "redball_active",
            IndicatorColor = Color.FromRgb(220, 53, 69),
            TooltipTemplate = "Keep-awake is active",
            SoundCue = null
        });

        _statusSemantics.Add(new StatusSemantic
        {
            Status = "keepawake_timed",
            IconId = "redball_timed",
            IndicatorColor = Color.FromRgb(253, 126, 20),
            TooltipTemplate = "Keep-awake: {0} minutes remaining",
            SoundCue = null
        });

        _statusSemantics.Add(new StatusSemantic
        {
            Status = "keepawake_paused",
            IconId = "redball_paused",
            IndicatorColor = Color.FromRgb(108, 117, 125),
            TooltipTemplate = "Keep-awake is paused",
            SoundCue = null
        });

        _statusSemantics.Add(new StatusSemantic
        {
            Status = "typething_active",
            IconId = "redball_active",
            IndicatorColor = Color.FromRgb(40, 167, 69),
            TooltipTemplate = "TypeThing is typing...",
            SoundCue = "Assets/Sounds/typing.wav"
        });

        _statusSemantics.Add(new StatusSemantic
        {
            Status = "battery_low",
            IconId = "status_warning",
            IndicatorColor = Color.FromRgb(255, 193, 7),
            TooltipTemplate = "Battery low ({0}%) - Keep-awake may pause",
            SoundCue = "Assets/Sounds/warning.wav"
        });
    }
}

/// <summary>
/// Resolved status information.
/// </summary>
public class ResolvedStatus
{
    public string? IconPath { get; set; }
    public Color Color { get; set; }
    public string? Tooltip { get; set; }
    public string? SoundCue { get; set; }
    public bool IsAnimated { get; set; }
}

/// <summary>
/// Icon audit summary.
/// </summary>
public class IconAuditSummary
{
    public int TotalIcons { get; set; }
    public int TrayCoverage { get; set; }
    public int MainWindowCoverage { get; set; }
    public int WidgetCoverage { get; set; }
    public double TrayCoveragePercent { get; set; }
    public double MainWindowCoveragePercent { get; set; }
    public double WidgetCoveragePercent { get; set; }
    public int StatusSemantics { get; set; }
    public List<string> MissingVariants { get; set; } = new();
}
