using System.Collections.Generic;

namespace Redball.UI.Services;

/// <summary>
/// Interface for localization string management.
/// </summary>
public interface ILocalizationService
{
    string CurrentLocale { get; set; }
    IReadOnlyCollection<string> AvailableLocales { get; }
    string GetString(string key, string? locale = null);
    void LoadFromFile(string? path = null);
    void AutoDetect();
}
