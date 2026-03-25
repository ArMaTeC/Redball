namespace Redball.UI.WPF.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using Redball.UI.WPF.Models;
using Redball.UI.WPF.Views.Pages;

/// <summary>
/// Searchable index of all commands and settings for the command palette.
/// Implements UX Progressive Disclosure from improve_me.txt item D.
/// </summary>
public sealed class CommandPaletteIndex
{
    private static readonly Lazy<CommandPaletteIndex> _instance = new(() => new CommandPaletteIndex());
    public static CommandPaletteIndex Instance => _instance.Value;

    private readonly List<PaletteCommand> _commands = new();
    private readonly List<SettingDefinition> _settings = new();

    private CommandPaletteIndex()
    {
        RegisterDefaultCommands();
        RegisterDefaultSettings();
    }

    /// <summary>
    /// Registers a command in the palette.
    /// </summary>
    public void RegisterCommand(PaletteCommand command)
    {
        _commands.Add(command);
    }

    /// <summary>
    /// Registers a setting definition.
    /// </summary>
    public void RegisterSetting(SettingDefinition setting)
    {
        _settings.Add(setting);
    }

    /// <summary>
    /// Searches commands and settings by query string.
    /// </summary>
    public IReadOnlyList<PaletteCommand> Search(string query, VisibilityTier maxTier = VisibilityTier.Basic)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _commands
                .Where(c => c.CanExecute?.Invoke() ?? true)
                .Take(10)
                .ToList();
        }

        var normalizedQuery = query.ToLowerInvariant();
        var terms = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var results = _commands
            .Where(c => c.CanExecute?.Invoke() ?? true)
            .Select(c => new
            {
                Command = c,
                Score = CalculateScore(c, terms, normalizedQuery)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(15)
            .Select(x => x.Command)
            .ToList();

        return results;
    }

    /// <summary>
    /// Gets settings by visibility tier.
    /// </summary>
    public IReadOnlyList<SettingDefinition> GetSettingsByTier(VisibilityTier tier)
    {
        return _settings
            .Where(s => s.Tier <= tier)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Name)
            .ToList();
    }

    /// <summary>
    /// Gets all settings for search.
    /// </summary>
    public IReadOnlyList<SettingDefinition> GetSettings()
    {
        return _settings.ToList();
    }

    private static int CalculateScore(PaletteCommand command, string[] terms, string fullQuery)
    {
        int score = 0;
        var title = command.Title.ToLowerInvariant();
        var subtitle = command.Subtitle.ToLowerInvariant();
        var keywords = command.Keywords.Select(k => k.ToLowerInvariant()).ToList();

        foreach (var term in terms)
        {
            // Exact match in title (highest priority)
            if (title == term) score += 100;
            // Title starts with term
            else if (title.StartsWith(term)) score += 50;
            // Title contains term
            else if (title.Contains(term)) score += 30;
            // Subtitle contains term
            else if (subtitle.Contains(term)) score += 20;
            // Keywords match
            else if (keywords.Any(k => k.Contains(term))) score += 15;
        }

        // Boost for exact full query match
        if (title.Contains(fullQuery)) score += 25;

        return score;
    }

    private void RegisterDefaultCommands()
    {
        // Navigation commands
        RegisterCommand(new PaletteCommand
        {
            Id = "nav.dashboard",
            Title = "Dashboard",
            Subtitle = "Go to main dashboard",
            Category = "Navigation",
            IconGlyph = "\uE80F", // Home
            NavigateTo = "Dashboard",
            Keywords = new[] { "home", "main", "overview", "status" }
        });

        RegisterCommand(new PaletteCommand
        {
            Id = "nav.settings",
            Title = "Settings",
            Subtitle = "Open settings panel",
            Category = "Navigation",
            IconGlyph = "\uE713", // Settings
            NavigateTo = "Settings",
            Keywords = new[] { "config", "options", "preferences" },
            Shortcut = "Ctrl+,"
        });

        RegisterCommand(new PaletteCommand
        {
            Id = "nav.miniwidget",
            Title = "Mini Widget Settings",
            Subtitle = "Configure mini widget appearance",
            Category = "Navigation",
            IconGlyph = "\uE7C5", // MiniContract
            NavigateTo = "MiniWidgetSettings",
            Keywords = new[] { "widget", "floating", "compact", "overlay" }
        });

        // Action commands
        RegisterCommand(new PaletteCommand
        {
            Id = "action.toggle",
            Title = "Toggle Active",
            Subtitle = "Start or stop keep-awake",
            Category = "Actions",
            IconGlyph = "\uE768", // Play
            Execute = () => { /* Would call KeepAwakeService.Toggle() */ },
            Keywords = new[] { "start", "stop", "enable", "disable", "pause", "resume" },
            Shortcut = "Ctrl+Alt+Pause"
        });

        RegisterCommand(new PaletteCommand
        {
            Id = "action.typething",
            Title = "TypeThing",
            Subtitle = "Type clipboard content",
            Category = "Actions",
            IconGlyph = "\uE8A1", // Keyboard
            Execute = () => { /* Would call TypeThing */ },
            Keywords = new[] { "clipboard", "paste", "type", "sendkeys" },
            Shortcut = "Ctrl+Shift+V"
        });

        RegisterCommand(new PaletteCommand
        {
            Id = "action.stoptypething",
            Title = "Stop TypeThing",
            Subtitle = "Stop typing operation",
            Category = "Actions",
            IconGlyph = "\uE71A", // Stop
            Execute = () => { /* Would call StopTypeThing */ },
            Keywords = new[] { "cancel", "abort", "typing" },
            Shortcut = "Ctrl+Shift+X"
        });

        RegisterCommand(new PaletteCommand
        {
            Id = "action.emergency_release",
            Title = "Emergency HID Release",
            Subtitle = "Release all HID resources immediately",
            Category = "Actions",
            IconGlyph = "\uE7BA", // Warning
            Execute = () => { /* Would call EmergencyReleaseHid */ },
            Keywords = new[] { "emergency", "release", "keyboard", "reset", "restore" },
            Shortcut = "Ctrl+Shift+Esc"
        });

        // Feature toggles
        RegisterCommand(new PaletteCommand
        {
            Id = "toggle.minimode",
            Title = "Toggle Mini Mode",
            Subtitle = "Show/hide mini widget",
            Category = "Features",
            IconGlyph = "\uE7C5", // MiniContract
            Execute = () => { /* Toggle mini widget */ },
            Keywords = new[] { "widget", "compact", "floating", "overlay" }
        });

        // Help
        RegisterCommand(new PaletteCommand
        {
            Id = "help.docs",
            Title = "Documentation",
            Subtitle = "Open help documentation",
            Category = "Help",
            IconGlyph = "\uE8E6", // Help
            Execute = () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/ArMaTeC/redball/wiki",
                UseShellExecute = true
            }),
            Keywords = new[] { "wiki", "docs", "help", "guide", "manual" }
        });

        // Diagnostics
        RegisterCommand(new PaletteCommand
        {
            Id = "diag.synchealth",
            Title = "Sync Health",
            Subtitle = "View sync queue status and health",
            Category = "Diagnostics",
            IconGlyph = "\uE895", // Sync
            NavigateTo = "SyncHealth",
            Keywords = new[] { "sync", "queue", "offline", "outbox", "reconcile" }
        });

        RegisterCommand(new PaletteCommand
        {
            Id = "diag.telemetry",
            Title = "Crash Telemetry",
            Subtitle = "View crash reports and diagnostics",
            Category = "Diagnostics",
            IconGlyph = "\uE7BA", // Warning
            NavigateTo = "CrashTelemetry",
            Keywords = new[] { "crash", "error", "report", "diagnostics", "debug" }
        });

        RegisterCommand(new PaletteCommand
        {
            Id = "diag.export",
            Title = "Export Diagnostics",
            Subtitle = "Create diagnostics bundle for support",
            Category = "Diagnostics",
            IconGlyph = "\uE78C", // Save
            Execute = () => { /* Export diagnostics */ },
            Keywords = new[] { "export", "logs", "support", "bundle" }
        });
    }

    private void RegisterDefaultSettings()
    {
        // Basic settings
        RegisterSetting(new SettingDefinition
        {
            Id = "keepawake.enabled",
            Name = "Keep Awake Enabled",
            Description = "Prevent system sleep when active",
            Category = "Keep Awake",
            Tier = VisibilityTier.Basic,
            Tags = new[] { "sleep", "prevent", "active" },
            CommandId = "settings.keepawake.enabled",
            ConfigPath = "KeepAwakeEnabled",
            IconGlyph = "\uE768"
        });

        RegisterSetting(new SettingDefinition
        {
            Id = "keepawake.display",
            Name = "Prevent Display Sleep",
            Description = "Keep monitor on when active",
            Category = "Keep Awake",
            Tier = VisibilityTier.Basic,
            Tags = new[] { "display", "monitor", "screen" },
            CommandId = "settings.keepawake.display",
            ConfigPath = "PreventDisplaySleep",
            IconGlyph = "\uE7F4"
        });

        // Advanced settings
        RegisterSetting(new SettingDefinition
        {
            Id = "typething.hidmode",
            Name = "TypeThing Input Mode",
            Description = "Select input method: SendInput, HID driver, or Service",
            Category = "TypeThing",
            Tier = VisibilityTier.Advanced,
            Tags = new[] { "hid", "driver", "service", "input", "remote", "rdp" },
            CommandId = "settings.typething.mode",
            ConfigPath = "TypeThingInputMode",
            IconGlyph = "\uE8A1"
        });

        RegisterSetting(new SettingDefinition
        {
            Id = "schedule.enabled",
            Name = "Scheduled Activation",
            Description = "Automatically enable keep-awake on schedule",
            Category = "Schedule",
            Tier = VisibilityTier.Advanced,
            Tags = new[] { "timer", "auto", "schedule", "time" },
            CommandId = "settings.schedule.enabled",
            ConfigPath = "ScheduleEnabled",
            IconGlyph = "\uE823"
        });

        // Experimental settings
        RegisterSetting(new SettingDefinition
        {
            Id = "sync.experimental",
            Name = "Experimental Sync Features",
            Description = "Enable preview sync capabilities",
            Category = "Sync",
            Tier = VisibilityTier.Experimental,
            Tags = new[] { "experimental", "preview", "beta" },
            CommandId = "settings.sync.experimental",
            ConfigPath = "ExperimentalSync",
            IconGlyph = "\uE783",
            RequiresRestart = true
        });
    }
}
