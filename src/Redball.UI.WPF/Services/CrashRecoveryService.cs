using System;
using System.IO;

namespace Redball.UI.Services;

/// <summary>
/// Manages crash detection and recovery using a flag file.
/// Port of Test-CrashRecovery, Clear-CrashFlag.
/// </summary>
public static class CrashRecoveryService
{
    private static readonly string CrashFlagPath = Path.Combine(
        AppContext.BaseDirectory, "Redball.crash.flag");

    /// <summary>
    /// Checks if a crash flag exists from a previous session.
    /// Returns true if a crash was detected (previous session did not exit cleanly).
    /// </summary>
    public static bool WasPreviousCrash()
    {
        try
        {
            return File.Exists(CrashFlagPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets the crash flag. Call on startup after singleton check.
    /// </summary>
    public static void SetCrashFlag()
    {
        try
        {
            File.WriteAllText(CrashFlagPath, "Running");
            Logger.Debug("CrashRecovery", "Crash flag set");
        }
        catch (Exception ex)
        {
            Logger.Debug("CrashRecovery", $"Could not set crash flag: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the crash flag. Call on clean exit.
    /// </summary>
    public static void ClearCrashFlag()
    {
        try
        {
            if (File.Exists(CrashFlagPath))
            {
                File.Delete(CrashFlagPath);
                Logger.Debug("CrashRecovery", "Crash flag cleared");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("CrashRecovery", $"Crash flag cleanup skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks for crash recovery and resets to safe defaults if needed.
    /// Returns true if a crash was recovered from.
    /// </summary>
    public static bool CheckAndRecover()
    {
        if (!WasPreviousCrash()) return false;

        Logger.Warning("CrashRecovery", "Previous abnormal termination detected. Resetting to safe defaults.");

        // Set the new crash flag for this session
        SetCrashFlag();

        return true;
    }
}
