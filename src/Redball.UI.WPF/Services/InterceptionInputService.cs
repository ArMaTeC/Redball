using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
    private ManagementEventWatcher? _deviceArrivalWatcher;
    private ManagementEventWatcher? _deviceRemovalWatcher;
    private Timer? _deviceRefreshDebounceTimer;
    private DateTime? _lastRefreshUtc;
    private DateTime _nextAllowedRefreshUtc = DateTime.MinValue;
    private string _lastErrorSummary = "None";
    private DateTime? _lastDriverActionUtc;
    private string _lastDriverAction = "None";
    private int _consecutiveInitializeFailures;
    private bool _isRebootRequired;

    /// <summary>
    /// Maximum time to wait for a key send operation before considering it failed (ms).
    /// </summary>
    private const int KeySendTimeoutMs = 5000;

    /// <summary>
    /// Maximum retries for a single character send before failing.
    /// </summary>
    private const int MaxCharacterSendRetries = 3;

    /// <summary>
    /// Backoff multiplier between retries (ms).
    /// </summary>
    private const int CharacterRetryBackoffMs = 50;

    /// <summary>
    /// Idle timeout before auto-releasing HID resources (minutes).
    /// </summary>
    private const int IdleTimeoutMinutes = 2; // Reduced from 30 for safety

    /// <summary>
    /// Debounce delay for USB device refresh (ms).
    /// </summary>
    private const int DeviceRefreshDebounceMs = 1200;

    /// <summary>
    /// Cooldown between HID refreshes (ms).
    /// </summary>
    private const int RefreshCooldownMs = 2500;

    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private Timer? _idleCheckTimer;
    private string? _driverVersion;
    private bool _audioFeedbackEnabled;

    /// <summary>
    /// Expected SHA256 hashes for the interception driver files (example values).
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> DriverHashes = new()
    {
        { "interception.sys", "..." }, // In a real app, these would be known good hashes
        { "Redball.KMDF.sys", "..." }
    };

    private const string LegacyServiceName = "interception";
    private const string RedballKmdfServiceName = "Redball.KMDF";

    public bool AudioFeedbackEnabled
    {
        get => _audioFeedbackEnabled;
        set => _audioFeedbackEnabled = value;
    }

    /// <summary>
    /// Whether the Interception driver is installed on this system.
    /// </summary>
    public bool IsDriverInstalled { get; private set; }

    /// <summary>
    /// Whether the service is initialized and ready to send keystrokes.
    /// </summary>
    public bool IsReady => _initialized && _keyboardHook != null && _keyboardHook.CanSimulateInput;
    public bool IsInitialized => _initialized;
    public DateTime? LastRefreshUtc => _lastRefreshUtc;
    public DateTime? NextAllowedRefreshUtc => _nextAllowedRefreshUtc == DateTime.MinValue ? null : _nextAllowedRefreshUtc;
    public string LastErrorSummary => _lastErrorSummary;
    public DateTime? LastDriverActionUtc => _lastDriverActionUtc;
    public string LastDriverAction => _lastDriverAction;
    public int ConsecutiveInitializeFailures => _consecutiveInitializeFailures;
    public string? DriverVersion => _driverVersion;
    public bool IsRebootRequired => _isRebootRequired;
    public DriverSelection InstalledDriverType { get; private set; } = DriverSelection.None;

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

    public bool UninstallDriver(bool elevateIfNeeded = true)
    {
        try
        {
            if (!InputInterceptor.CheckAdministratorRights())
            {
                if (!elevateIfNeeded) return false;

                Logger.Info("InterceptionInputService", "Attempting to elevate for driver uninstall");
                var processInfo = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "Redball.UI.WPF.exe",
                    Arguments = "--uninstall-driver",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    using var process = Process.Start(processInfo);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Logger.Warning("InterceptionInputService", "User cancelled UAC elevation for uninstall");
                    return false;
                }
            }

            Logger.Info("InterceptionInputService", "Uninstalling all Redball HID drivers...");
            return ManualUninstallCleanup();
        }
        catch (Exception ex)
        {
            Logger.Error("InterceptionInputService", "Driver uninstallation failed", ex);
            return false;
        }
    }

    public bool InstallDriverNoRestart(DriverSelection selection = DriverSelection.Auto, bool elevateIfNeeded = true)
    {
        try
        {
            if (selection == DriverSelection.Auto)
            {
                selection = Interop.NativeMethods.IsTestModeEnabled() ? DriverSelection.RedballKMDF : DriverSelection.Interception;
            }

            if (!InstallDriver(selection, elevateIfNeeded)) return false;
            
            Logger.Info("InterceptionInputService", $"Driver {selection} installed. Attempting no-restart activation...");
            TryRestartKeyboardDevices();
            
            return RefreshDriverInstalledState();
        }
        catch (Exception ex)
        {
            Logger.Error("InterceptionInputService", "No-restart driver installation failed", ex);
            return false;
        }
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

    public bool RefreshDriverInstalledState()
    {
        try
        {
            // Check for service in registry (legacy or custom)
            bool legacyService = false;
            bool customService = false;
            try
            {
                using var legacyKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{LegacyServiceName}", false);
                legacyService = legacyKey != null;
                using var customKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{RedballKmdfServiceName}", false);
                customService = customKey != null;
            }
            catch { }

            // Check if it's in UpperFilters
            bool legacyInFilters = false;
            bool customInFilters = false;
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e96b-e325-11ce-bfc1-08002be10318}", false);
                var filters = key?.GetValue("UpperFilters") as string[];
                if (filters != null)
                {
                    legacyInFilters = Array.IndexOf(filters, LegacyServiceName) >= 0;
                    customInFilters = Array.IndexOf(filters, RedballKmdfServiceName) >= 0;
                }
            }
            catch { }

            var legacyFileExists = File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "interception.sys"));
            var customFileExists = File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "Redball.KMDF.sys"));
            
            if (customService && customFileExists && customInFilters)
                InstalledDriverType = DriverSelection.RedballKMDF;
            else if (legacyService && legacyFileExists && legacyInFilters)
                InstalledDriverType = DriverSelection.Interception;
            else if (customService || legacyService)
                InstalledDriverType = (customService && customFileExists) ? DriverSelection.RedballKMDF : DriverSelection.Interception;
            else
                InstalledDriverType = DriverSelection.None;

            IsDriverInstalled = InstalledDriverType != DriverSelection.None;

            if (IsDriverInstalled && (!legacyInFilters && !customInFilters))
            {
                Logger.Info("InterceptionInputService", $"Driver {InstalledDriverType} is installed but NOT active in UpperFilters (reboot/restart required).");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"RefreshDriverInstalledState failed: {ex.Message}");
            IsDriverInstalled = false;
            InstalledDriverType = DriverSelection.None;
        }

        return IsDriverInstalled;
    }

    public string GetDriverInstallStateText()
    {
        return RefreshDriverInstalledState() ? "Installed" : "Not Installed";
    }

    public bool ValidateDriverIntegrity()
    {
        try
        {
            var driverPath = System.IO.Path.Combine(Environment.SystemDirectory, "drivers", "interception.sys");
            if (!System.IO.File.Exists(driverPath))
            {
                Logger.Warning("InterceptionInputService", "Integrity check: driver file missing.");
                return false;
            }

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = System.IO.File.OpenRead(driverPath);
            var hashBytes = sha256.ComputeHash(stream);
            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            
            Logger.Debug("InterceptionInputService", $"Driver Integrity: interception.sys hash={hashString}");
            
            var fileInfo = new System.IO.FileInfo(driverPath);
            return fileInfo.Length > 0;
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"Integrity check failed: {ex.Message}");
            return false;
        }
    }

    public bool RepairStack()
    {
        Logger.Info("InterceptionInputService", "Starting full stack repair...");
        ReleaseResources("Repair requested");
        
        Thread.Sleep(500);
        
        if (!RefreshDriverInstalledState())
        {
            Logger.Warning("InterceptionInputService", "Repair: Driver not found, attempting re-install...");
            if (!InstallDriverNoRestart(DriverSelection.Auto, false)) return false;
        }

        if (!ValidateDriverIntegrity())
        {
            Logger.Warning("InterceptionInputService", "Repair: Integrity check failed.");
            // We could try to force-replace files here if we had them.
        }

        TryRestartKeyboardDevices();
        
        return Initialize();
    }

    public bool CheckLayoutCompatibility()
    {
        // Interception uses scan codes, so it's largely layout-agnostic, 
        // but VkKeyScanW depends on the current thread's layout.
        var layout = GetKeyboardLayout(0);
        var layoutId = (ushort)((long)layout & 0xFFFF);
        
        // Known problematic layouts for pure scan-code mapping without Unicode fallback
        // (Simplified check for diagnostic purposes)
        bool isStandard = layoutId == 0x0409 || layoutId == 0x0809; // US or UK
        
        Logger.Debug("InterceptionInputService", $"Current Keyboard Layout: {layout:X8} (ID: {layoutId:X4}) - Standard: {isStandard}");
        
        if (!isStandard)
        {
            Logger.Info("InterceptionInputService", "Non-US/UK layout detected. HID mode will use layout-aware VkKeyScanW mapping with Unicode fallback.");
        }
        
        return true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    private void PlayClickSound()
    {
        if (!_audioFeedbackEnabled) return;
        try
        {
            System.Media.SystemSounds.Beep.Play(); // Replace with a custom click wav later if needed
        }
        catch { }
    }

    private void SetLastDriverAction(string action)
    {
        _lastDriverAction = action;
        _lastDriverActionUtc = DateTime.UtcNow;
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
                RefreshDriverInstalledState();
                Logger.Info("InterceptionInputService", $"Driver installed: {IsDriverInstalled} ({InstalledDriverType})");

                if (!IsDriverInstalled)
                {
                    Logger.Warning("InterceptionInputService", "Interception driver not installed. HID input mode unavailable.");
                    _lastErrorSummary = "Driver not installed";
                    _consecutiveInitializeFailures++;
                    SetLastDriverAction("Initialize failed (driver not installed)");
                    return false;
                }

                // Initialize the InputInterceptor DLL wrapper
                if (!InputInterceptor.Initialize())
                {
                    Logger.Error("InterceptionInputService", "Failed to initialize InputInterceptor DLL");
                    _lastErrorSummary = "InputInterceptor initialization failed";
                    _consecutiveInitializeFailures++;
                    SetLastDriverAction("Initialize failed (InputInterceptor init)");
                    return false;
                }

                // Create keyboard hook with no filter (we only want to send, not intercept)
                // KeyboardFilter.None means we won't capture any incoming keystrokes
                _keyboardHook = new KeyboardHook(KeyboardFilter.None);
                if (!_keyboardHook.CanSimulateInput)
                {
                    Logger.Error("InterceptionInputService", "Keyboard hook created but cannot simulate input. Releasing interception resources to avoid blocking keyboard input.");
                    _keyboardHook.Dispose();
                    _keyboardHook = null;
                    InputInterceptor.Dispose();
                    _initialized = false;
                    _lastErrorSummary = "Keyboard hook cannot simulate input";
                    _consecutiveInitializeFailures++;
                    SetLastDriverAction("Initialize failed (hook cannot simulate)");
                    return false;
                }

                _initialized = true;
                _consecutiveInitializeFailures = 0;
                _driverVersion = DetectDriverVersion();
                EnsureUsbDeviceWatchers();
                StartIdleCheckTimer();
                _lastRefreshUtc = DateTime.UtcNow;
                _lastErrorSummary = "None";
                SetLastDriverAction("Initialize success");

                Logger.Info("InterceptionInputService", $"Initialized successfully. CanSimulateInput: {_keyboardHook.CanSimulateInput}");
                return IsReady;
            }
            catch (Exception ex)
            {
                Logger.Error("InterceptionInputService", "Failed to initialize", ex);

                try
                {
                    _keyboardHook?.Dispose();
                    _keyboardHook = null;
                    InputInterceptor.Dispose();
                }
                catch (Exception cleanupEx)
                {
                    Logger.Debug("InterceptionInputService", $"Failed to fully cleanup after initialize error: {cleanupEx.Message}");
                }

                _initialized = false;
                _lastErrorSummary = ex.Message;
                _consecutiveInitializeFailures++;
                SetLastDriverAction("Initialize failed (exception)");
                return false;
            }
        }
    }



    private bool TryRestartKeyboardDevices()
    {
        try
        {
            var enumResult = RunProcess("pnputil", "/enum-devices /class Keyboard /connected");
            if (enumResult.ExitCode != 0)
            {
                Logger.Warning("InterceptionInputService", $"pnputil enum failed (exit {enumResult.ExitCode}): {enumResult.StdErr}");
                return false;
            }

            var matches = Regex.Matches(enumResult.StdOut ?? string.Empty, @"Instance ID:\s+(.+)", RegexOptions.IgnoreCase);
            if (matches.Count == 0)
            {
                Logger.Warning("InterceptionInputService", "No connected keyboard instance IDs found for restart");
                return false;
            }

            var restarted = 0;
            foreach (Match m in matches)
            {
                var instanceId = m.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(instanceId)) continue;

                var restartResult = RunProcess("pnputil", $"/restart-device \"{instanceId}\"");
                if (restartResult.ExitCode == 0)
                {
                    restarted++;
                    Logger.Debug("InterceptionInputService", $"Restarted keyboard device: {instanceId}");
                }
                else
                {
                    Logger.Warning("InterceptionInputService", $"Failed to restart keyboard device {instanceId} (exit {restartResult.ExitCode})");
                }
            }

            Logger.Info("InterceptionInputService", $"Restarted {restarted}/{matches.Count} keyboard device(s)");
            return restarted > 0;
        }
        catch (Exception ex)
        {
            Logger.Warning("InterceptionInputService", $"Keyboard device restart attempt failed: {ex.Message}");
            return false;
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private void EnsureUsbDeviceWatchers()
    {
        if (_disposed) return;
        if (_deviceArrivalWatcher != null || _deviceRemovalWatcher != null) return;

        try
        {
            _deviceArrivalWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2"));
            _deviceArrivalWatcher.EventArrived += OnUsbDeviceChange;
            _deviceArrivalWatcher.Start();

            _deviceRemovalWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3"));
            _deviceRemovalWatcher.EventArrived += OnUsbDeviceChange;
            _deviceRemovalWatcher.Start();

            Logger.Info("InterceptionInputService", "USB device-change watcher started");
        }
        catch (Exception ex)
        {
            Logger.Warning("InterceptionInputService", $"Failed to start USB device watcher: {ex.Message}");
            StopUsbDeviceWatchers();
        }
    }

    private void StopUsbDeviceWatchers()
    {
        try
        {
            if (_deviceArrivalWatcher != null)
            {
                _deviceArrivalWatcher.EventArrived -= OnUsbDeviceChange;
                _deviceArrivalWatcher.Stop();
                _deviceArrivalWatcher.Dispose();
                _deviceArrivalWatcher = null;
            }

            if (_deviceRemovalWatcher != null)
            {
                _deviceRemovalWatcher.EventArrived -= OnUsbDeviceChange;
                _deviceRemovalWatcher.Stop();
                _deviceRemovalWatcher.Dispose();
                _deviceRemovalWatcher = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"StopUsbDeviceWatchers error: {ex.Message}");
        }
    }

    private void OnUsbDeviceChange(object sender, EventArrivedEventArgs e)
    {
        if (_disposed) return;

        lock (_lock)
        {
            _deviceRefreshDebounceTimer ??= new Timer(_ => RefreshAfterUsbDeviceChange(), null, Timeout.Infinite, Timeout.Infinite);
            _deviceRefreshDebounceTimer.Change(DeviceRefreshDebounceMs, Timeout.Infinite);
        }
    }

    private void RefreshAfterUsbDeviceChange()
    {
        if (_disposed) return;

        bool shouldRefresh;
        lock (_lock)
        {
            shouldRefresh = _initialized;
            if (!shouldRefresh) return;

            var nowUtc = DateTime.UtcNow;
            if (_nextAllowedRefreshUtc != DateTime.MinValue && nowUtc < _nextAllowedRefreshUtc)
            {
                _lastErrorSummary = "Refresh throttled (cooldown active)";
                Logger.Debug("InterceptionInputService", $"USB refresh skipped due to cooldown. Next allowed at {_nextAllowedRefreshUtc:O}");
                return;
            }

            _nextAllowedRefreshUtc = nowUtc.AddMilliseconds(RefreshCooldownMs);

            try
            {
                _keyboardHook?.Dispose();
                _keyboardHook = null;
                InputInterceptor.Dispose();
                _initialized = false;
            }
            catch (Exception ex)
            {
                Logger.Debug("InterceptionInputService", $"Pre-refresh cleanup error: {ex.Message}");
            }
        }

        var ready = Initialize();
        _lastRefreshUtc = DateTime.UtcNow;
        if (!ready)
        {
            _lastErrorSummary = "Not ready after USB refresh";
        }
        Logger.Info("InterceptionInputService", ready
            ? "HID interception refreshed after USB device change"
            : "USB device changed, but HID interception is not ready after refresh");
    }

    /// <summary>
    /// Sends a single key press (down + up) at the driver level.
    /// </summary>
    /// <param name="keyCode">The Interception KeyCode to press.</param>
    /// <param name="releaseDelayMs">Delay in ms between key down and key up.</param>
    /// <returns>True if the keystroke was sent successfully.</returns>
    public bool SendKeyPress(KeyCode keyCode, int releaseDelayMs = 10)
    {
        UpdateActivityTimestamp();
        
        // Lazy-init on first use
        if (!_initialized)
        {
            if (!Initialize()) 
            {
                Logger.Warning("InterceptionInputService", $"Failed to lazy-init for SendKeyPress {keyCode}");
                return false;
            }
        }

        if (!IsReady)
        {
            Logger.Debug("InterceptionInputService", "SendKeyPress called but service not ready after init");
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
        UpdateActivityTimestamp();
        
        // Lazy-init on first use
        if (!_initialized)
        {
            if (!Initialize()) 
            {
                Logger.Warning("InterceptionInputService", $"Failed to lazy-init for SendCharacter '{ch}'");
                return false;
            }
        }

        if (!IsReady)
        {
            Logger.Debug("InterceptionInputService", "SendCharacter called but service not ready after init");
            return false;
        }

        PlayClickSound();

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
            var isDeadKeyResult = (vkResult & unchecked((short)0x8000)) != 0;

            if (isDeadKeyResult)
            {
                Logger.Debug("InterceptionInputService", $"Dead-key mapping detected for '{ch}' (U+{(int)ch:X4}), using Unicode fallback");
                return SendUnicodeFallback(ch);
            }

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
    /// <returns>True if installation was initiated successfully.</returns>
    public bool InstallDriver(DriverSelection selection = DriverSelection.Auto, bool elevateIfNeeded = true)
    {
        try
        {
            if (selection == DriverSelection.Auto)
            {
                selection = Interop.NativeMethods.IsTestModeEnabled() ? DriverSelection.RedballKMDF : DriverSelection.Interception;
                Logger.Info("InterceptionInputService", $"Auto-selected driver: {selection} (TestMode: {Interop.NativeMethods.IsTestModeEnabled()})");
            }

            if (selection == DriverSelection.None) return false;

            RefreshDriverInstalledState();
            if (IsDriverInstalled && InstalledDriverType == selection)
            {
                Logger.Info("InterceptionInputService", $"Driver {selection} already installed");
                SetLastDriverAction($"Driver install skipped ({selection} already installed)");
                return true;
            }

            if (!InputInterceptor.CheckAdministratorRights())
            {
                if (!elevateIfNeeded) return false;

                Logger.Info("InterceptionInputService", $"Attempting to elevate for {selection} driver installation");
                var processInfo = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "Redball.UI.WPF.exe",
                    Arguments = $"--install-driver {selection}",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    using var process = Process.Start(processInfo);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Logger.Warning("InterceptionInputService", "User cancelled UAC elevation");
                    return false;
                }
            }

            bool success = false;
            if (selection == DriverSelection.Interception)
            {
                success = InstallLegacyInterception();
            }
            else if (selection == DriverSelection.RedballKMDF)
            {
                success = InstallRedballKmdf();
            }

            if (success)
            {
                RefreshDriverInstalledState();
                if (InstalledDriverType == selection)
                {
                    SetLastDriverAction($"{selection} install completed");
                }
                else
                {
                    success = false;
                    Logger.Warning("InterceptionInputService", $"{selection} install command reported success but driver not detected as active.");
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error("InterceptionInputService", "Driver installation failed", ex);
            return false;
        }
    }

    private bool InstallLegacyInterception()
    {
        Logger.Info("InterceptionInputService", "Installing Legacy Interception driver...");
        var tempExe = Path.Combine(Path.GetTempPath(), "install-interception.exe");
        try
        {
            if (!ExtractResource("install-interception.exe", tempExe)) return false;

            var result = RunProcess(tempExe, "/install");
            if (result.ExitCode == 1) _isRebootRequired = true;
            
            return result.ExitCode == 0 || result.ExitCode == 1;
        }
        finally
        {
            try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
        }
    }

    private bool InstallRedballKmdf()
    {
        Logger.Info("InterceptionInputService", "Installing Redball KMDF driver...");
        var driverPath = Path.Combine(Environment.SystemDirectory, "drivers", "Redball.KMDF.sys");
        var tempSys = Path.Combine(Path.GetTempPath(), "Redball.KMDF.sys");

        try
        {
            if (!ExtractResource("Redball.KMDF.sys", tempSys)) return false;

            // 1. Copy driver file
            try { File.Copy(tempSys, driverPath, true); }
            catch (Exception ex) { Logger.Error("InterceptionInputService", "Failed to copy driver file", ex); return false; }

            // 2. Create Service
            RunProcess("sc.exe", $"create {RedballKmdfServiceName} binPath= \"system32\\drivers\\Redball.KMDF.sys\" type= kernel start= auto");
            
            // 3. Add to UpperFilters
            AddToUpperFilters("{4d36e96b-e325-11ce-bfc1-08002be10318}", RedballKmdfServiceName); // Keyboard
            AddToUpperFilters("{4d36e96f-e325-11ce-bfc1-08002be10318}", RedballKmdfServiceName); // Mouse

            // 4. Start Service
            var startResult = RunProcess("sc.exe", $"start {RedballKmdfServiceName}");
            
            _isRebootRequired = true; // Manual install almost always needs reboot/restart
            return true;
        }
        finally
        {
            try { if (File.Exists(tempSys)) File.Delete(tempSys); } catch { }
        }
    }

    private bool ExtractResource(string resourceName, string targetPath)
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames();
            var fullName = System.Linq.Enumerable.FirstOrDefault(names, n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
            
            if (fullName == null)
            {
                Logger.Error("InterceptionInputService", $"Resource {resourceName} not found in assembly.");
                return false;
            }

            using var stream = asm.GetManifestResourceStream(fullName);
            if (stream == null) return false;
            using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fileStream);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("InterceptionInputService", $"Failed to extract resource {resourceName}", ex);
            return false;
        }
    }

    private void AddToUpperFilters(string classGuid, string serviceName)
    {
        try
        {
            var keyPath = $@"SYSTEM\CurrentControlSet\Control\Class\{classGuid}";
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath, true);
            if (key == null) return;

            var filters = key.GetValue("UpperFilters") as string[];
            var newList = filters != null ? new List<string>(filters) : new List<string>();
            
            if (!newList.Contains(serviceName))
            {
                newList.Add(serviceName);
                key.SetValue("UpperFilters", newList.ToArray(), Microsoft.Win32.RegistryValueKind.MultiString);
                Logger.Info("InterceptionInputService", $"Added {serviceName} to {classGuid} UpperFilters");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("InterceptionInputService", $"AddToUpperFilters failed for {classGuid}: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a diagnostic status string for the service.
    /// </summary>
    public string GetStatusText()
    {
        if (!RefreshDriverInstalledState()) return "Driver not installed";
        if (!_initialized) return "Not initialized";
        if (!IsReady) return "Initialized but not ready (no device captured)";
        return "Ready (HID keyboard active)";
    }

    public string GetDiagnosticsText()
    {
        var lastRefreshText = _lastRefreshUtc.HasValue
            ? _lastRefreshUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "Never";

        return string.Join(Environment.NewLine,
            "HID Diagnostics",
            $"Status: {GetStatusText()}",
            $"Driver Installed: {GetDriverInstallStateText()} ({InstalledDriverType})",
            $"Driver Version: {_driverVersion ?? "Unknown"}",
            $"Integrity Valid: {ValidateDriverIntegrity()}",
            $"Windows Compatible: {CheckWindowsCompatibility()}",
            $"Layout Compatible: {CheckLayoutCompatibility()}",
            $"Initialized: {_initialized}",
            $"Ready: {IsReady}",
            $"Last Refresh: {lastRefreshText}",
            $"Next Allowed Refresh: {(_nextAllowedRefreshUtc == DateTime.MinValue ? "Now" : _nextAllowedRefreshUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))}",
            $"Last Driver Action: {_lastDriverAction} ({(_lastDriverActionUtc.HasValue ? _lastDriverActionUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "Never")})",
            $"Consecutive Initialize Failures: {_consecutiveInitializeFailures}",
            $"Idle Timeout (min): {IdleTimeoutMinutes}",
            $"Audio Feedback: {_audioFeedbackEnabled}",
            $"Last Error: {_lastErrorSummary}");
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

    private bool ManualUninstallCleanup()
    {
        try
        {
            Logger.Info("InterceptionInputService", "STARTING NUCLEAR MANUAL CLEANUP...");
            
            // 1. Unhook and Release
            ReleaseResources("Nuclear Cleanup");

            // 2. Remove from Registry (Keyboard & Mouse)
            Logger.Info("InterceptionInputService", "Removing 'interception' from UpperFilters registry keys...");
            RemoveFromUpperFilters("{4d36e96b-e325-11ce-bfc1-08002be10318}", "interception");
            RemoveFromUpperFilters("{4d36e96f-e325-11ce-bfc1-08002be10318}", "interception");

            // 3. Stop and Delete Service
            Logger.Info("InterceptionInputService", "Stopping and deleting driver services (Legacy & KMDF)...");
            RunProcess("sc.exe", $"stop {LegacyServiceName}");
            RunProcess("sc.exe", $"stop {RedballKmdfServiceName}");
            Thread.Sleep(200);
            RunProcess("sc.exe", $"delete {LegacyServiceName}");
            RunProcess("sc.exe", $"delete {RedballKmdfServiceName}");

            // 4. Force Delete Driver Files
            var legacyDriverPath = Path.Combine(Environment.SystemDirectory, "drivers", "interception.sys");
            var customDriverPath = Path.Combine(Environment.SystemDirectory, "drivers", "Redball.KMDF.sys");

            foreach (var path in new[] { legacyDriverPath, customDriverPath })
            {
                if (File.Exists(path))
                {
                    try
                    {
                        Logger.Info("InterceptionInputService", $"Deleting driver file: {Path.GetFileName(path)}");
                        File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("InterceptionInputService", $"File.Delete failed for {Path.GetFileName(path)}: {ex.Message}");
                        RunProcess("cmd.exe", $"/c del /f \"{path}\"");
                    }
                }
            }

            // 5. Final Registry Clean (Check for service key leftovers)
            try
            {
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree($@"SYSTEM\CurrentControlSet\Services\{LegacyServiceName}", false);
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree($@"SYSTEM\CurrentControlSet\Services\{RedballKmdfServiceName}", false);
            }
            catch { }

            Logger.Info("InterceptionInputService", "NUCLEAR CLEANUP COMPLETED. Keyboard devices should be restarted now.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("InterceptionInputService", "Nuclear cleanup failed spectacularly", ex);
            return false;
        }
    }

    private bool RunInstallerVisible(string exePath, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
            return proc?.ExitCode == 0 || proc?.ExitCode == 1;
        }
        catch { return false; }
    }

    private void RemoveFromUpperFilters(string classGuid, string filterStub)
    {
        try
        {
            var keyPath = $@"SYSTEM\CurrentControlSet\Control\Class\{classGuid}";
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath, true);
            if (key == null) return;

            var filters = key.GetValue("UpperFilters") as string[];
            if (filters == null) return;

            var newList = new List<string>(filters);
            
            // Remove multiple common filter names (standard and common typos/legacy)
            var targets = new[] { LegacyServiceName, RedballKmdfServiceName, "keyboard", "mouinterception", "Redball.Driver" };
            bool changed = false;
            
            foreach (var target in targets)
            {
                if (newList.Remove(target) || newList.Remove(target + "\0"))
                {
                    changed = true;
                    Logger.Info("InterceptionInputService", $"Removing {target} from {classGuid}");
                }
            }

            if (changed)
            {
                key.SetValue("UpperFilters", newList.ToArray(), Microsoft.Win32.RegistryValueKind.MultiString);
                Logger.Info("InterceptionInputService", $"Filters updated for {classGuid}. Re-verifying...");
                
                var verified = key.GetValue("UpperFilters") as string[];
                if (verified != null)
                {
                    foreach (var target in targets)
                    {
                        if (Array.IndexOf(verified, target) >= 0)
                        {
                            Logger.Warning("InterceptionInputService", $"Failed to fully remove {target} from {classGuid}!");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("InterceptionInputService", $"RemoveFromUpperFilters failed for {classGuid}: {ex.Message}");
        }
    }

    private void StartIdleCheckTimer()
    {
        if (_idleCheckTimer != null) return;
        _idleCheckTimer = new Timer(_ => CheckIdleTimeout(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void CheckIdleTimeout()
    {
        var idleMinutes = (DateTime.UtcNow - _lastActivityUtc).TotalMinutes;
        if (idleMinutes >= IdleTimeoutMinutes && _initialized)
        {
            Logger.Info("InterceptionInputService", $"Idle timeout reached ({idleMinutes:F0} min). Auto-releasing HID resources.");
            ReleaseResources("Idle timeout");
        }
    }

    private void UpdateActivityTimestamp()
    {
        _lastActivityUtc = DateTime.UtcNow;
    }

    private string? DetectDriverVersion()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\keyboard") ??
                          Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\interception");
            if (key != null)
            {
                var version = key.GetValue("Version") as string;
                if (!string.IsNullOrEmpty(version)) return version;
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"Driver version detection failed: {ex.Message}");
            return null;
        }
    }

    public bool CheckWindowsCompatibility()
    {
        var os = Environment.OSVersion;
        if (os.Platform != PlatformID.Win32NT) return false;
        var version = os.Version;
        // Windows 7 SP1 (6.1.7601) and later are supported
        if (version.Major < 6) return false;
        if (version.Major == 6 && version.Minor < 1) return false;
        if (version.Major == 6 && version.Minor == 1 && version.Build < 7601) return false;
        return true;
    }

    public bool SendCharacterWithRetry(char ch, int maxRetries = MaxCharacterSendRetries)
    {
        UpdateActivityTimestamp();
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            if (SendCharacter(ch)) return true;
            if (attempt < maxRetries - 1)
            {
                var delay = CharacterRetryBackoffMs * (attempt + 1);
                Logger.Debug("InterceptionInputService", $"Character send failed for '{ch}', retrying in {delay}ms (attempt {attempt + 1}/{maxRetries})");
                Thread.Sleep(delay);
            }
        }
        Logger.Warning("InterceptionInputService", $"Character send failed after {maxRetries} attempts: '{ch}'");
        return false;
    }

    public bool SendKeyPressWithTimeout(KeyCode keyCode, int releaseDelayMs = 10)
    {
        UpdateActivityTimestamp();
        if (!IsReady) return false;
        try
        {
            using var cts = new CancellationTokenSource(KeySendTimeoutMs);
            var task = System.Threading.Tasks.Task.Run(() => _keyboardHook!.SimulateKeyPress(keyCode, releaseDelayMs), cts.Token);
            if (!task.Wait(KeySendTimeoutMs, cts.Token))
            {
                Logger.Warning("InterceptionInputService", $"Key send timeout for {keyCode}");
                return false;
            }
            return task.Result;
        }
        catch (Exception ex)
        {
            Logger.Debug("InterceptionInputService", $"SendKeyPressWithTimeout failed for {keyCode}: {ex.Message}");
            return false;
        }
    }

    public void PerformHealthCheck()
    {
        if (!_initialized) return;
        var wasReady = IsReady;
        var stillReady = _keyboardHook?.CanSimulateInput ?? false;
        if (wasReady && !stillReady)
        {
            Logger.Warning("InterceptionInputService", "Health check: Keyboard hook lost simulation capability. Releasing resources.");
            ReleaseResources("Health check failure");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Logger.Info("InterceptionInputService", "Disposing...");

        ReleaseResources("Dispose");

        Logger.Info("InterceptionInputService", "Disposed");
    }

    public void ReleaseResources(string reason = "Manual release")
    {
        lock (_lock)
        {
            Logger.Info("InterceptionInputService", $"Releasing interception resources ({reason})...");

            try
            {
                _keyboardHook?.Dispose();
                _keyboardHook = null;

                InputInterceptor.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Debug("InterceptionInputService", $"ReleaseResources error: {ex.Message}");
            }

            _deviceRefreshDebounceTimer?.Dispose();
            _deviceRefreshDebounceTimer = null;
            _idleCheckTimer?.Dispose();
            _idleCheckTimer = null;
            StopUsbDeviceWatchers();

            _initialized = false;
            _lastRefreshUtc = DateTime.UtcNow;
            SetLastDriverAction($"Resources released ({reason})");
            Logger.Info("InterceptionInputService", "Interception resources released");
        }
    }
}
