using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Partial class: TypeThing paste-as-typing logic, hotkey management, and P/Invoke helpers.
/// </summary>
public partial class MainWindow
{
    private readonly List<(string Preview, string FullText, DateTime Time)> _typeThingHistory = new();
    private const int MaxTypeThingHistory = 10;
    private bool _typeThingBusyNotificationShown;

    private void MarkTypeThingOperationStarted()
    {
        _isTyping = true;
        _typeThingBusyNotificationShown = false;
    }

    private void MarkTypeThingOperationFinished()
    {
        _isTyping = false;
        _typeThingBusyNotificationShown = false;
    }

    private void ShowTypeThingBusyNotificationOnce()
    {
        if (_typeThingBusyNotificationShown)
        {
            return;
        }

        _typeThingBusyNotificationShown = true;
        NotificationService.Instance.ShowWarning("TypeThing", "Already typing! Please wait for current operation to complete.");
    }

    private void AddToTypeThingHistory(string text)
    {
        var preview = text.Length > 80 ? text[..80].Replace("\r", "").Replace("\n", " ") + "..." : text.Replace("\r", "").Replace("\n", " ");
        _typeThingHistory.Insert(0, (preview, text, DateTime.Now));
        if (_typeThingHistory.Count > MaxTypeThingHistory)
            _typeThingHistory.RemoveAt(_typeThingHistory.Count - 1);
        RefreshTypeThingHistoryUI();
    }

    private void RefreshTypeThingHistoryUI()
    {
        if (TypeThingHistoryList == null) return;
        TypeThingHistoryList.Items.Clear();
        foreach (var entry in _typeThingHistory)
        {
            TypeThingHistoryList.Items.Add(new System.Windows.Controls.ListBoxItem
            {
                Content = $"[{entry.Time:HH:mm}] {entry.Preview}",
                Tag = entry.FullText,
                ToolTip = $"{entry.FullText.Length} characters"
            });
        }
    }

    private void TypeThingHistoryRetype_Click(object sender, RoutedEventArgs e)
    {
        if (TypeThingHistoryList.SelectedItem is System.Windows.Controls.ListBoxItem item && item.Tag is string text)
        {
            StartTypeThingFromText(text);
            _analytics.TrackFeature("typething.history_retype");
        }
    }

