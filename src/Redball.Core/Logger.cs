namespace Redball.Core;

using System;
using System.Diagnostics;
using System.IO;

/// <summary>
/// Simple internal logger for Redball.Core assembly.
/// Mirrors the WPF Logger API for compatibility.
/// </summary>
internal static class Logger
{
    private static readonly string LogFilePath;
    private static readonly object LockObj = new();

    static Logger()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDir = Path.Combine(localAppData, "Redball", "UserData", "Logs");
        Directory.CreateDirectory(logDir);
        LogFilePath = Path.Combine(logDir, $"core_{DateTime.Now:yyyyMMdd}.log");
    }

    public static void Info(string component, string message)
    {
        WriteLog("INFO", component, message);
    }

    public static void Error(string component, string message, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message} - {ex.GetType().Name}: {ex.Message}" : message;
        WriteLog("ERROR", component, fullMessage);
    }

    public static void Warn(string component, string message)
    {
        WriteLog("WARN", component, message);
    }

    public static void Warning(string component, string message)
    {
        Warn(component, message);
    }

    public static void Debug(string component, string message)
    {
#if DEBUG
        WriteLog("DEBUG", component, message);
#endif
    }

    private static void WriteLog(string level, string component, string message)
    {
        var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{component}] {message}";
        
        lock (LockObj)
        {
            try
            {
                File.AppendAllText(LogFilePath, entry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Silent fail - logging should not crash the app
                System.Diagnostics.Debug.WriteLine($"[Logger] Failed to write to log file: {ex.Message}");
            }
        }

        System.Diagnostics.Debug.WriteLine(entry);
    }
}
