using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Redball.UI.Services;

namespace Redball.UI.WPF.Services;

/// <summary>
/// Hotkey registration info.
/// </summary>
public class HotkeyInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Keys Key { get; set; }
    public ModifierKeys Modifiers { get; set; }
    public string Action { get; set; } = "";
    public int? RegisteredId { get; set; }
    public bool IsRegistered { get; set; }
    public string? ConflictDescription { get; set; }
}

/// <summary>
/// Detected hotkey conflict.
/// </summary>
public class HotkeyConflict
{
    public string HotkeyId { get; set; } = "";
    public string HotkeyName { get; set; } = "";
    public string Shortcut { get; set; } = "";
    public string ConflictingApp { get; set; } = "";
    public string ConflictType { get; set; } = ""; // "system", "other_app", "same_app"
    public string Severity { get; set; } = ""; // "blocking", "warning"
}

/// <summary>
/// Remapping suggestion.
/// </summary>
public class RemappingSuggestion
{
    public string OriginalShortcut { get; set; } = "";
    public string SuggestedShortcut { get; set; } = "";
    public Keys SuggestedKey { get; set; }
    public ModifierKeys SuggestedModifiers { get; set; }
    public string Reason { get; set; } = "";
    public int ConflictScore { get; set; } // Lower is better
}

/// <summary>
/// Service for detecting and resolving hotkey conflicts.
/// Implements os-4 from improve_me.txt: Global hotkey conflict detection + user-guided remapping wizard.
/// </summary>
public class HotkeyConflictDetectionService
{
    private static readonly Lazy<HotkeyConflictDetectionService> _instance = new(() => new HotkeyConflictDetectionService());
    public static HotkeyConflictDetectionService Instance => _instance.Value;

    private readonly List<HotkeyInfo> _hotkeys = new();
    private readonly List<HotkeyConflict> _detectedConflicts = new();
    private readonly object _lock = new();

