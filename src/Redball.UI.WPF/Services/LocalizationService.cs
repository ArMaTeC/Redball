using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Redball.UI.Services;

/// <summary>
/// Provides localized strings for the UI based on the configured locale.
/// Port of Import-RedballLocale, Get-LocalizedString.
/// Supports: en, es, fr, de, bl (blade runner theme).
/// </summary>
public class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    private Dictionary<string, Dictionary<string, string>> _locales = new();
    private string _currentLocale = "en";

    private LocalizationService()
    {
        LoadBuiltInLocales();
        Logger.Verbose("LocalizationService", "Instance created");
    }

    public string CurrentLocale
    {
        get => _currentLocale;
        set
        {
            if (_locales.ContainsKey(value))
            {
                _currentLocale = value;
                Logger.Info("LocalizationService", $"Locale set to: {value}");
            }
            else
            {
                Logger.Warning("LocalizationService", $"Unknown locale '{value}', keeping '{_currentLocale}'");
            }
        }
    }

    public IReadOnlyCollection<string> AvailableLocales => _locales.Keys;

    /// <summary>
    /// Gets a localized string by key. Falls back to English, then to the key itself.
    /// </summary>
    public string GetString(string key, string? locale = null)
    {
        var loc = locale ?? _currentLocale;

        if (_locales.TryGetValue(loc, out var strings) && strings.TryGetValue(key, out var value))
            return value;

        if (loc != "en" && _locales.TryGetValue("en", out var enStrings) && enStrings.TryGetValue(key, out var enValue))
            return enValue;

        return key;
    }

    /// <summary>
    /// Loads additional locales from an external locales.json file.
    /// </summary>
    public void LoadFromFile(string? path = null)
    {
        var filePath = path ?? ResolveLocalesPath();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Logger.Debug("LocalizationService", "No external locales file found");
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var external = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (external != null)
            {
                foreach (var (locale, strings) in external)
                {
                    if (_locales.ContainsKey(locale))
                    {
                        foreach (var (key, val) in strings)
                            _locales[locale][key] = val;
                    }
                    else
                    {
                        _locales[locale] = strings;
                    }
                }
                Logger.Info("LocalizationService", $"Loaded external locales from: {filePath}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("LocalizationService", $"Failed to load external locales: {ex.Message}");
        }
    }

    /// <summary>
    /// Auto-detect system locale and set it if available.
    /// </summary>
    public void AutoDetect()
    {
        try
        {
            var systemLocale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (_locales.ContainsKey(systemLocale))
            {
                CurrentLocale = systemLocale;
                Logger.Info("LocalizationService", $"Auto-detected locale: {systemLocale}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("LocalizationService", $"Auto-detect failed: {ex.Message}");
        }
    }

    private void LoadBuiltInLocales()
    {
        _locales["en"] = new Dictionary<string, string>
        {
            ["app.name"] = "Redball",
            ["status.active"] = "Active",
            ["status.paused"] = "Paused",
            ["status.display_on"] = "Display On",
            ["status.display_normal"] = "Display Normal",
            ["status.f15_on"] = "F15 On",
            ["status.f15_off"] = "F15 Off",
            ["menu.toggle"] = "Toggle Keep-Awake",
            ["menu.settings"] = "Settings",
            ["menu.about"] = "About",
            ["menu.exit"] = "Exit",
            ["menu.typething"] = "TypeThing",
            ["notify.activated"] = "Keep-awake activated",
            ["notify.deactivated"] = "Keep-awake deactivated",
            ["notify.battery_pause"] = "Battery below {0}%. Keep-awake paused.",
            ["notify.battery_resume"] = "Power restored. Keep-awake resumed.",
            ["notify.network_pause"] = "Network disconnected. Keep-awake paused.",
            ["notify.network_resume"] = "Network reconnected. Keep-awake resumed.",
            ["notify.idle_pause"] = "User idle for {0} minutes. Keep-awake paused.",
            ["notify.idle_resume"] = "User activity detected. Keep-awake resumed.",
            ["notify.schedule_start"] = "Keep-awake started per schedule.",
            ["notify.schedule_stop"] = "Keep-awake stopped per schedule.",
            ["notify.timed_expired"] = "Timed keep-awake expired.",
            ["notify.crash_recovery"] = "Recovered from previous crash. Using safe defaults.",
            ["typething.clipboard_empty"] = "Clipboard is empty. Copy some text first.",
            ["typething.typing_start"] = "Typing {0} characters in {1} seconds...",
            ["typething.typing_complete"] = "Done! Typed {0} characters.",
            ["typething.typing_stopped"] = "Typing stopped ({0}/{1} characters).",
            ["confirm.exit"] = "Redball is currently keeping your system awake. Are you sure you want to exit?",
            ["confirm.exit_title"] = "Confirm Exit",
        };

        _locales["es"] = new Dictionary<string, string>
        {
            ["app.name"] = "Redball",
            ["status.active"] = "Activo",
            ["status.paused"] = "Pausado",
            ["menu.toggle"] = "Alternar mantener activo",
            ["menu.settings"] = "Configuraci\u00f3n",
            ["menu.about"] = "Acerca de",
            ["menu.exit"] = "Salir",
            ["notify.activated"] = "Mantener activo activado",
            ["notify.deactivated"] = "Mantener activo desactivado",
        };

        _locales["fr"] = new Dictionary<string, string>
        {
            ["app.name"] = "Redball",
            ["status.active"] = "Actif",
            ["status.paused"] = "En pause",
            ["menu.toggle"] = "Basculer le maintien en \u00e9veil",
            ["menu.settings"] = "Param\u00e8tres",
            ["menu.about"] = "\u00c0 propos",
            ["menu.exit"] = "Quitter",
            ["notify.activated"] = "Maintien en \u00e9veil activ\u00e9",
            ["notify.deactivated"] = "Maintien en \u00e9veil d\u00e9sactiv\u00e9",
        };

        _locales["de"] = new Dictionary<string, string>
        {
            ["app.name"] = "Redball",
            ["status.active"] = "Aktiv",
            ["status.paused"] = "Pausiert",
            ["menu.toggle"] = "Wach-Modus umschalten",
            ["menu.settings"] = "Einstellungen",
            ["menu.about"] = "\u00dcber",
            ["menu.exit"] = "Beenden",
            ["notify.activated"] = "Wach-Modus aktiviert",
            ["notify.deactivated"] = "Wach-Modus deaktiviert",
        };

        _locales["bl"] = new Dictionary<string, string>
        {
            ["app.name"] = "Redball",
            ["status.active"] = "Baseline: Active",
            ["status.paused"] = "Retired",
            ["menu.toggle"] = "Toggle Replicant Mode",
            ["menu.settings"] = "Voight-Kampff Settings",
            ["menu.about"] = "Nexus Info",
            ["menu.exit"] = "Time to die",
            ["notify.activated"] = "All those moments will be lost in time, like tears in rain. System awake.",
            ["notify.deactivated"] = "Replicant retired. System may sleep.",
        };
    }

    private static string? ResolveLocalesPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "locales.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "locales.json"),
            Path.Combine(Environment.CurrentDirectory, "locales.json"),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full)) return full;
            }
            catch { }
        }

        return null;
    }
}
