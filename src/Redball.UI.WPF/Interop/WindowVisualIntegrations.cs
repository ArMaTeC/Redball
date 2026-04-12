using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Redball.UI.Services;

namespace Redball.UI.Interop;

/// <summary>
/// User Interface (3.1): Enforces Windows 11 Mica and Acrylic hardware-accelerated materials natively.
/// Applies the true blurred glass aesthetic over the WPF Hwnd.
/// </summary>
public static class WindowVisualIntegrations
{
    private enum DWMWINDOWATTRIBUTE
    {
        DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        DWMWA_SYSTEMBACKDROP_TYPE = 38
    }

    private enum DWM_SYSTEMBACKDROP_TYPE
    {
        DWMSBT_AUTO = 0,
        DWMSBT_NONE = 1,
        DWMSBT_MAINWINDOW = 2,      // Mica (Windows 11)
        DWMSBT_TRANSIENTWINDOW = 3, // Acrylic (Windows 11)
        DWMSBT_TABBEDWINDOW = 4     // Tabbed Mica
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Applies the 10/10 Native OS material (Mica on Win11) to a given WPF window.
    /// Needs to be called within the OnSourceInitialized event.
    /// </summary>
    public static void ApplyMicaBackdrop(Window window, bool isDarkMode)
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 22000) return; // Only applies to Windows 11+
            
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            
            // Set Dark/Light theme mode for the title bar / backdrop coloring
            int darkThemeValue = isDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkThemeValue, Marshal.SizeOf(typeof(int)));

            // Enforce Mica material as the background layer natively
            int backdropType = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, Marshal.SizeOf(typeof(int)));
            
            Logger.Info("WindowVisualIntegrations", "Windows 11 Mica hardware acceleration successfully applied.");
        }
        catch (Exception ex)
        {
            Logger.Error("WindowVisualIntegrations", "Failed to apply Windows 11 materials.", ex);
        }
    }
}
