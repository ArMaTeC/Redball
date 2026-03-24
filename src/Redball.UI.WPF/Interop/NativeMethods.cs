using System;
using System.Runtime.InteropServices;

namespace Redball.UI.Interop;

/// <summary>
/// Centralized P/Invoke declarations for Win32 API calls used throughout Redball.
/// </summary>
internal static class NativeMethods
{
    // --- Power Management (kernel32.dll) ---

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint esFlags);

    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;
    public const uint ES_AWAYMODE_REQUIRED = 0x00000040;

    // --- Keyboard Input (user32.dll) ---

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const int INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    public const ushort VK_F13 = 0x7C;
    public const ushort VK_F14 = 0x7D;
    // VK_F15 = 0x7E (invisible key used for heartbeat)
    public const ushort VK_F15 = 0x7E;
    public const ushort VK_F16 = 0x7F;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // --- Focus Assist / Do Not Disturb Detection ---

    [DllImport("ntdll.dll")]
    public static extern int NtQueryWnfStateData(
        ref ulong stateName,
        IntPtr typeId,
        IntPtr explicitScope,
        out uint changeStamp,
        byte[] buffer,
        ref uint bufferSize);

    // WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED state name
    // This WNF state indicates Focus Assist status on Windows 10/11
    public static readonly ulong WNF_SHEL_QUIET_MOMENT_SHELL_MODE_CHANGED = 0xD83063EA3BF1C75;

    /// <summary>
    /// Returns the Focus Assist profile: 0 = Off, 1 = Priority Only, 2 = Alarms Only
    /// Returns -1 on failure.
    /// </summary>
    public static int GetFocusAssistStatus()
    {
        try
        {
            var stateName = WNF_SHEL_QUIET_MOMENT_SHELL_MODE_CHANGED;
            var buffer = new byte[4];
            uint bufferSize = 4;
            var result = NtQueryWnfStateData(ref stateName, IntPtr.Zero, IntPtr.Zero, out _, buffer, ref bufferSize);
            if (result == 0 && bufferSize >= 4)
                return BitConverter.ToInt32(buffer, 0);
            return -1;
        }
        catch
        {
            return -1;
        }
    }

    // --- Idle Detection (user32.dll) ---

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    // --- DWM Theming (dwmapi.dll) ---

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int attrSize);

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public enum DWM_SYSTEMBACKDROP_TYPE
    {
        DWMSBT_AUTO = 0,
        DWMSBT_NONE = 1,
        DWMSBT_MAINWINDOW = 2, // Mica
        DWMSBT_TRANSIENTWINDOW = 3, // Acrylic
        DWMSBT_TABBEDWINDOW = 4 // Mica Alt
    }
}
