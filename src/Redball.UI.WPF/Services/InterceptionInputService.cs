using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using InputInterceptorNS;

namespace Redball.UI.Services;

/// <summary>
/// Provides driver-level keyboard input simulation via the Interception driver.
/// This bypasses SendInput limitations on remote/RDP sessions by injecting
/// keystrokes at the HID driver level, making them indistinguishable from
/// real USB keyboard input.
/// 
/// Requires the Interception driver to be installed (admin, one-time setup).
/// Falls back gracefully if the driver is not present.
/// </summary>
public class InterceptionInputService : IDisposable
{
    private static readonly Lazy<InterceptionInputService> _instance = new(() => new InterceptionInputService());
    public static InterceptionInputService Instance => _instance.Value;

    private KeyboardHook? _keyboardHook;
    private bool _initialized;
    private bool _disposed;
    private readonly object _lock = new();

    /// <summary>
    /// Whether the Interception driver is installed on this system.
    /// </summary>
    public bool IsDriverInstalled { get; private set; }

    /// <summary>
    /// Whether the service is initialized and ready to send keystrokes.
    /// </summary>
    public bool IsReady => _initialized && _keyboardHook != null && _keyboardHook.CanSimulateInput;

    // --- P/Invoke for layout-aware character→key mapping ---

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    private const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, NativeINPUT[] pInputs, int cbSize);

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeINPUT
    {
        public int type;
        public NativeINPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeINPUTUNION
    {
        [FieldOffset(0)] public NativeKEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private InterceptionInputService()
    {
        Logger.Verbose("InterceptionInputService", "Instance created");
    }

    /// <summary>
    /// Checks if the Interception driver is installed and initializes the keyboard hook.
    /// Call this once during app startup or when the user enables HID mode.
    /// Returns true if ready to use.
    /// </summary>
    public bool Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return IsReady;

            try
            {
                // Check driver installation via registry
                IsDriverInstalled = InputInterceptor.CheckDriverInstalled();
                Logger.Info("InterceptionInputService", $"Driver installed: {IsDriverInstalled}");

                if (!IsDriverInstalled)
                {
                    Logger.Warning("InterceptionInputService", "Interception driver not installed. HID input mode unavailable.");
                    return false;
                }

                // Initialize the InputInterceptor DLL wrapper
                if (!InputInterceptor.Initialize())
                {
                    Logger.Error("InterceptionInputService", "Failed to initialize InputInterceptor DLL");
                    return false;
                }

                // Create keyboard hook with no filter (we only want to send, not intercept)
                // KeyboardFilter.None means we won't capture any incoming keystrokes
                _keyboardHook = new KeyboardHook(KeyboardFilter.All);
                _initialized = true;

                Logger.Info("InterceptionInputService", $"Initialized successfully. CanSimulateInput: {_keyboardHook.CanSimulateInput}");
                return IsReady;
            }
            catch (Exception ex)
            {
                Logger.Error("InterceptionInputService", "Failed to initialize", ex);
                _initialized = false;
                return false;
            }
        }
    }

    /// <summary>
    /// Sends a single key press (down + up) at the driver level.
    /// </summary>
    /// <param name="keyCode">The Interception KeyCode to press.</param>
    /// <param name="releaseDelayMs">Delay in ms between key down and key up.</param>
    /// <returns>True if the keystroke was sent successfully.</returns>
    public bool SendKeyPress(KeyCode keyCode, int releaseDelayMs = 10)
    {
        if (!IsReady)
        {
            Logger.Debug("InterceptionInputService", "SendKeyPress called but service not ready");
            return false;
        }

        try
        {
            return _keyboardHook!.SimulateKeyPress(keyCode, releaseDelayMs);
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"SendKeyPress failed for {keyCode}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sends a character at the driver level using the current keyboard layout.
    /// Uses VkKeyScanW to determine the correct key + modifier combination for ANY
    /// character on the user's active layout (UK, US, DE, etc.), covering all symbols
    /// including £, €, @, #, ^, §, ñ, ü, etc.
    /// 
    /// For characters with no physical key mapping (emoji, rare Unicode), falls back
    /// to Win32 SendInput with KEYEVENTF_UNICODE.
    /// </summary>
    /// <param name="ch">The character to type.</param>
    /// <returns>True if the character was sent successfully.</returns>
    public bool SendCharacter(char ch)
    {
        if (!IsReady)
        {
            Logger.Debug("InterceptionInputService", "SendCharacter called but service not ready");
            return false;
        }

        try
        {
            // Handle tab and newline directly
            if (ch == '\t') return _keyboardHook!.SimulateKeyPress(KeyCode.Tab, 10);
            if (ch == '\n') return _keyboardHook!.SimulateKeyPress(KeyCode.Enter, 10);
            if (ch == '\r') return true; // Skip CR (handled by LF)

            // Use VkKeyScanW to find the key combination for this character
            // on the current keyboard layout. This handles ALL layout-specific
            // symbols: £ (Shift+3 on UK), @ (Shift+' on UK), # (dedicated key on UK),
            // € (AltGr+4 on UK/DE), ñ, ü, ß, § etc.
            var vkResult = VkKeyScanW(ch);

            if (vkResult != -1)
            {
                var vk = (byte)(vkResult & 0xFF);
                var shiftState = (byte)((vkResult >> 8) & 0xFF);

                // Prefer layout-aware physical scan-code mapping for character typing.
                // This avoids US-centric OEM key assumptions and preserves symbol fidelity
                // on UK/DE/etc keyboard layouts.
                KeyCode? keyCode = null;
                var scan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC);
                if (scan != 0)
                {
                    keyCode = (KeyCode)scan;
                }
                else
                {
                    keyCode = VirtualKeyToKeyCode(vk);
                }

                if (keyCode != null)
                {
                    var needShift = (shiftState & 1) != 0;
                    var needCtrl = (shiftState & 2) != 0;
                    var needAlt = (shiftState & 4) != 0;
                    // AltGr = Ctrl+Alt on Windows
                    var needAltGr = needCtrl && needAlt;

                    // Press modifiers
                    if (needAltGr)
                    {
                        // AltGr is sent as RightAlt on most layouts
                        _keyboardHook!.SetKeyState(KeyCode.Control, KeyState.Down);
                        _keyboardHook!.SetKeyState(KeyCode.Alt, KeyState.Down);
                    }
                    else
                    {
                        if (needShift) _keyboardHook!.SetKeyState(KeyCode.LeftShift, KeyState.Down);
                        if (needCtrl) _keyboardHook!.SetKeyState(KeyCode.Control, KeyState.Down);
                        if (needAlt) _keyboardHook!.SetKeyState(KeyCode.Alt, KeyState.Down);
                    }

                    Thread.Sleep(2);

                    // Press the key
                    var result = _keyboardHook!.SimulateKeyPress(keyCode.Value, 10);

                    Thread.Sleep(2);

                    // Release modifiers (reverse order)
                    if (needAltGr)
                    {
                        _keyboardHook!.SetKeyState(KeyCode.Alt, KeyState.Up);
                        _keyboardHook!.SetKeyState(KeyCode.Control, KeyState.Up);
                    }
                    else
                    {
                        if (needAlt) _keyboardHook!.SetKeyState(KeyCode.Alt, KeyState.Up);
                        if (needCtrl) _keyboardHook!.SetKeyState(KeyCode.Control, KeyState.Up);
                        if (needShift) _keyboardHook!.SetKeyState(KeyCode.LeftShift, KeyState.Up);
                    }

                    return result;
                }
            }

            // No physical key mapping exists for this character on the current layout.
            // Fall back to Win32 SendInput with Unicode injection.
            // This handles emoji, rare symbols, and characters from other scripts.
            Logger.Debug("InterceptionInputService", $"No HID mapping for '{ch}' (U+{(int)ch:X4}), using Unicode fallback");
            return SendUnicodeFallback(ch);
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"SendCharacter failed for '{ch}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sends a character via Win32 SendInput KEYEVENTF_UNICODE.
    /// Used as fallback for characters that have no physical key on the current layout.
    /// </summary>
    private static bool SendUnicodeFallback(char ch)
    {
        try
        {
            var inputs = new NativeINPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wScan = (ushort)ch;
            inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wScan = (ushort)ch;
            inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            return SendInput(2, inputs, Marshal.SizeOf<NativeINPUT>()) == 2;
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"Unicode fallback failed for '{ch}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sends a virtual key press by mapping VK code to Interception KeyCode.
    /// Used for special keys like VK_RETURN, VK_TAB, F-keys, etc.
    /// </summary>
    /// <param name="vk">Win32 virtual key code.</param>
    /// <returns>True if the keystroke was sent successfully.</returns>
    public bool SendVirtualKey(ushort vk)
    {
        if (!IsReady) return false;

        try
        {
            var keyCode = VirtualKeyToKeyCode(vk);
            if (keyCode == null)
            {
                Logger.Debug("InterceptionInputService", $"No KeyCode mapping for VK 0x{vk:X4}");
                return false;
            }

            return _keyboardHook!.SimulateKeyPress(keyCode.Value, 10);
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"SendVirtualKey failed for VK 0x{vk:X4}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Attempts to install the Interception driver. Requires admin privileges.
    /// A reboot is required after installation.
    /// </summary>
    /// <returns>True if installation was initiated successfully.</returns>
    public bool InstallDriver(bool elevateIfNeeded = true)
    {
        try
        {
            if (IsDriverInstalled)
            {
                Logger.Info("InterceptionInputService", "Driver already installed");
                return true;
            }

            if (!InputInterceptor.CheckAdministratorRights())
            {
                if (!elevateIfNeeded)
                {
                    Logger.Warning("InterceptionInputService", "Admin rights required to install driver, but elevateIfNeeded is false.");
                    return false;
                }

                Logger.Info("InterceptionInputService", "Attempting to elevate for driver installation");
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "Redball.UI.WPF.exe",
                    Arguments = "--install-driver",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    using var process = System.Diagnostics.Process.Start(processInfo);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Logger.Warning("InterceptionInputService", "User cancelled UAC elevation");
                    return false;
                }
            }

            var tempExe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "install-interception.exe");
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames();
            var targetName = System.Linq.Enumerable.FirstOrDefault(names, n => n.EndsWith("install-interception.exe") || n.EndsWith("install_interception.exe"));
            
            if (targetName == null)
            {
                Logger.Error("InterceptionInputService", "Could not find install-interception.exe embedded resource. Available resources: " + string.Join(", ", names));
                return false;
            }

            using (var stream = asm.GetManifestResourceStream(targetName))
            {
                using (var fileStream = new System.IO.FileStream(tempExe, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                {
                    stream!.CopyTo(fileStream);
                }
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempExe,
                Arguments = "/install",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            using var driverProcess = System.Diagnostics.Process.Start(startInfo);
            driverProcess?.WaitForExit();
            var result = driverProcess?.ExitCode == 0;

            Logger.Info("InterceptionInputService", $"Driver installation result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error("InterceptionInputService", "Driver installation failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets a diagnostic status string for the service.
    /// </summary>
    public string GetStatusText()
    {
        if (!IsDriverInstalled) return "Driver not installed";
        if (!_initialized) return "Not initialized";
        if (!IsReady) return "Initialized but not ready (no device captured)";
        return "Ready (HID keyboard active)";
    }

    /// <summary>
    /// Maps Win32 virtual key codes to Interception KeyCode values.
    /// Covers alphanumeric, F-keys, navigation, numpad, and all punctuation/symbol keys
    /// so that VkKeyScanW results can be routed through the Interception driver.
    /// </summary>
    private static KeyCode? VirtualKeyToKeyCode(ushort vk)
    {
        return vk switch
        {
            0x08 => KeyCode.Backspace,
            0x09 => KeyCode.Tab,
            0x0D => KeyCode.Enter,
            0x10 => KeyCode.LeftShift,
            0x11 => KeyCode.Control,
            0x12 => KeyCode.Alt,
            0x14 => KeyCode.CapsLock,
            0x1B => KeyCode.Escape,
            0x20 => KeyCode.Space,
            0x21 => KeyCode.PageUp,
            0x22 => KeyCode.PageDown,
            0x23 => KeyCode.End,
            0x24 => KeyCode.Home,
            0x25 => KeyCode.Left,
            0x26 => KeyCode.Up,
            0x27 => KeyCode.Right,
            0x28 => KeyCode.Down,
            0x2C => KeyCode.PrintScreen,
            0x2D => KeyCode.Insert,
            0x2E => KeyCode.Delete,
            // 0-9 (also produces ! @ # $ % ^ & * ( ) with Shift)
            0x30 => KeyCode.Zero,
            0x31 => KeyCode.One,
            0x32 => KeyCode.Two,
            0x33 => KeyCode.Three,
            0x34 => KeyCode.Four,
            0x35 => KeyCode.Five,
            0x36 => KeyCode.Six,
            0x37 => KeyCode.Seven,
            0x38 => KeyCode.Eight,
            0x39 => KeyCode.Nine,
            // A-Z
            0x41 => KeyCode.A,
            0x42 => KeyCode.B,
            0x43 => KeyCode.C,
            0x44 => KeyCode.D,
            0x45 => KeyCode.E,
            0x46 => KeyCode.F,
            0x47 => KeyCode.G,
            0x48 => KeyCode.H,
            0x49 => KeyCode.I,
            0x4A => KeyCode.J,
            0x4B => KeyCode.K,
            0x4C => KeyCode.L,
            0x4D => KeyCode.M,
            0x4E => KeyCode.N,
            0x4F => KeyCode.O,
            0x50 => KeyCode.P,
            0x51 => KeyCode.Q,
            0x52 => KeyCode.R,
            0x53 => KeyCode.S,
            0x54 => KeyCode.T,
            0x55 => KeyCode.U,
            0x56 => KeyCode.V,
            0x57 => KeyCode.W,
            0x58 => KeyCode.X,
            0x59 => KeyCode.Y,
            0x5A => KeyCode.Z,
            // Windows / Apps keys
            0x5B => KeyCode.LeftWindowsKey,    // VK_LWIN
            0x5C => KeyCode.RightWindowsKey,   // VK_RWIN
            0x5D => KeyCode.Menu,              // VK_APPS (context menu key)
            // Numpad
            0x60 => KeyCode.Numpad0,
            0x61 => KeyCode.Numpad1,
            0x62 => KeyCode.Numpad2,
            0x63 => KeyCode.Numpad3,
            0x64 => KeyCode.Numpad4,
            0x65 => KeyCode.Numpad5,
            0x66 => KeyCode.Numpad6,
            0x67 => KeyCode.Numpad7,
            0x68 => KeyCode.Numpad8,
            0x69 => KeyCode.Numpad9,
            0x6A => KeyCode.NumpadAsterisk,  // Numpad *
            0x6B => KeyCode.NumpadPlus,      // Numpad +
            0x6D => KeyCode.NumpadMinus,     // Numpad -
            0x6E => KeyCode.NumpadDelete,    // Numpad . (decimal)
            0x6F => KeyCode.NumpadDivide,    // Numpad /
            // F-keys
            0x70 => KeyCode.F1,
            0x71 => KeyCode.F2,
            0x72 => KeyCode.F3,
            0x73 => KeyCode.F4,
            0x74 => KeyCode.F5,
            0x75 => KeyCode.F6,
            0x76 => KeyCode.F7,
            0x77 => KeyCode.F8,
            0x78 => KeyCode.F9,
            0x79 => KeyCode.F10,
            0x7A => KeyCode.F11,
            0x7B => KeyCode.F12,
            0x90 => KeyCode.NumLock,
            0x91 => KeyCode.ScrollLock,
            // Left/Right specific modifier VK codes (VkKeyScanW may return these)
            0xA0 => KeyCode.LeftShift,         // VK_LSHIFT
            0xA1 => KeyCode.RightShift,        // VK_RSHIFT
            0xA2 => KeyCode.Control,           // VK_LCONTROL
            0xA3 => KeyCode.Control,           // VK_RCONTROL (Interception has one Control code)
            0xA4 => KeyCode.Alt,               // VK_LMENU
            0xA5 => KeyCode.Alt,               // VK_RMENU (AltGr on European keyboards)
            // OEM keys — these are the physical keys for punctuation/symbols.
            // VkKeyScanW maps characters like £ $ % ^ @ # ' " ; : etc. to
            // these VK_OEM codes + shift state, so we must map them all.
            0xBA => KeyCode.Semicolon,       // VK_OEM_1  (;: on US, ;+ on UK, etc.)
            0xBB => KeyCode.Equals,          // VK_OEM_PLUS (=+ on US)
            0xBC => KeyCode.Comma,           // VK_OEM_COMMA (,< on US)
            0xBD => KeyCode.Dash,            // VK_OEM_MINUS (-_ on US)
            0xBE => KeyCode.Dot,             // VK_OEM_PERIOD (.> on US)
            0xBF => KeyCode.Slash,           // VK_OEM_2  (/? on US)
            0xC0 => KeyCode.Tilde,           // VK_OEM_3  (`~ on US, '@ on UK)
            0xDB => KeyCode.OpenBracketBrace,  // VK_OEM_4  ([{ on US)
            0xDC => KeyCode.Backslash,       // VK_OEM_5  (\| on US, #~ on UK)
            0xDD => KeyCode.CloseBracketBrace, // VK_OEM_6  (]} on US)
            0xDE => KeyCode.Apostrophe,      // VK_OEM_7  ('" on US, #~ on UK)
            0xDF => KeyCode.Tilde,           // VK_OEM_8  (` on UK/international layouts; map to physical OEM3 key)
            0xE2 => KeyCode.Backslash,       // VK_OEM_102 (extra key on 102-key European keyboards: \| on UK, <> on DE)
            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Logger.Info("InterceptionInputService", "Disposing...");

        try
        {
            _keyboardHook?.Dispose();
            _keyboardHook = null;

            if (_initialized)
            {
                InputInterceptor.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"Dispose error: {ex.Message}");
        }

        _initialized = false;
        Logger.Info("InterceptionInputService", "Disposed");
    }
}
