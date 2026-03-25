namespace Redball.Service;

using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>
/// Handles input injection across sessions, including RDP.
/// Uses SendInput for local session and WTSSendMessage for remote session notification.
/// </summary>
public class InputInjectionEngine
{
    private readonly ILogger<InputInjectionEngine> _logger;
    private bool _initialized;

    public InputInjectionEngine(ILogger<InputInjectionEngine> logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        if (_initialized) return;

        _logger.LogInformation("Initializing input injection engine");
        _initialized = true;
    }

    public void Shutdown()
    {
        _initialized = false;
        _logger.LogInformation("Input injection engine shutdown");
    }

    /// <summary>
    /// Injects keyboard input into a specific session.
    /// </summary>
    public bool InjectKeyboardInput(uint sessionId, ushort keyCode, bool keyUp, bool extended)
    {
        if (!_initialized)
        {
            _logger.LogWarning("Engine not initialized");
            return false;
        }

        try
        {
            var currentSession = GetCurrentSessionId();

            if (sessionId == currentSession || sessionId == 0)
            {
                // Local session - use SendInput directly
                return SendLocalKeyboardInput(keyCode, keyUp, extended);
            }
            else
            {
                // Remote session - need to impersonate and inject
                return SendRemoteKeyboardInput(sessionId, keyCode, keyUp, extended);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inject keyboard input for session {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Injects mouse input into a specific session.
    /// </summary>
    public bool InjectMouseInput(uint sessionId, MouseInputData input)
    {
        if (!_initialized)
        {
            _logger.LogWarning("Engine not initialized");
            return false;
        }

        try
        {
            var currentSession = GetCurrentSessionId();

            if (sessionId == currentSession || sessionId == 0)
            {
                return SendLocalMouseInput(input);
            }
            else
            {
                return SendRemoteMouseInput(sessionId, input);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inject mouse input for session {SessionId}", sessionId);
            return false;
        }
    }

    private bool SendLocalKeyboardInput(ushort keyCode, bool keyUp, bool extended)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = keyCode,
                wScan = 0,
                dwFlags = (keyUp ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        var result = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (result == 0)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("SendInput failed with error {Error}", error);
            return false;
        }

        return true;
    }

    private bool SendLocalMouseInput(MouseInputData data)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = data.X,
                dy = data.Y,
                mouseData = data.MouseData,
                dwFlags = data.Flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        var result = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        return result != 0;
    }

    private bool SendRemoteKeyboardInput(uint targetSessionId, ushort keyCode, bool keyUp, bool extended)
    {
        _logger.LogDebug("Launching helper for remote keyboard injection in session {SessionId}", targetSessionId);

        // For RDP sessions, we need to use the session helper via CreateProcessAsUser
        var request = new
        {
            Type = "keyboard",
            KeyCode = keyCode,
            KeyUp = keyUp,
            Extended = extended
        };

        return LaunchHelperInSession(targetSessionId, request);
    }

    private bool SendRemoteMouseInput(uint targetSessionId, MouseInputData input)
    {
        _logger.LogDebug("Launching helper for remote mouse injection in session {SessionId}", targetSessionId);

        var request = new
        {
            Type = "mouse",
            X = input.X,
            Y = input.Y,
            MouseData = input.MouseData,
            Flags = input.Flags
        };

        return LaunchHelperInSession(targetSessionId, request);
    }

    private bool LaunchHelperInSession(uint sessionId, object request)
    {
        var hToken = IntPtr.Zero;
        if (!WTSQueryUserToken(sessionId, out hToken))
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("WTSQueryUserToken failed for session {SessionId}: {Error}", sessionId, error);
            return false;
        }

        try
        {
            var helperPath = GetHelperPath();
            var json = JsonSerializer.Serialize(request);

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf<STARTUPINFO>();

            var pi = new PROCESS_INFORMATION();

            // Get user environment for the session
            IntPtr env = IntPtr.Zero;
            if (!CreateEnvironmentBlock(out env, hToken, false))
            {
                _logger.LogWarning("Failed to create environment block");
            }

            try
            {
                bool created = CreateProcessAsUser(
                    hToken,
                    helperPath,
                    $"\"{helperPath}\" \"{json.Replace("\"", "\\\"")}\"",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT,
                    env,
                    null,
                    ref si,
                    out pi);

                if (!created)
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger.LogError("CreateProcessAsUser failed: {Error}", error);
                    return false;
                }

                try
                {
                    // Resume and wait for completion
                    ResumeThread(pi.hThread);

                    // Wait up to 5 seconds for helper to complete
                    using var process = Process.GetProcessById((int)pi.dwProcessId);
                    if (!process.WaitForExit(5000))
                    {
                        _logger.LogWarning("Session helper timed out");
                        try { process.Kill(); } catch { }
                        return false;
                    }

                    return process.ExitCode == 0;
                }
                finally
                {
                    CloseHandle(pi.hProcess);
                    CloseHandle(pi.hThread);
                }
            }
            finally
            {
                if (env != IntPtr.Zero)
                    DestroyEnvironmentBlock(env);
            }
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    private static string GetHelperPath()
    {
        // Look for helper next to service executable
        var serviceDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var helperPath = Path.Combine(serviceDir ?? AppContext.BaseDirectory, "Redball.SessionHelper.exe");

        if (!File.Exists(helperPath))
        {
            // Fallback to sibling directory
            var parentDir = Directory.GetParent(serviceDir ?? AppContext.BaseDirectory)?.Parent?.FullName;
            if (parentDir != null)
            {
                helperPath = Path.Combine(parentDir, "Redball.SessionHelper", "Redball.SessionHelper.exe");
            }
        }

        return helperPath;
    }

    private static uint GetCurrentSessionId()
    {
        return (uint)Process.GetCurrentProcess().SessionId;
    }

    #region P/Invoke

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    private static extern uint ResumeThread(IntPtr hThread);

    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

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

    public struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
    }

    #endregion
}
