namespace Redball.Service;

using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;

/// <summary>
/// Handles input injection across sessions, including RDP.
/// Uses SendInput for local session and WTSSendMessage for remote session notification.
/// </summary>
public partial class InputInjectionEngine
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

        Log.Initializing(_logger);
        _initialized = true;
    }

    public void Shutdown()
    {
        _initialized = false;
        Log.Shutdown(_logger);
    }

    /// <summary>
    /// Injects keyboard input into a specific session.
    /// </summary>
    public bool InjectKeyboardInput(uint sessionId, ushort keyCode, bool keyUp, bool extended)
    {
        if (!_initialized)
        {
            Log.EngineNotInitialized(_logger);
            return false;
        }

        try
        {
            var currentSession = GetCurrentSessionId();

            // CRITICAL: Services running in Session 0 cannot use SendInput to inject
            // into user sessions due to Windows session isolation. Always use the
            // remote injection path (helper process) when in Session 0.
            if (currentSession == 0)
            {
                // Service is in Session 0 - must use helper process for any user session
                var targetSession = sessionId == 0 ? GetActiveUserSessionId() : sessionId;
                Log.UsingSessionZeroRemotePath(_logger, targetSession);
                return SendRemoteKeyboardInput(targetSession, keyCode, keyUp, extended);
            }

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
            Log.KeyboardInjectionFailed(_logger, sessionId, ex);
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
            Log.EngineNotInitialized(_logger);
            return false;
        }

        try
        {
            var currentSession = GetCurrentSessionId();

            // CRITICAL: Services running in Session 0 cannot use SendInput to inject
            // into user sessions due to Windows session isolation.
            if (currentSession == 0)
            {
                var targetSession = sessionId == 0 ? GetActiveUserSessionId() : sessionId;
                Log.UsingSessionZeroRemotePath(_logger, targetSession);
                return SendRemoteMouseInput(targetSession, input);
            }

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
            Log.MouseInjectionFailed(_logger, sessionId, ex);
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

        // SECURITY: Use validated SendInput with buffer size check
        var result = SendInputSafe(new[] { input });
        if (result == 0)
        {
            var error = Marshal.GetLastWin32Error();
            Log.SendInputFailed(_logger, error);
            return false;
        }

        return true;
    }

    private static bool SendLocalMouseInput(MouseInputData data)
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

        // SECURITY: Use validated SendInput with buffer size check
        var result = SendInputSafe(new[] { input });
        return result != 0;
    }

    private bool SendRemoteKeyboardInput(uint targetSessionId, ushort keyCode, bool keyUp, bool extended)
    {
        Log.LaunchingHelperRemoteKeyboard(_logger, targetSessionId);

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
        Log.LaunchingHelperRemoteMouse(_logger, targetSessionId);

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
            Log.WtsQueryUserTokenFailed(_logger, sessionId, error);
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
                Log.EnvironmentBlockFailed(_logger);
            }

            try
            {
                // SECURITY: Use Base64 encoding to prevent command injection via JSON special characters
                var base64Json = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
                var commandLine = $"\"{helperPath}\" \"{base64Json}\"";

                bool created = CreateProcessAsUser(
                    hToken,
                    helperPath,
                    commandLine,
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
                    Log.CreateProcessAsUserFailed(_logger, error);
                    return false;
                }

                try
                {
                    // Resume and wait for completion
                    int resumeResult = (int)ResumeThread(pi.hThread);
                    if (resumeResult == -1)
                    {
                        var error = Marshal.GetLastWin32Error();
                        Log.ResumeThreadFailed(_logger, error);
                    }

                    // Wait up to 5 seconds for helper to complete
                    using var process = Process.GetProcessById((int)pi.dwProcessId);
                    if (!process.WaitForExit(5000))
                    {
                        Log.SessionHelperTimeout(_logger);
                        try { process.Kill(); }
                        catch (Exception ex)
                        {
                            Log.KillHelperFailed(_logger, ex.Message);
                        }
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
        // SECURITY: Validate helper executable path to prevent path traversal attacks
        const string expectedFileName = "Redball.SessionHelper.exe";
        var serviceDir = AppContext.BaseDirectory;

        // Normalize and validate service directory
        var fullServiceDir = Path.GetFullPath(serviceDir);
        var helperPath = Path.Combine(fullServiceDir, expectedFileName);
        var fullHelperPath = Path.GetFullPath(helperPath);

        // SECURITY: Verify the helper path is within the service directory (prevent path traversal)
        if (!fullHelperPath.StartsWith(fullServiceDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Helper path escapes service directory: {helperPath}");
        }

        // SECURITY: Verify the filename matches expected (prevent file substitution)
        if (!Path.GetFileName(fullHelperPath).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Invalid helper executable name: {helperPath}");
        }

        if (!File.Exists(fullHelperPath))
        {
            // Fallback to sibling directory with same validation
            var parentDir = Directory.GetParent(serviceDir)?.Parent?.FullName;
            if (parentDir != null)
            {
                var fallbackPath = Path.Combine(parentDir, "Redball.SessionHelper", expectedFileName);
                var fullFallbackPath = Path.GetFullPath(fallbackPath);

                // Validate fallback path is within parent directory
                if (!fullFallbackPath.StartsWith(Path.GetFullPath(parentDir), StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Fallback helper path escapes parent directory: {fallbackPath}");
                }

                if (Path.GetFileName(fullFallbackPath).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(fullFallbackPath))
                {
                    return fullFallbackPath;
                }
            }
        }

        return fullHelperPath;
    }

    private static uint GetCurrentSessionId()
    {
        return (uint)Process.GetCurrentProcess().SessionId;
    }

    /// <summary>
    /// Gets the active console session ID (the currently logged-in user's session).
    /// This is the session we need to inject input into when running as a service in Session 0.
    /// </summary>
    private static uint GetActiveUserSessionId()
    {
        // Use WTS API to get the active console session
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            // No active console session, fallback to session 1 (typical user session)
            return 1;
        }
        return sessionId;
    }

    #region P/Invoke

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    // SECURITY: Buffer-validated wrapper for SendInput
    private static uint SendInputSafe(INPUT[] inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Length == 0)
            return 0;
        if (inputs.Length > 1000)
            throw new ArgumentException("Input array too large", nameof(inputs));

        int cbSize = Marshal.SizeOf<INPUT>();
        return SendInput((uint)inputs.Length, inputs, cbSize);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [LibraryImport("kernel32.dll")]
    private static partial uint WTSGetActiveConsoleSessionId();

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, [MarshalAs(UnmanagedType.Bool)] bool bInherit);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(IntPtr hThread);

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

    public record struct MouseInputData(int X, int Y, uint MouseData, uint Flags);

    #endregion

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Initializing input injection engine")]
        public static partial void Initializing(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Input injection engine shutdown")]
        public static partial void Shutdown(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Engine not initialized")]
        public static partial void EngineNotInitialized(ILogger logger);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to inject keyboard input for session {SessionId}")]
        public static partial void KeyboardInjectionFailed(ILogger logger, uint sessionId, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to inject mouse input for session {SessionId}")]
        public static partial void MouseInjectionFailed(ILogger logger, uint sessionId, Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "SendInput failed with error {Error}")]
        public static partial void SendInputFailed(ILogger logger, int error);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Launching helper for remote keyboard injection in session {SessionId}")]
        public static partial void LaunchingHelperRemoteKeyboard(ILogger logger, uint sessionId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Service in Session 0, using remote injection path for session {SessionId}")]
        public static partial void UsingSessionZeroRemotePath(ILogger logger, uint sessionId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Launching helper for remote mouse injection in session {SessionId}")]
        public static partial void LaunchingHelperRemoteMouse(ILogger logger, uint sessionId);

        [LoggerMessage(Level = LogLevel.Error, Message = "WTSQueryUserToken failed for session {SessionId}: {Error}")]
        public static partial void WtsQueryUserTokenFailed(ILogger logger, uint sessionId, int error);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create environment block")]
        public static partial void EnvironmentBlockFailed(ILogger logger);

        [LoggerMessage(Level = LogLevel.Error, Message = "CreateProcessAsUser failed: {Error}")]
        public static partial void CreateProcessAsUserFailed(ILogger logger, int error);

        [LoggerMessage(Level = LogLevel.Error, Message = "ResumeThread failed: {Error}")]
        public static partial void ResumeThreadFailed(ILogger logger, int error);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Session helper timed out")]
        public static partial void SessionHelperTimeout(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to kill timed out session helper: {Message}")]
        public static partial void KillHelperFailed(ILogger logger, string message);
    }
}
