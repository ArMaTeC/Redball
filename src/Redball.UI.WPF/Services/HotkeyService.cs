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
}