    // Common shortcuts to avoid
    private readonly HashSet<string> _reservedShortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+A", "Ctrl+S",
        "Ctrl+P", "Ctrl+F", "Ctrl+H", "Ctrl+O", "Ctrl+N", "Ctrl+W",
        "Alt+F4", "Alt+Tab", "Win+L", "Win+D", "Win+E", "Ctrl+Alt+Del",
        "F1", "F5", "F11", "Ctrl+Shift+Esc"
    };

    // Suggested alternatives
    private readonly Dictionary<string, List<string>> _alternatives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl+K"] = new() { "Ctrl+Shift+K", "Alt+K", "Ctrl+Alt+K" },
        ["Ctrl+T"] = new() { "Ctrl+Shift+T", "Alt+T", "Ctrl+Alt+T" },
        ["Ctrl+M"] = new() { "Ctrl+Shift+M", "Alt+M", "Ctrl+Alt+M" },
        ["Ctrl+B"] = new() { "Ctrl+Shift+B", "Alt+B", "Ctrl+Alt+B" },
        ["Ctrl+D"] = new() { "Ctrl+Shift+D", "Alt+D", "Ctrl+Alt+D" }
    };

    public event EventHandler<List<HotkeyConflict>>? ConflictsDetected;

    private HotkeyConflictDetectionService()
    {
        InitializeDefaultHotkeys();
        Logger.Info("HotkeyConflictDetectionService", "Hotkey conflict detection service initialized");
    }

    /// <summary>
    /// Registers a hotkey for conflict checking.
    /// </summary>
    public void RegisterHotkey(HotkeyInfo hotkey)
    {
        lock (_lock)
        {
            _hotkeys.Add(hotkey);
        }

        Logger.Info("HotkeyConflictDetectionService", $"Hotkey registered: {hotkey.Name} ({hotkey.Key} + {hotkey.Modifiers})");

        // Check for conflicts immediately
        var conflicts = CheckForConflicts(hotkey);
        if (conflicts.Any())
        {
            lock (_lock)
            {
                _detectedConflicts.AddRange(conflicts);
            }
            ConflictsDetected?.Invoke(this, conflicts);
        }
    }

    /// <summary>
    /// Checks all registered hotkeys for conflicts.
    /// </summary>
    public List<HotkeyConflict> CheckAllConflicts()
    {
        var allConflicts = new List<HotkeyConflict>();

        lock (_lock)
        {
            foreach (var hotkey in _hotkeys)
            {
                var conflicts = CheckForConflicts(hotkey);
                allConflicts.AddRange(conflicts);
            }
        }

        lock (_lock)
        {
            _detectedConflicts.Clear();
            _detectedConflicts.AddRange(allConflicts);
        }

        if (allConflicts.Any())
        {
            ConflictsDetected?.Invoke(this, allConflicts);
        }

        return allConflicts;
    }

    /// <summary>
    /// Gets detected conflicts.
    /// </summary>
    public IReadOnlyList<HotkeyConflict> GetConflicts()
    {
        lock (_lock)
        {
            return _detectedConflicts.ToList();
        }
    }

    /// <summary>
    /// Gets remapping suggestions for a hotkey.
    /// </summary>
    public List<RemappingSuggestion> GetRemappingSuggestions(string hotkeyId)
    {
        HotkeyInfo? hotkey;
        lock (_lock)
        {
            hotkey = _hotkeys.FirstOrDefault(h => h.Id == hotkeyId);
        }

        if (hotkey == null) return new List<RemappingSuggestion>();

        var currentShortcut = FormatShortcut(hotkey.Key, hotkey.Modifiers);
        var suggestions = new List<RemappingSuggestion>();

        // Check if we have predefined alternatives
        if (_alternatives.TryGetValue(currentShortcut, out var alternatives))
        {
            foreach (var alt in alternatives)
            {
                var (key, modifiers) = ParseShortcut(alt);
                var conflictScore = CalculateConflictScore(key, modifiers);

                suggestions.Add(new RemappingSuggestion
                {
                    OriginalShortcut = currentShortcut,
                    SuggestedShortcut = alt,
                    SuggestedKey = key,
                    SuggestedModifiers = modifiers,
                    Reason = conflictScore == 0 ? "No conflicts detected" : $"Low conflict score: {conflictScore}",
                    ConflictScore = conflictScore
                });
            }
        }

        // Generate additional suggestions with Shift modifier
        if ((hotkey.Modifiers & ModifierKeys.Shift) == 0)
        {
            var shiftShortcut = FormatShortcut(hotkey.Key, hotkey.Modifiers | ModifierKeys.Shift);
            var conflictScore = CalculateConflictScore(hotkey.Key, hotkey.Modifiers | ModifierKeys.Shift);

            suggestions.Add(new RemappingSuggestion
            {
                OriginalShortcut = currentShortcut,
                SuggestedShortcut = shiftShortcut,
                SuggestedKey = hotkey.Key,
                SuggestedModifiers = hotkey.Modifiers | ModifierKeys.Shift,
                Reason = conflictScore == 0 ? "Adding Shift modifier typically reduces conflicts" : "Shift variant",
                ConflictScore = conflictScore
            });
        }

        return suggestions.OrderBy(s => s.ConflictScore).ToList();
    }

    /// <summary>
    /// Applies a remapping suggestion.
    /// </summary>
    public bool ApplyRemapping(string hotkeyId, RemappingSuggestion suggestion)
    {
        lock (_lock)
        {
            var hotkey = _hotkeys.FirstOrDefault(h => h.Id == hotkeyId);
            if (hotkey == null) return false;

            // Update the hotkey
            hotkey.Key = suggestion.SuggestedKey;
            hotkey.Modifiers = suggestion.SuggestedModifiers;

            Logger.Info("HotkeyConflictDetectionService",
                $"Hotkey remapped: {hotkey.Name} -> {suggestion.SuggestedShortcut}");

            return true;
        }
    }

    /// <summary>
    /// Gets the conflict report summary.
    /// </summary>
    public ConflictReportSummary GetConflictReport()
    {
        lock (_lock)
        {
            var blocking = _detectedConflicts.Count(c => c.Severity == "blocking");
            var warnings = _detectedConflicts.Count(c => c.Severity == "warning");

            return new ConflictReportSummary
            {
                TotalHotkeys = _hotkeys.Count,
                RegisteredHotkeys = _hotkeys.Count(h => h.IsRegistered),
                BlockingConflicts = blocking,
                WarningConflicts = warnings,
                CanUseAllHotkeys = blocking == 0,
                ConflictsNeedingResolution = _detectedConflicts
                    .Where(c => c.Severity == "blocking")
                    .Select(c => c.HotkeyName)
                    .ToList()
            };
        }
    }

    private List<HotkeyConflict> CheckForConflicts(HotkeyInfo hotkey)
    {
        var conflicts = new List<HotkeyConflict>();
        var shortcut = FormatShortcut(hotkey.Key, hotkey.Modifiers);

        // Check reserved shortcuts
        if (_reservedShortcuts.Contains(shortcut))
        {
            conflicts.Add(new HotkeyConflict
            {
                HotkeyId = hotkey.Id,
                HotkeyName = hotkey.Name,
                Shortcut = shortcut,
                ConflictingApp = "System",
                ConflictType = "system",
                Severity = "blocking"
            });
        }

        // Check for duplicates in our own hotkeys
        HotkeyInfo[] hotkeys;
        lock (_lock)
        {
            hotkeys = _hotkeys.Where(h => h.Id != hotkey.Id).ToArray();
        }

        var duplicate = hotkeys.FirstOrDefault(h =>
            h.Key == hotkey.Key && h.Modifiers == hotkey.Modifiers);

        if (duplicate != null)
        {
            conflicts.Add(new HotkeyConflict
            {
                HotkeyId = hotkey.Id,
                HotkeyName = hotkey.Name,
                Shortcut = shortcut,
                ConflictingApp = "Redball",
                ConflictType = "same_app",
                Severity = "blocking"
            });
        }

        // Check common application conflicts
        var commonConflicts = CheckCommonApplicationConflicts(hotkey, shortcut);
        conflicts.AddRange(commonConflicts);

        return conflicts;
    }

    private List<HotkeyConflict> CheckCommonApplicationConflicts(HotkeyInfo hotkey, string shortcut)
    {
        var conflicts = new List<HotkeyConflict>();

        // Common app shortcuts that might conflict
        var commonAppShortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl+K"] = "Visual Studio Code, Slack, Teams",
            ["Ctrl+T"] = "All Browsers",
            ["Ctrl+W"] = "All Browsers, VS Code",
            ["Ctrl+Tab"] = "All Browsers, Many Apps",
            ["Ctrl+Shift+K"] = "VS Code (delete line)",
            ["F5"] = "All Browsers (refresh)",
            ["Ctrl+R"] = "All Browsers (refresh)"
        };

        if (commonAppShortcuts.TryGetValue(shortcut, out var apps))
        {
            conflicts.Add(new HotkeyConflict
            {
                HotkeyId = hotkey.Id,
                HotkeyName = hotkey.Name,
                Shortcut = shortcut,
                ConflictingApp = apps,
                ConflictType = "other_app",
                Severity = "warning"
            });
        }

        return conflicts;
    }

    private int CalculateConflictScore(Keys key, ModifierKeys modifiers)
    {
        var shortcut = FormatShortcut(key, modifiers);
        var score = 0;

        // Reserved shortcuts have highest conflict
        if (_reservedShortcuts.Contains(shortcut))
            score += 100;

        // Common app shortcuts
        var commonConflicts = new[] { "Ctrl+K", "Ctrl+T", "Ctrl+W", "Ctrl+Tab" };
        if (commonConflicts.Contains(shortcut, StringComparer.OrdinalIgnoreCase))
            score += 50;

        // More modifiers = less conflict
        var modifierCount = 0;
        if ((modifiers & ModifierKeys.Control) != 0) modifierCount++;
        if ((modifiers & ModifierKeys.Shift) != 0) modifierCount++;
        if ((modifiers & ModifierKeys.Alt) != 0) modifierCount++;
        if ((modifiers & ModifierKeys.Windows) != 0) modifierCount++;

        score -= modifierCount * 10;

        return Math.Max(0, score);
    }

    private string FormatShortcut(Keys key, ModifierKeys modifiers)
    {
        var sb = new StringBuilder();

        if ((modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
        if ((modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
        if ((modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");
        if ((modifiers & ModifierKeys.Windows) != 0) sb.Append("Win+");

        sb.Append(key);

        return sb.ToString();
    }

    private (Keys key, ModifierKeys modifiers) ParseShortcut(string shortcut)
    {
        var parts = shortcut.Split('+');
        var modifiers = ModifierKeys.None;
        var keyPart = parts[^1];

        for (int i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= parts[i].ToUpperInvariant() switch
            {
                "CTRL" => ModifierKeys.Control,
                "SHIFT" => ModifierKeys.Shift,
                "ALT" => ModifierKeys.Alt,
                "WIN" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
        }

        if (Enum.TryParse<Keys>(keyPart, true, out var key))
        {
            return (key, modifiers);
        }

        return (Keys.None, modifiers);
    }

    private void InitializeDefaultHotkeys()
    {
        // These would be populated from actual app configuration
        _hotkeys.Add(new HotkeyInfo
        {
            Id = "keepawake_toggle",
            Name = "Toggle Keep-Awake",
            Key = Keys.K,
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
            Action = "ToggleKeepAwake"
        });

        _hotkeys.Add(new HotkeyInfo
        {
            Id = "typething_start",
            Name = "Start TypeThing",
            Key = Keys.T,
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
            Action = "StartTypeThing"
        });

        _hotkeys.Add(new HotkeyInfo
        {
            Id = "typething_stop",
            Name = "Stop TypeThing",
            Key = Keys.T,
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt,
            Action = "StopTypeThing"
        });

        _hotkeys.Add(new HotkeyInfo
        {
            Id = "command_palette",
            Name = "Open Command Palette",
            Key = Keys.K,
            Modifiers = ModifierKeys.Control,
            Action = "ShowCommandPalette"
        });
    }
}

/// <summary>
/// Conflict report summary.
/// </summary>
public class ConflictReportSummary
{
    public int TotalHotkeys { get; set; }
    public int RegisteredHotkeys { get; set; }
    public int BlockingConflicts { get; set; }
    public int WarningConflicts { get; set; }
    public bool CanUseAllHotkeys { get; set; }
    public List<string> ConflictsNeedingResolution { get; set; } = new();
}

/// <summary>
/// Modifier keys enumeration.
/// </summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}
