using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Redball.UI.Interop;

/// <summary>
/// Centralized P/Invoke declarations for Win32 API calls used throughout Redball.
/// SECURITY: All methods in this class are SecurityCritical as they call native code.
/// These are required for Windows system integration (power management, input injection).
/// </summary>
[SecurityCritical]
internal static partial class NativeMethods
{
    // --- Power Management (kernel32.dll) ---

    // codeql[cs/dll-import-of-unmanaged-code] Required for Windows power management APIs
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint SetThreadExecutionState(uint esFlags);

    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;
    public const uint ES_AWAYMODE_REQUIRED = 0x00000040;

    // --- Keyboard Input (user32.dll) ---

    // codeql[cs/dll-import-of-unmanaged-code] Required for keep-awake functionality via simulated input
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>
    /// SECURITY: Wrapper for SendInput with buffer size validation.
    /// Validates input array before calling native method to prevent buffer issues.
    /// </summary>
    /// <param name="inputs">Array of INPUT structures (max 1000 elements for safety)</param>
    /// <returns>Number of events successfully inserted</returns>
    public static uint SendInputSafe(INPUT[] inputs)
    {
        // SECURITY: Validate input array
        if (inputs == null)
            throw new ArgumentNullException(nameof(inputs));

        if (inputs.Length == 0)
            return 0;

        // SECURITY: Limit maximum inputs to prevent potential abuse (1000 is generous)
        const int MaxInputs = 1000;
        if (inputs.Length > MaxInputs)
            throw new ArgumentException($"Input array exceeds maximum of {MaxInputs} elements", nameof(inputs));

        // SECURITY: Validate cbSize matches expected struct size (28 bytes for INPUT on x64)
        int cbSize = Marshal.SizeOf<INPUT>();
        if (cbSize <= 0 || cbSize > 64)
            throw new InvalidOperationException($"Unexpected INPUT struct size: {cbSize}");

        return SendInput((uint)inputs.Length, inputs, cbSize);
    }

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

    // codeql[cs/dll-import-of-unmanaged-code] Required for Windows Focus Assist detection via WNF state
    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryWnfStateData(
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NativeMethods: Failed to get Focus Assist status: {ex.Message}");
            return -1;
        }
    }

    // --- Idle Detection (user32.dll) ---

    // codeql[cs/dll-import-of-unmanaged-code] Required for user idle time detection
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

    // codeql[cs/dll-import-of-unmanaged-code] Required for admin privilege check
    [LibraryImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsUserAnAdmin();

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    // --- DWM Theming (dwmapi.dll) ---

    // codeql[cs/dll-import-of-unmanaged-code] Required for Windows theming (Mica/Acrylic)
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int attrSize);

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

    // --- Window Styles and Property Management (user32.dll) ---

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_TRANSPARENT = 0x20;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    /// <summary>
    /// Wrapper for GetWindowLongPtr that handles 32-bit and 64-bit compatibility.
    /// </summary>
    public static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
    {
        // For win-x64, we always use GetWindowLongPtr
        return GetWindowLongPtr(hWnd, nIndex);
    }

    /// <summary>
    /// Wrapper for SetWindowLongPtr that handles 32-bit and 64-bit compatibility.
    /// </summary>
    public static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        // For win-x64, we always use SetWindowLongPtr
        return SetWindowLongPtr(hWnd, nIndex, dwNewLong);
    }

    // --- Code Integrity / Test Signing (ntdll.dll) ---

    // codeql[cs/dll-import-of-unmanaged-code] Required for detecting Windows Test Mode
    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength,
        out int ReturnLength);

    private const int SystemCodeIntegrityInformation = 103;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_CODEINTEGRITY_INFORMATION
    {
        public uint Length;
        public uint CodeIntegrityOptions;
    }

    private const uint CODEINTEGRITY_OPTION_TESTSIGNING = 0x00000002;

    /// <summary>
    /// Checks if Windows Test Signing (Test Mode) is enabled.
    /// </summary>
    public static bool IsTestModeEnabled()
    {
        try
        {
            var info = new SYSTEM_CODEINTEGRITY_INFORMATION
            {
                Length = (uint)Marshal.SizeOf(typeof(SYSTEM_CODEINTEGRITY_INFORMATION))
            };

            var ptr = Marshal.AllocHGlobal((int)info.Length);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                var result = NtQuerySystemInformation(SystemCodeIntegrityInformation, ptr, (int)info.Length, out _);
                if (result == 0) // STATUS_SUCCESS
                {
                    info = Marshal.PtrToStructure<SYSTEM_CODEINTEGRITY_INFORMATION>(ptr);
                    return (info.CodeIntegrityOptions & CODEINTEGRITY_OPTION_TESTSIGNING) != 0;
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }

            // Fallback to registry if ntdll fails or is restricted
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            var options = key?.GetValue("SystemStartOptions") as string;
            return options?.Contains("TESTSIGNING", StringComparison.OrdinalIgnoreCase) ?? false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NativeMethods: Failed to check test mode: {ex.Message}");
            return false;
        }
    }

    // --- Memory Management (psapi.dll) ---

    // codeql[cs/dll-import-of-unmanaged-code] Required for memory optimisation
    [LibraryImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyWorkingSet(IntPtr hProcess);

    // --- Native window dragging (user32.dll) ---

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    /// <summary>
    /// Initiates a native OS-level window drag via WM_NCLBUTTONDOWN/HTCAPTION.
    /// Uses PostMessage (non-blocking) so the UI thread is not held during the drag.
    /// </summary>
    public static void BeginNativeDrag(IntPtr hwnd)
    {
        ReleaseCapture();
        PostMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
    }
}
