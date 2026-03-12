using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Redball.UI.Views;

/// <summary>
/// Manages global hotkey registration via Win32 RegisterHotKey API.
/// Hooks into the WPF message loop to receive WM_HOTKEY messages.
/// </summary>
public class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    private const int WM_HOTKEY = 0x0312;

    private readonly HwndSource _hwndSource;
    private readonly Dictionary<int, Action> _hotkeyActions = new();
    private readonly List<int> _registeredIds = new();
    private bool _disposed;

    public HotkeyService(HwndSource hwndSource)
    {
        _hwndSource = hwndSource ?? throw new ArgumentNullException(nameof(hwndSource));
        _hwndSource.AddHook(WndProc);
    }

    public bool RegisterHotkey(int id, uint modifiers, uint vk, Action callback)
    {
        if (_disposed) return false;

        var handle = _hwndSource.Handle;
        if (handle == IntPtr.Zero) return false;

        var result = RegisterHotKey(handle, id, modifiers, vk);
        if (result)
        {
            _hotkeyActions[id] = callback;
            _registeredIds.Add(id);
        }
        return result;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_hotkeyActions.TryGetValue(id, out var action))
            {
                action?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var handle = _hwndSource.Handle;
        foreach (var id in _registeredIds)
        {
            UnregisterHotKey(handle, id);
        }
        _registeredIds.Clear();
        _hotkeyActions.Clear();

        try
        {
            _hwndSource.RemoveHook(WndProc);
        }
        catch
        {
            // HwndSource may already be disposed
        }
    }

    public static (uint Modifiers, uint VirtualKey) ParseHotkey(string hotkey)
    {
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
        return (modifiers, vk);
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
        return 0;
    }
}
