using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Redball.UI.Services;

/// <summary>
/// Manages named configuration profiles (e.g., "Work", "Home", "Presentation").
/// Profiles are stored as JSON files in the profiles subdirectory.
/// </summary>
public class ProfileService
{
    private static readonly Lazy<ProfileService> _instance = new(() => new ProfileService());
    public static ProfileService Instance => _instance.Value;

    private readonly string _profilesDir;

    public string ActiveProfileName { get; private set; } = "";

    public event EventHandler? ProfileChanged;

    private ProfileService()
    {
        _profilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArMaTeC", "Redball", "Profiles");

        if (!Directory.Exists(_profilesDir))
            Directory.CreateDirectory(_profilesDir);

        MigrateLegacyProfiles();

        Logger.Verbose("ProfileService", $"Profiles directory: {_profilesDir}");
    }

    public List<string> GetProfileNames()
    {
        try
        {
            return Directory.GetFiles(_profilesDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderBy(n => n)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("ProfileService", "Failed to list profiles", ex);
            return new List<string>();
        }
    }

    public bool SaveProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            var sanitized = SanitizeFileName(name);
            var path = Path.Combine(_profilesDir, sanitized + ".json");
            var json = JsonSerializer.Serialize(ConfigService.Instance.Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            ActiveProfileName = name;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
            Logger.Info("ProfileService", $"Profile saved: {name}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ProfileService", $"Failed to save profile: {name}", ex);
            return false;
        }
    }

    public bool LoadProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            var sanitized = SanitizeFileName(name);
            var path = Path.Combine(_profilesDir, sanitized + ".json");
            if (!File.Exists(path))
            {
                Logger.Error("ProfileService", $"Profile not found: {name}");
                return false;
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<RedballConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (config == null) return false;

            ConfigService.Instance.Config = config;
            ConfigService.Instance.Save();
            ActiveProfileName = name;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
            Logger.Info("ProfileService", $"Profile loaded: {name}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ProfileService", $"Failed to load profile: {name}", ex);
            return false;
        }
    }

    public bool DeleteProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            var sanitized = SanitizeFileName(name);
            var path = Path.Combine(_profilesDir, sanitized + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
                if (ActiveProfileName == name) ActiveProfileName = "";
                ProfileChanged?.Invoke(this, EventArgs.Empty);
                Logger.Info("ProfileService", $"Profile deleted: {name}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("ProfileService", $"Failed to delete profile: {name}", ex);
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private void MigrateLegacyProfiles()
    {
        try
        {
            var legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Redball", "Profiles");

            if (Directory.Exists(legacyDir))
            {
                var files = Directory.GetFiles(legacyDir, "*.json");
                foreach (var file in files)
                {
                    var dest = Path.Combine(_profilesDir, Path.GetFileName(file));
                    if (!File.Exists(dest))
                    {
                        File.Move(file, dest);
                        Logger.Info("ProfileService", $"Migrated legacy profile: {Path.GetFileName(file)}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("ProfileService", $"Legacy profile migration skipped: {ex.Message}");
        }
    }
}
