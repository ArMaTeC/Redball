using System;
using System.IO;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Manages Windows startup registration via the Registry Run key.
/// Port of Install-RedballStartup, Uninstall-RedballStartup, Test-RedballStartup.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Redball";

    /// <summary>
    /// Returns true if Redball is registered to start with Windows.
    /// </summary>
    public static bool IsInstalledAtStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(ValueName);
            return value != null;
        }
        catch (Exception ex)
        {
            Logger.Debug("StartupService", $"Startup check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Registers Redball to start with Windows.
    /// </summary>
    public static bool Install()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "Redball.UI.WPF.exe");
            }

            if (!File.Exists(exePath))
            {
                Logger.Warning("StartupService", $"Executable not found: {exePath}");
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                Logger.Warning("StartupService", "Could not open Run key for writing");
                return false;
            }

            key.SetValue(ValueName, $"\"{exePath}\"");
            Logger.Info("StartupService", $"Installed to startup: {exePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("StartupService", "Failed to install startup entry", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes Redball from Windows startup.
    /// </summary>
    public static bool Uninstall()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return false;

            if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName);
                Logger.Info("StartupService", "Removed from startup");
                return true;
            }

            Logger.Debug("StartupService", "Not in startup, nothing to remove");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("StartupService", "Failed to remove startup entry", ex);
            return false;
        }
    }
}
