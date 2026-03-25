using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Redball.UI.Views;

/// <summary>
/// Low-level keyboard hook using SetWindowsHookEx(WH_KEYBOARD_LL).
/// Captures keystrokes globally, including when RDP client has focus.
/// </summary>
public class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly HookProc _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private readonly Dictionary<(uint Modifiers, uint Key), Action> _hotkeyActions = new();
    private readonly object _lock = new();
    private bool _disposed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public LowLevelKeyboardHook()
    {
        _hookProc = HookCallback;
        Services.Logger.Info("LowLevelKeyboardHook", "Instance created, not yet installed");
    }

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public void Install()
    {
        if (_disposed)
        {
            Services.Logger.Warning("LowLevelKeyboardHook", "Cannot install: already disposed");
            return;
        }

        if (_hookId != IntPtr.Zero)
        {
            Services.Logger.Debug("LowLevelKeyboardHook", "Already installed");
            return;
        }

        Services.Logger.Info("LowLevelKeyboardHook", "Installing hook...");

        // Use current process module for the hook
        var moduleHandle = GetModuleHandle(string.Empty);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);

        if (_hookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Services.Logger.Error("LowLevelKeyboardHook", $"Failed to install hook: Win32 error {error}");
        }
        else
        {
            Services.Logger.Info("LowLevelKeyboardHook", "Hook installed successfully");
        }
    }

    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero) return;

        Services.Logger.Info("LowLevelKeyboardHook", "Uninstalling hook...");
        if (UnhookWindowsHookEx(_hookId))
        {
            Services.Logger.Info("LowLevelKeyboardHook", "Hook uninstalled successfully");
        }
        else
        {
            var error = Marshal.GetLastWin32Error();
            Services.Logger.Warning("LowLevelKeyboardHook", $"Failed to uninstall hook: Win32 error {error}");
        }
        _hookId = IntPtr.Zero;
    }

    public void RegisterHotkey(uint modifiers, uint vk, Action callback)
    {
        lock (_lock)
        {
            var key = (modifiers, vk);
            _hotkeyActions[key] = callback;
            Services.Logger.Debug("LowLevelKeyboardHook", $"Registered hotkey: mods=0x{modifiers:X}, vk=0x{vk:X}");
        }
    }

    public void UnregisterAll()
    {
        lock (_lock)
        {
            _hotkeyActions.Clear();
            Services.Logger.Debug("LowLevelKeyboardHook", "All hotkeys unregistered");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var vk = hookStruct.vkCode;

            // Build modifier mask from async key states
            uint modifiers = 0;
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0) modifiers |= HotkeyService.MOD_CONTROL; // VK_CONTROL
            if ((GetAsyncKeyState(0x12) & 0x8000) != 0) modifiers |= HotkeyService.MOD_ALT;       // VK_MENU (Alt)
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) modifiers |= HotkeyService.MOD_SHIFT;     // VK_SHIFT
            if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0) 
                modifiers |= HotkeyService.MOD_WIN;  // VK_LWIN or VK_RWIN

            Action? action = null;
            lock (_lock)
            {
                if (_hotkeyActions.TryGetValue((modifiers, vk), out action))
                {
                    Services.Logger.Verbose("LowLevelKeyboardHook", $"Hotkey matched: mods=0x{modifiers:X}, vk=0x{vk:X}");
                }
            }

            if (action != null)
            {
                try
                {
                    // Invoke on thread pool to avoid blocking the hook
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            Services.Logger.Error("LowLevelKeyboardHook", "Hotkey callback threw exception", ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Services.Logger.Error("LowLevelKeyboardHook", "Failed to queue hotkey callback", ex);
                }

                // Block the key from propagating (eat the keystroke)
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Services.Logger.Info("LowLevelKeyboardHook", "Disposing...");
        Uninstall();
        UnregisterAll();
    }
}
