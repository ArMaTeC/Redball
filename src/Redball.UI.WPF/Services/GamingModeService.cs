using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms; // Requires Reference in .csproj if not already there, but we can stick to native calls

namespace Redball.UI.Services;

/// <summary>
/// Detects if the user is playing a full-screen game and triggers
/// performance optimizations (reducing CPU/memory footprint).
/// </summary>
public class GamingModeService
{
    private static readonly Lazy<GamingModeService> _instance = new(() => new GamingModeService());
    public static GamingModeService Instance => _instance.Value;

    private bool _isGaming;
    public bool IsGaming => _isGaming;

    public event EventHandler<bool>? GamingStateChanged;

    private GamingModeService() { }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwnd);

    /// <summary>
    /// Checks if a full-screen window is in focus.
    /// Simplified: matches primary screen resolution and is not the desktop shell.
    /// </summary>
    public void CheckAndUpdate()
    {
        if (!ConfigService.Instance.Config.GamingModeEnabled)
        {
            if (_isGaming) SetGamingState(false);
            return;
        }

        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == GetDesktopWindow() || foreground == GetShellWindow())
        {
            if (_isGaming) SetGamingState(false);
            return;
        }

        if (GetWindowRect(foreground, out RECT rect))
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            // Basic check: is the window as big as the current monitor?
            // On multi-monitor, this works for the primary monitor
            bool isFullScreen = width >= SystemParameters.PrimaryScreenWidth && 
                                height >= SystemParameters.PrimaryScreenHeight;

            if (isFullScreen != _isGaming)
            {
                SetGamingState(isFullScreen);
            }
        }
    }

    private void SetGamingState(bool gaming)
    {
        _isGaming = gaming;
        Logger.Info("GamingModeService", $"Gaming mode: {(gaming ? "ENABLED" : "DISABLED")}");
        
        if (gaming)
        {
            OptimizeFootprint();
        }

        GamingStateChanged?.Invoke(this, gaming);
    }

    /// <summary>
    /// Trims the process memory footprint.
    /// </summary>
    public void OptimizeFootprint()
    {
        try
        {
            Logger.Verbose("GamingModeService", "Minimizing memory footprint...");
            
            // Collect GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Explicitly trim the working set (P/Invoke EmptyWorkingSet)
            // Process.GetCurrentProcess().WorkingSet64 would still show "total", 
            // but this hints Windows to swap out unused pages.
            var handle = System.Diagnostics.Process.GetCurrentProcess().Handle;
            EmptyWorkingSet(handle);
            
            Logger.Info("GamingModeService", "Process memory footprint trimmed.");
        }
        catch (Exception ex)
        {
            Logger.Debug("GamingModeService", $"Optimization failed: {ex.Message}");
        }
    }
}