    private void RefreshTemplateCombo()
    {
        if (TemplateCombo == null) return;
        TemplateCombo.Items.Clear();
        foreach (var name in TemplateService.Instance.GetTemplateNames())
            TemplateCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = name });
    }

    private void TemplateType_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content is string name)
        {
            var text = TemplateService.Instance.GetTemplate(name);
            if (!string.IsNullOrEmpty(text))
            {
                StartTypeThingFromText(text);
                _analytics.TrackFeature("typething.template_typed");
            }
        }
    }

    private void TemplateSave_Click(object sender, RoutedEventArgs e)
    {
        var clipText = System.Windows.Clipboard.GetText();
        if (string.IsNullOrEmpty(clipText))
        {
            NotificationService.Instance.ShowWarning("Templates", "Clipboard is empty. Copy some text first.");
            return;
        }

        var name = ShowInputDialog("Save Template", "Enter a name for this template:", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        if (TemplateService.Instance.SaveTemplate(name, clipText))
        {
            RefreshTemplateCombo();
            NotificationService.Instance.ShowInfo("Templates", $"Template \"{name}\" saved ({clipText.Length} chars).");
            _analytics.TrackFeature("typething.template_saved");
        }
    }

    private void TemplateDelete_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content is string name)
        {
            if (TemplateService.Instance.DeleteTemplate(name))
            {
                RefreshTemplateCombo();
                NotificationService.Instance.ShowInfo("Templates", $"Template \"{name}\" deleted.");
                _analytics.TrackFeature("typething.template_deleted");
            }
        }
    }

    private void TypeThingDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            e.Effects = files?.Length == 1 && files[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void TypeThingDropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files?.Length != 1) return;

        var filePath = files[0];
        if (!filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            NotificationService.Instance.ShowWarning("TypeThing", "Only .txt files are supported.");
            return;
        }

        try
        {
            var text = System.IO.File.ReadAllText(filePath);
            if (string.IsNullOrEmpty(text))
            {
                NotificationService.Instance.ShowWarning("TypeThing", "The dropped file is empty.");
                return;
            }

            Logger.Info("MainWindow", $"TypeThing: File dropped — {filePath} ({text.Length} chars)");
            _analytics.TrackFeature("typething.file_dropped");
            StartTypeThingFromText(text);
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "TypeThing: Failed to read dropped file", ex);
            NotificationService.Instance.ShowError("TypeThing", $"Failed to read file: {ex.Message}");
        }
    }

    public void StartTypeThingFromText(string text)
    {
        if (_isTyping)
        {
            ShowTypeThingBusyNotificationOnce();
            return;
        }

        MarkTypeThingOperationStarted();
        Dispatcher.Invoke(() =>
        {
            try
            {
                var config = ConfigService.Instance.Config;
                if (!config.TypeThingEnabled)
                {
                    NotificationService.Instance.ShowWarning("TypeThing", "TypeThing is disabled in settings.");
                    MarkTypeThingOperationFinished();
                    return;
                }

                // Sanitise content — warn on sensitive patterns or excessive length
                var sanitiseWarnings = ClipboardSanitiser.Analyse(text);
                if (sanitiseWarnings.Count > 0)
                {
                    var warningMsg = string.Join("\n• ", sanitiseWarnings);
                    Logger.Warning("MainWindow", $"TypeThing: Content sanitisation warnings: {warningMsg}");

                    var result = NotificationWindow.Show(
                        "TypeThing — Content Warning",
                        $"⚠ Content warnings:\n\n• {warningMsg}\n\nDo you still want to type this content?",
                        "\uE7BA", // Warning icon
                        true);

                    if (!result)
                    {
                        MarkTypeThingOperationFinished();
                        return;
                    }
                }

                var avgDelayMs = (config.TypeThingMinDelayMs + config.TypeThingMaxDelayMs) / 2.0;
                var estimatedSeconds = (int)Math.Ceiling(text.Length * avgDelayMs / 1000.0);
                var preview = text.Length > 200 ? text[..200] + "..." : text;
                var previewMsg = $"Characters: {text.Length}\nEstimated time: ~{estimatedSeconds}s\n\nPreview:\n{preview}";

                var confirm = NotificationWindow.Show("TypeThing — Confirm Typing", previewMsg, "\uE946", true);

                if (!confirm)
                {
                    MarkTypeThingOperationFinished();
                    return;
                }

                var countdown = Math.Max(1, config.TypeThingStartDelaySec);
                _typeThingCountdownTimer?.Stop();
                _typeThingCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _typeThingCountdownTimer.Tick += (s, e) =>
                {
                    countdown--;
                    if (countdown <= 0)
                    {
                        _typeThingCountdownTimer?.Stop();
                        _typeThingCountdownTimer = null;
                        TypeText(text);
                    }
                };

                AddToTypeThingHistory(text);
                // Trigger text-to-speech if enabled
                TextToSpeechService.Instance.SpeakAsync(text);
                NotificationService.Instance.ShowInfo("TypeThing", $"Typing {text.Length} characters in {countdown} seconds... Switch to target window now!");
                _typeThingCountdownTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", "TypeThing file error", ex);
                MarkTypeThingOperationFinished();
            }
        });
    }

    public void StartTypeThing()
    {
        if (_isTyping)
        {
            Logger.Warning("MainWindow", "TypeThing already running, ignoring request");
            _analytics.TrackFeature("typething.rejected_busy");
            ShowTypeThingBusyNotificationOnce();
            return;
        }

        Logger.Info("MainWindow", "StartTypeThing called");
        _analytics.TrackFeature("typething.started");
        MarkTypeThingOperationStarted();
        Dispatcher.Invoke(() =>
        {
            try
            {
                var config = ConfigService.Instance.Config;
                if (!config.TypeThingEnabled)
                {
                    _analytics.TrackFeature("typething.blocked_disabled");
                    NotificationService.Instance.ShowWarning("TypeThing", "TypeThing is disabled in settings.");
                    MarkTypeThingOperationFinished();
                    return;
                }

                var clipboardText = System.Windows.Clipboard.GetText();
                if (string.IsNullOrEmpty(clipboardText))
                {
                    Logger.Warning("MainWindow", "TypeThing: Clipboard is empty");
                    _analytics.TrackFeature("typething.failed_empty_clipboard");
                    NotificationService.Instance.ShowWarning("TypeThing", "Clipboard is empty. Copy some text first.");
                    MarkTypeThingOperationFinished();
                    return;
                }

                Logger.Info("MainWindow", $"TypeThing: Got {clipboardText.Length} chars from clipboard");

                // Sanitise clipboard content — warn on sensitive patterns or excessive length
                var sanitiseWarnings = ClipboardSanitiser.Analyse(clipboardText);
                if (sanitiseWarnings.Count > 0)
                {
                    var warningMsg = string.Join("\n• ", sanitiseWarnings);
                    Logger.Warning("MainWindow", $"TypeThing: Clipboard sanitisation warnings: {warningMsg}");
                    _analytics.TrackFeature("typething.sanitise_warning");

                    var result = NotificationWindow.Show(
                        "TypeThing — Content Warning",
                        $"⚠ Clipboard content warnings:\n\n• {warningMsg}\n\nDo you still want to type this content?",
                        "\uE7BA", // Warning icon
                        true);

                    if (!result)
                    {
                        _analytics.TrackFeature("typething.sanitise_cancelled");
                        MarkTypeThingOperationFinished();
                        return;
                    }
                    _analytics.TrackFeature("typething.sanitise_accepted");
                }

                // Start typing after a short delay so user can switch to target window
                var countdown = Math.Max(1, config.TypeThingStartDelaySec);
                _typeThingCountdownTimer?.Stop();
                _typeThingCountdownTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _typeThingCountdownTimer.Tick += (s, e) =>
                {
                    countdown--;
                    if (countdown <= 0)
                    {
                        _typeThingCountdownTimer?.Stop();
                        _typeThingCountdownTimer = null;
                        Logger.Info("MainWindow", "TypeThing: Starting typing");
                        TypeText(clipboardText);
                    }
                    else
                    {
                        Logger.Debug("MainWindow", $"TypeThing: Starting in {countdown}...");
                    }
                };

                AddToTypeThingHistory(clipboardText);
                // Trigger text-to-speech if enabled
                TextToSpeechService.Instance.SpeakAsync(clipboardText);
                NotificationService.Instance.ShowInfo("TypeThing", $"Typing {clipboardText.Length} characters in {countdown} seconds... Switch to target window now!");
                _typeThingCountdownTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Error("MainWindow", "TypeThing error", ex);
                _analytics.TrackFeature("typething.failed_exception");
                CleanupTypeThingHidSession("TypeThing exception");
                MarkTypeThingOperationFinished();
            }
        });
    }

    private bool _useHidInput;

    private void TypeText(string text)
    {
        var isRdp = IsRemoteSession();
        var config = ConfigService.Instance.Config;

        // Determine input mode: HID (driver-level) or SendInput (default)
        _useHidInput = false;
        if (config.TypeThingHidSafeMode)
        {
            InterceptionInputService.Instance.ReleaseResources("HID Safe Mode active");
            Logger.Warning("MainWindow", "TypeThing: HID Safe Mode is active, forcing SendInput mode");
        }
        else
        if (Enum.TryParse<TypeThingInputMode>(config.TypeThingInputMode, true, out var inputMode) &&
            inputMode == TypeThingInputMode.HID)
        {
            var interception = InterceptionInputService.Instance;
            if (interception.IsReady || interception.Initialize())
            {
                _useHidInput = true;
                Logger.Info("MainWindow", "TypeThing: Using HID driver-level input (Interception)");
            }
            else
            {
                Logger.Warning("MainWindow", "TypeThing: HID mode requested but driver not available, falling back to SendInput");
                NotificationService.Instance.ShowWarning("TypeThing", $"HID not available ({interception.LastErrorSummary}). Falling back to SendInput mode.");

                if (interception.ConsecutiveInitializeFailures >= 3)
                {
                    config.TypeThingHidSafeMode = true;
                    config.TypeThingInputMode = "SendInput";
                    ConfigService.Instance.Save();
                    NotificationService.Instance.ShowWarning("HID Safe Mode", "Enabled automatically after repeated HID initialization failures. You can re-enable HID later in Settings.");
                }
            }
        }

        Logger.Info("MainWindow", $"TypeThing: Begin typing {text.Length} chars (RDP: {isRdp}, HID: {_useHidInput})");
        var index = 0;
        var minDelay = Math.Max(1, config.TypeThingMinDelayMs);
        var maxDelay = Math.Max(minDelay, config.TypeThingMaxDelayMs);
        _typeThingTimer?.Stop();
        _typeThingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_random.Next(minDelay, maxDelay + 1))
        };
        _typeThingTimer.Tick += (s, e) =>
        {
            if (!_isTyping)
            {
                _typeThingTimer?.Stop();
                _typeThingTimer = null;
                CleanupTypeThingHidSession("TypeThing stopped by user");
                Logger.Info("MainWindow", "TypeThing: Typing stopped by user");
                _analytics.TrackFeature("typething.stopped");
                NotificationService.Instance.ShowInfo("TypeThing", "Typing stopped.");
                return;
            }

            if (index >= text.Length)
            {
                _typeThingTimer?.Stop();
                _typeThingTimer = null;
                CleanupTypeThingHidSession("TypeThing complete");
                MarkTypeThingOperationFinished();
                Logger.Info("MainWindow", "TypeThing: Typing complete");
                _analytics.TrackFeature("typething.completed");
                NotificationService.Instance.ShowInfo("TypeThing", $"Done! Typed {text.Length} characters.");
                return;
            }

            var ch = text[index];
            if (ch == '\r')
            {
                // If next char is \n, skip \r (the \n will send Enter)
                // If standalone \r, treat it as a newline
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    // Skip — the following \n will handle it
                }
                else if (config.TypeThingTypeNewlines)
                {
                    SendKeyPress(0x0D); // VK_RETURN
                }
                // else: TypeThingTypeNewlines is false, skip silently
            }
            else if (ch == '\n')
            {
                if (config.TypeThingTypeNewlines)
                {
                    SendKeyPress(0x0D); // VK_RETURN
                }
                // else: skip silently (don't fall through to SendCharacter)
            }
            else if (ch == '\t')
            {
                SendKeyPress(0x09); // VK_TAB
            }
            else
            {
                // Use retry logic for HID to improve reliability
                if (_useHidInput)
                {
                    if (!InterceptionInputService.Instance.SendCharacterWithRetry(ch))
                    {
                        Logger.Warning("MainWindow", $"TypeThing: HID SendCharacterWithRetry failed for '{ch}', falling back to SendInput");
                        SendCharacter(ch);
                    }
                }
                else
                {
                    SendCharacter(ch);
                }
            }
            index++;

            // Show progress notification every 100 characters
            if (index % 100 == 0 && index < text.Length)
            {
                NotificationService.Instance.ShowInfo("TypeThing", $"Typing progress: {index}/{text.Length} characters...");
            }

            // Randomize interval for human-like typing
            if (config.TypeThingAddRandomPauses && _random.Next(1, 101) <= Math.Max(0, config.TypeThingRandomPauseChance))
            {
                var extraPause = _random.Next(50, Math.Max(51, config.TypeThingRandomPauseMaxMs + 1));
                _typeThingTimer.Interval = TimeSpan.FromMilliseconds(Math.Min(maxDelay + extraPause, maxDelay + config.TypeThingRandomPauseMaxMs));
            }
            else
            {
                _typeThingTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(minDelay, maxDelay + 1));
            }
        };
        _typeThingTimer.Start();
    }

    public void StopTypeThing()
    {
        Logger.Info("MainWindow", "StopTypeThing called");
        Dispatcher.Invoke(() =>
        {
            if (!_isTyping && _typeThingCountdownTimer == null && _typeThingTimer == null)
            {
                Logger.Debug("MainWindow", "TypeThing is not active; nothing to stop");
                return;
            }

            _typeThingCountdownTimer?.Stop();
            _typeThingCountdownTimer = null;
            _typeThingTimer?.Stop();
            _typeThingTimer = null;
            CleanupTypeThingHidSession("StopTypeThing requested");
            MarkTypeThingOperationFinished();
            _analytics.TrackFeature("typething.stop_requested");
            NotificationService.Instance.ShowInfo("TypeThing", "TypeThing stopped.");
        });
    }

    private void CleanupTypeThingHidSession(string reason)
    {
        if (!_useHidInput)
        {
            return;
        }

        try
        {
            InterceptionInputService.Instance.ReleaseResources(reason);
        }
        catch (Exception ex)
        {
            Logger.Debug("MainWindow", $"CleanupTypeThingHidSession failed: {ex.Message}");
        }
        finally
        {
            _useHidInput = false;
        }
    }

    public void ReloadHotkeys()
    {
        Logger.Info("MainWindow", "Reloading hotkeys from config...");
        try
        {
            _hotkeyService?.Dispose();
            SetupGlobalHotkeys();
            Logger.Info("MainWindow", "Hotkeys reloaded successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to reload hotkeys", ex);
        }
    }

    public void SuspendHotkeys()
    {
        Logger.Info("MainWindow", "Suspending hotkeys for key capture...");
        try
        {
            _hotkeyService?.Dispose();
            _hotkeyService = null;
            Logger.Info("MainWindow", "Hotkeys suspended");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to suspend hotkeys", ex);
        }
    }

    public void ResumeHotkeys()
    {
        Logger.Info("MainWindow", "Resuming hotkeys after key capture...");
        SetupGlobalHotkeys();
    }

    private void SetupGlobalHotkeys()
    {
        Logger.Info("MainWindow", "Setting up global hotkeys...");
        try
        {
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource == null)
            {
                Logger.Warning("MainWindow", "HwndSource not available for hotkey registration");
                return;
            }
            Logger.Debug("MainWindow", $"HwndSource obtained: {hwndSource.Handle}");

            _hotkeyService = new HotkeyService(hwndSource);

            // Register Ctrl+Alt+Pause to toggle active state
            Logger.Debug("MainWindow", "Registering Ctrl+Alt+Pause hotkey...");
            _hotkeyService.RegisterHotkey(1, HotkeyService.MOD_CONTROL | HotkeyService.MOD_ALT, 0x13 /* VK_PAUSE */, () =>
            {
                Logger.Info("MainWindow", "Hotkey: Ctrl+Alt+Pause - Toggle active");
                _viewModel?.ToggleActiveCommand.Execute(null);
            });

            // Register TypeThing start hotkey from config
            var startHotkey = string.IsNullOrWhiteSpace(ConfigService.Instance.Config.TypeThingStartHotkey) ? "Ctrl+Shift+V" : ConfigService.Instance.Config.TypeThingStartHotkey;
            Logger.Debug("MainWindow", $"TypeThing start hotkey from config: {startHotkey}");
            var (startMods, startKey) = HotkeyService.ParseHotkey(startHotkey);
            if (startKey != 0)
            {
                _hotkeyService.RegisterHotkey(100, startMods, startKey, () =>
                {
                    Logger.Info("MainWindow", $"Hotkey: {startHotkey} - TypeThing start");
                    StartTypeThing();
                });
            }
            else
            {
                Logger.Warning("MainWindow", $"Could not parse TypeThing start hotkey: {startHotkey}");
            }

            // Register TypeThing stop hotkey from config
            var stopHotkey = string.IsNullOrWhiteSpace(ConfigService.Instance.Config.TypeThingStopHotkey) ? "Ctrl+Shift+X" : ConfigService.Instance.Config.TypeThingStopHotkey;
            Logger.Debug("MainWindow", $"TypeThing stop hotkey from config: {stopHotkey}");
            var (stopMods, stopKey) = HotkeyService.ParseHotkey(stopHotkey);
            if (stopKey != 0)
            {
                _hotkeyService.RegisterHotkey(101, stopMods, stopKey, () =>
                {
                    Logger.Info("MainWindow", $"Hotkey: {stopHotkey} - TypeThing stop");
                    StopTypeThing();
                });
            }

            // Emergency HID release hotkey: Ctrl+Shift+Esc
            _hotkeyService.RegisterHotkey(102, HotkeyService.MOD_CONTROL | HotkeyService.MOD_SHIFT, 0x1B /* VK_ESCAPE */, () =>
            {
                Logger.Warning("MainWindow", "Hotkey: Ctrl+Shift+Esc - Emergency HID release requested");
                EmergencyReleaseHid("Hotkey Ctrl+Shift+Esc", true);
            });

            Logger.Info("MainWindow", "Global hotkeys registered successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to register global hotkeys", ex);
        }
    }

    public void ExitApplication()
    {
        Logger.Info("MainWindow", "ExitApplication called");
        _isExiting = true;
        Logger.Info("MainWindow", "Shutting down application");
        Application.Current.Shutdown();
    }

    public void EmergencyReleaseHid(string source, bool showNotification)
    {
        try
        {
            StopTypeThing();
            InterceptionInputService.Instance.ReleaseResources($"Emergency release ({source})");
            _useHidInput = false;

            if (showNotification)
            {
                NotificationService.Instance.ShowWarning("HID Emergency Release", "HID resources released. Physical keyboard control should be restored.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Emergency HID release failed", ex);
            if (showNotification)
            {
                NotificationService.Instance.ShowError("HID Emergency Release", "Failed to release HID resources. Check logs for details.");
            }
        }
    }

    #region P/Invoke SendInput helpers

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    private const int SM_REMOTESESSION = 0x1000;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static bool IsRemoteSession()
    {
        return GetSystemMetrics(SM_REMOTESESSION) != 0;
    }

    private static INPUT MakeScanCodeInput(ushort scanCode, uint extraFlags = 0)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki.wScan = scanCode;
        input.u.ki.dwFlags = KEYEVENTF_SCANCODE | extraFlags;
        return input;
    }

    private void SendKeyPress(ushort vk)
    {
        // Route through Interception driver if HID mode is active
        if (_useHidInput)
        {
            if (InterceptionInputService.Instance.SendVirtualKey(vk))
                return;
            Logger.Debug("MainWindow", $"TypeThing: HID SendVirtualKey failed for VK 0x{vk:X4}, falling back to SendInput");
        }

        // Send using wVk (virtual key code) for control keys like Enter/Tab.
        // KEYEVENTF_SCANCODE tells Windows to ignore wVk, which causes many apps
        // to silently drop the keystroke for non-printable keys. Populate wScan
        // as a hint but let wVk drive the input.
        var scan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC);
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = vk;
        inputs[0].u.ki.wScan = scan;
        inputs[0].u.ki.dwFlags = 0;
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = vk;
        inputs[1].u.ki.wScan = scan;
        inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private void SendCharacter(char ch)
    {
        // Route through Interception driver if HID mode is active
        if (_useHidInput)
        {
            if (InterceptionInputService.Instance.SendCharacter(ch))
                return;
            Logger.Debug("MainWindow", $"TypeThing: HID SendCharacter failed for '{ch}', falling back to SendInput");
        }

        // Try hardware scan code approach — works with VMware, fullscreen/maximized apps
        var vkResult = VkKeyScanW(ch);
        var isDeadKeyResult = (vkResult & unchecked((short)0x8000)) != 0;

        if (isDeadKeyResult)
        {
            Logger.Debug("MainWindow", $"TypeThing: Dead-key mapping detected for '{ch}' (U+{(int)ch:X4}), using Unicode fallback");
            vkResult = -1;
        }

        if (vkResult != -1)
        {
            var vk = (byte)(vkResult & 0xFF);
            var shiftState = (byte)((vkResult >> 8) & 0xFF);
            var scan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC);

            if (scan != 0)
            {
                var needShift = (shiftState & 1) != 0;
                var needCtrl = (shiftState & 2) != 0;
                var needAlt = (shiftState & 4) != 0;

                var inputList = new List<INPUT>();

                // Press modifiers
                if (needShift) inputList.Add(MakeScanCodeInput((ushort)MapVirtualKeyW(VK_SHIFT, MAPVK_VK_TO_VSC)));
                if (needCtrl) inputList.Add(MakeScanCodeInput((ushort)MapVirtualKeyW(VK_CONTROL, MAPVK_VK_TO_VSC)));
                if (needAlt) inputList.Add(MakeScanCodeInput((ushort)MapVirtualKeyW(VK_MENU, MAPVK_VK_TO_VSC)));

                // Key down + up
                inputList.Add(MakeScanCodeInput(scan));
                inputList.Add(MakeScanCodeInput(scan, KEYEVENTF_KEYUP));

                // Release modifiers (reverse order)
                if (needAlt) inputList.Add(MakeScanCodeInput((ushort)MapVirtualKeyW(VK_MENU, MAPVK_VK_TO_VSC), KEYEVENTF_KEYUP));
                if (needCtrl) inputList.Add(MakeScanCodeInput((ushort)MapVirtualKeyW(VK_CONTROL, MAPVK_VK_TO_VSC), KEYEVENTF_KEYUP));
                if (needShift) inputList.Add(MakeScanCodeInput((ushort)MapVirtualKeyW(VK_SHIFT, MAPVK_VK_TO_VSC), KEYEVENTF_KEYUP));

                var inputs = inputList.ToArray();
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
                return;
            }
        }

        // Fallback: Unicode SendInput for chars not on current keyboard layout
        var uInputs = new INPUT[2];
        uInputs[0].type = INPUT_KEYBOARD;
        uInputs[0].u.ki.wScan = (ushort)ch;
        uInputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;
        uInputs[1].type = INPUT_KEYBOARD;
        uInputs[1].u.ki.wScan = (ushort)ch;
        uInputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        SendInput(2, uInputs, Marshal.SizeOf<INPUT>());
    }

    #endregion
}
