namespace Redball.SessionHelper;

using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>
/// Helper process that runs inside a specific session (via CreateProcessAsUser from the service)
/// to inject input into that session's desktop. This is necessary because services cannot
/// directly access window stations/desktops in other sessions.
/// </summary>
class Program
{
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: Redball.SessionHelper <base64-encoded-json>");
            return;
        }

        try
        {
            // SECURITY: Decode Base64 argument to prevent command injection
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(args[0]));
            var request = JsonSerializer.Deserialize<InjectionRequest>(json);

            if (request == null)
            {
                Console.Error.WriteLine("Invalid request data");
                Environment.Exit(1);
            }

            bool success = request.Type switch
            {
                "keyboard" => InjectKeyboard(request),
                "mouse" => InjectMouse(request),
                _ => false
            };

            Environment.Exit(success ? 0 : 1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    static bool InjectKeyboard(InjectionRequest request)
    {
        if (request.KeyCode == null) return false;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = request.KeyCode.Value,
                wScan = (ushort)(request.ScanCode ?? 0),
                dwFlags = (request.KeyUp ? KEYEVENTF_KEYUP : 0) | (request.Extended ? KEYEVENTF_EXTENDEDKEY : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        // SECURITY: Use validated SendInput with buffer size check
        var result = SendInputSafe(new[] { input });
        return result == 1;
    }

    static bool InjectMouse(InjectionRequest request)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = request.X,
                dy = request.Y,
                mouseData = request.MouseData,
                dwFlags = request.Flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        // SECURITY: Use validated SendInput with buffer size check
        var result = SendInputSafe(new[] { input });
        return result == 1;
    }

    #region P/Invoke

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    // SECURITY: Buffer-validated wrapper for SendInput
    private static uint SendInputSafe(INPUT[] inputs)
    {
        if (inputs == null)
            throw new ArgumentNullException(nameof(inputs));
        if (inputs.Length == 0)
            return 0;
        if (inputs.Length > 1000)
            throw new ArgumentException("Input array too large", nameof(inputs));

        int cbSize = Marshal.SizeOf<INPUT>();
        return SendInput((uint)inputs.Length, inputs, cbSize);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private class InjectionRequest
    {
        public string Type { get; set; } = string.Empty;
        public ushort? KeyCode { get; set; }
        public ushort? ScanCode { get; set; }
        public bool KeyUp { get; set; }
        public bool Extended { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public uint MouseData { get; set; }
        public uint Flags { get; set; }
    }

    #endregion
}
