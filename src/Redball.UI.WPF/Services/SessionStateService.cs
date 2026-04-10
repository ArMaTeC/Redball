using Redball.Core.Security;
using System;
using System.IO;
using System.Text.Json;

namespace Redball.UI.Services;

/// <summary>
/// Saves and restores session state across application restarts.
/// Port of Save-RedballState, Restore-RedballState.
/// </summary>
public class SessionStateService : ISessionStateService
{
    private static readonly string DefaultStatePath = Path.Combine(
        AppContext.BaseDirectory, "Redball.state.json");

    /// <summary>
    /// Saves current session state to disk.
    /// </summary>
    public bool Save(KeepAwakeService keepAwake, string? path = null)
    {
        var statePath = path ?? ResolveStatePath();

        // SECURITY: Validate state path to prevent path traversal if custom path provided
        if (path != null && !SecurePathValidator.IsValidFilePath(statePath))
        {
            Logger.Error("SessionState", $"Invalid state file path: {statePath}");
            return false;
        }

        Logger.Debug("SessionState", $"Saving state to: {statePath}");

        try
        {
            var state = new SessionState
            {
                Active = keepAwake.IsActive,
                PreventDisplaySleep = keepAwake.PreventDisplaySleep,
                UseHeartbeat = keepAwake.UseHeartbeat,
                Until = keepAwake.Until,
                SavedAt = DateTime.Now
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });

            var dir = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(statePath, json);
            Logger.Info("SessionState", "State saved successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SessionState", "Failed to save state", ex);
            return false;
        }
    }

    /// <summary>
    /// Restores session state from disk and applies it to the keep-awake service.
    /// Deletes the state file after successful restore.
    /// </summary>
    public bool Restore(KeepAwakeService keepAwake, string? path = null)
    {
        var statePath = path ?? ResolveStatePath();

        // SECURITY: Validate state path to prevent path traversal if custom path provided
        if (path != null && !SecurePathValidator.IsValidFilePath(statePath))
        {
            Logger.Error("SessionState", $"Invalid state file path: {statePath}");
            return false;
        }

        if (!File.Exists(statePath))
        {
            Logger.Debug("SessionState", "No saved state file found");
            return false;
        }

        try
        {
            var json = File.ReadAllText(statePath);
            // SECURITY: Use SecureJsonSerializer with size limit and max depth
            var state = SecureJsonSerializer.Deserialize<SessionState>(json);

            if (state == null)
            {
                Logger.Warning("SessionState", "State file was empty or invalid");
                return false;
            }

            Logger.Info("SessionState", $"Restoring state: Active={state.Active}, Display={state.PreventDisplaySleep}, Heartbeat={state.UseHeartbeat}, SavedAt={state.SavedAt}");

            keepAwake.PreventDisplaySleep = state.PreventDisplaySleep;
            keepAwake.UseHeartbeat = state.UseHeartbeat;

            // Only restore timed awake if it hasn't expired
            DateTime? until = null;
            if (state.Until.HasValue && state.Until.Value > DateTime.Now)
            {
                until = state.Until.Value;
                Logger.Info("SessionState", $"Restoring timed awake until {until}");
            }

            keepAwake.SetActive(state.Active, until);

            // Delete state file after successful restore
            try
            {
                File.Delete(statePath);
                Logger.Debug("SessionState", "State file deleted after restore");
            }
            catch (Exception ex)
            {
                Logger.Debug("SessionState", $"Failed to delete state file: {ex.Message}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("SessionState", "Failed to restore state", ex);
            return false;
        }
    }

    private static string ResolveStatePath()
    {
        // Try multiple locations
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Redball.state.json"),
            Path.Combine(Environment.CurrentDirectory, "Redball.state.json"),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(Path.GetFullPath(candidate)))
                    return Path.GetFullPath(candidate);
            }
            catch (Exception ex)
            {
                Logger.Debug("SessionState", $"Failed to resolve state path {candidate}: {ex.Message}");
            }
        }

        return DefaultStatePath;
    }
}

public class SessionState
{
    public bool Active { get; set; }
    public bool PreventDisplaySleep { get; set; } = true;
    public bool UseHeartbeat { get; set; } = true;
    public DateTime? Until { get; set; }
    public DateTime SavedAt { get; set; }
}
