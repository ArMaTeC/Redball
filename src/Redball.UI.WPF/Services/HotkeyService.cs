using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Redball.UI.Views;

/// <summary>
/// Manages global hotkey registration. Uses RegisterHotKey for local sessions
/// and LowLevelKeyboardHook for RDP sessions where RegisterHotKey doesn't work.
/// </summary>
public class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_REMOTESESSION = 0x1000;
    private const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    private readonly HwndSource? _hwndSource;
    private readonly LowLevelKeyboardHook? _llHook;
    private readonly Dictionary<int, (uint Modifiers, uint Key, Action Callback)> _hotkeyRegistrations = new();
    private readonly List<int> _registeredIds = new();
    private readonly bool _useLowLevelHook;
    private bool _disposed;

    public HotkeyService(HwndSource hwndSource)
    {
        Services.Logger.Info("HotkeyService", "Initializing HotkeyService...");
        
        // Detect if we're in a remote session (RDP)
        _useLowLevelHook = IsRemoteSession();
        Services.Logger.Info("HotkeyService", $"Remote session detected: {_useLowLevelHook}");

        if (_useLowLevelHook)
        {
            // Use low-level hook for RDP sessions
            _llHook = new LowLevelKeyboardHook();
            _llHook.Install();
        }
        else
        {
            // Use RegisterHotKey for local sessions
            if (hwndSource == null)
            {
                Services.Logger.Fatal("HotkeyService", "HwndSource is null - hotkey service cannot be created");
                throw new ArgumentNullException(nameof(hwndSource));
            }
            
            _hwndSource = hwndSource;
            _hwndSource.AddHook(WndProc);
            Services.Logger.Debug("HotkeyService", $"Hooked into HwndSource handle: {hwndSource.Handle}");
        }
    }

    private static bool IsRemoteSession()
    {
        return GetSystemMetrics(SM_REMOTESESSION) != 0;
    }

    public bool RegisterHotkey(int id, uint modifiers, uint vk, Action callback)
    {
        if (_disposed)
        {
            Services.Logger.Warning("HotkeyService", $"Cannot register hotkey {id}: service is disposed");
            return false;
        }

        // Store registration for potential re-registration
        _hotkeyRegistrations[id] = (modifiers, vk, callback);

        if (_useLowLevelHook && _llHook != null)
        {
            // Use low-level hook for RDP sessions
            _llHook.RegisterHotkey(modifiers, vk, callback);
            _registeredIds.Add(id);
            Services.Logger.Info("HotkeyService", $"Hotkey {id} registered with low-level hook");
            return true;
        }

        // Use RegisterHotKey for local sessions
        if (_hwndSource?.Handle == null || _hwndSource.Handle == IntPtr.Zero)
        {
            Services.Logger.Error("HotkeyService", $"Cannot register hotkey {id}: window handle is not available");
            return false;
        }

        var modifierStr = FormatModifiers(modifiers);
        var keyStr = FormatVirtualKey(vk);
        Services.Logger.Info("HotkeyService", $"Registering hotkey ID={id}: {modifierStr}+{keyStr} (vk=0x{vk:X})");

        var result = RegisterHotKey(_hwndSource.Handle, id, modifiers, vk);
        if (result)
        {
            _registeredIds.Add(id);
            Services.Logger.Info("HotkeyService", $"Hotkey {id} registered successfully via RegisterHotKey");
        }
        else
        {
            var error = Marshal.GetLastWin32Error();
            Services.Logger.Error("HotkeyService", $"Failed to register hotkey {id}: Win32 error {error} (0x{error:X})");
        }
        
        return result;
    }

    public bool RegisterHotkey(int id, string hotkeyString, Action callback)
    {
        Services.Logger.Debug("HotkeyService", $"Parsing hotkey string '{hotkeyString}' for ID {id}");
        var (modifiers, vk) = ParseHotkey(hotkeyString);
        
        if (vk == 0)
        {
            Services.Logger.Warning("HotkeyService", $"Could not parse virtual key from '{hotkeyString}'");
            return false;
        }
        
        return RegisterHotkey(id, modifiers, vk, callback);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && !_useLowLevelHook)
        {
            var id = wParam.ToInt32();
            if (_hotkeyRegistrations.TryGetValue(id, out var registration))
            {
                Services.Logger.Verbose("HotkeyService", $"WM_HOTKEY received for ID {id}, invoking callback");
                try
                {
                    registration.Callback?.Invoke();
                    handled = true;
                }
                catch (Exception ex)
                {
                    Services.Logger.Error("HotkeyService", $"Hotkey callback for ID {id} threw exception", ex);
                }
            }
            else
            {
                Services.Logger.Warning("HotkeyService", $"WM_HOTKEY for unknown ID {id}");
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            Services.Logger.Verbose("HotkeyService", "Dispose called but already disposed");
            return;
        }
        
        _disposed = true;
        Services.Logger.Info("HotkeyService", $"Disposing service, unregistering {_registeredIds.Count} hotkeys...");

        if (_useLowLevelHook)
        {
            // Dispose low-level hook
            _llHook?.Dispose();
        }
        else
        {
            // Unregister hotkeys via RegisterHotKey API
            var handle = _hwndSource?.Handle ?? IntPtr.Zero;
            int successCount = 0;
            int failCount = 0;
            
            foreach (var id in _registeredIds)
            {
                try
                {
                    if (handle != IntPtr.Zero && UnregisterHotKey(handle, id))
                    {
                        successCount++;
                        Services.Logger.Verbose("HotkeyService", $"Unregistered hotkey {id}");
                    }
                    else
                    {
                        failCount++;
                        var error = Marshal.GetLastWin32Error();
                        Services.Logger.Warning("HotkeyService", $"Failed to unregister hotkey {id}: Win32 error {error}");
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    Services.Logger.Error("HotkeyService", $"Exception unregistering hotkey {id}", ex);
                }
            }
            
            Services.Logger.Info("HotkeyService", $"Unregister complete: {successCount} succeeded, {failCount} failed");

            try
            {
                _hwndSource?.RemoveHook(WndProc);
                Services.Logger.Debug("HotkeyService", "Window hook removed");
            }
            catch (Exception ex)
            {
                Services.Logger.Warning("HotkeyService", $"Error removing hook (may already be disposed): {ex.Message}");
            }
        }
        
        _registeredIds.Clear();
        _hotkeyRegistrations.Clear();
        Services.Logger.Info("HotkeyService", "Dispose complete");
    }

    public static (uint Modifiers, uint VirtualKey) ParseHotkey(string hotkey)
    {
        Services.Logger.Verbose("HotkeyService", $"Parsing hotkey: '{hotkey}'");
        
        var parts = hotkey?.Split('+', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        uint modifiers = 0;
        uint vk = 0;

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= MOD_CONTROL;
                    break;
                case "ALT":
                    modifiers |= MOD_ALT;
                    break;
                case "SHIFT":
                    modifiers |= MOD_SHIFT;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    vk = KeyToVirtualKey(trimmed);
                    break;
            }
        }
        
        Services.Logger.Debug("HotkeyService", $"Parsed '{hotkey}' -> modifiers=0x{modifiers:X}, vk=0x{vk:X}");
        return (modifiers, vk);
    }

    private static string FormatModifiers(uint modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & MOD_WIN) != 0) parts.Add("Win");
        return string.Join("+", parts);
    }

    private static string FormatVirtualKey(uint vk)
    {
        // Common virtual keys
        return vk switch
        {
            >= 0x41 and <= 0x5A => ((char)('A' + (vk - 0x41))).ToString(),
            >= 0x30 and <= 0x39 => ((char)('0' + (vk - 0x30))).ToString(),
            >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",
            0x13 => "Pause",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x09 => "Tab",
            0x20 => "Space",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            _ => $"0x{vk:X}"
        };
    }

    private static uint KeyToVirtualKey(string key)
    {
        if (key.Length == 1 && char.IsLetter(key[0]))
            return (uint)(0x41 + (char.ToUpper(key[0]) - 'A')); // A-Z
        if (key.Length == 1 && char.IsDigit(key[0]))
            return (uint)(0x30 + (key[0] - '0')); // 0-9
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.AsSpan(1), out var fnum) && fnum >= 1 && fnum <= 24)
            return (uint)(0x70 + fnum - 1); // F1-F24
        if (key.Equals("PAUSE", StringComparison.OrdinalIgnoreCase) || key.Equals("BREAK", StringComparison.OrdinalIgnoreCase))
            return 0x13;
        if (key.Equals("INSERT", StringComparison.OrdinalIgnoreCase))
            return 0x2D;
        if (key.Equals("DELETE", StringComparison.OrdinalIgnoreCase) || key.Equals("DEL", StringComparison.OrdinalIgnoreCase))
            return 0x2E;
        if (key.Equals("HOME", StringComparison.OrdinalIgnoreCase))
            return 0x24;
        if (key.Equals("END", StringComparison.OrdinalIgnoreCase))
            return 0x23;
        if (key.Equals("PAGEUP", StringComparison.OrdinalIgnoreCase))
            return 0x21;
        if (key.Equals("PAGEDOWN", StringComparison.OrdinalIgnoreCase))
            return 0x22;
        if (key.Equals("TAB", StringComparison.OrdinalIgnoreCase))
            return 0x09;
        if (key.Equals("SPACE", StringComparison.OrdinalIgnoreCase))
            return 0x20;
        if (key.Equals("UP", StringComparison.OrdinalIgnoreCase))
            return 0x26;
        if (key.Equals("DOWN", StringComparison.OrdinalIgnoreCase))
            return 0x28;
        if (key.Equals("LEFT", StringComparison.OrdinalIgnoreCase))
            return 0x25;
        if (key.Equals("RIGHT", StringComparison.OrdinalIgnoreCase))
            return 0x27;
        
        Services.Logger.Warning("HotkeyService", $"Unknown key '{key}', returning 0");
        return 0;
    }
}
