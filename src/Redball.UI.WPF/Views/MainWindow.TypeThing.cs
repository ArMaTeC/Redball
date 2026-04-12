using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Clipboard history item with blur/reveal support
/// </summary>
public class ClipboardHistoryItem : INotifyPropertyChanged
{
    private bool _isRevealed;
    private DispatcherTimer? _revealTimer;

    public string Preview { get; set; } = "";
    public string FullText { get; set; } = "";
    public DateTime Time { get; set; }

    public string DisplayText => $"[{Time:HH:mm}] {Preview}";

    public string BlurredText
    {
        get
        {
            if (string.IsNullOrEmpty(Preview)) return "[empty]";
            var visibleChars = Math.Min(3, Preview.Length);
            var hiddenLength = Preview.Length - visibleChars;
            return $"[{Time:HH:mm}] {Preview[..visibleChars]}{new string('*', Math.Min(hiddenLength, 20))}...";
        }
    }

    public bool IsRevealed
    {
        get => _isRevealed;
        set
        {
            if (_isRevealed != value)
            {
                _isRevealed = value;
                OnPropertyChanged(nameof(IsRevealed));
            }
        }
    }

    public void RevealFor(int seconds)
    {
        _revealTimer?.Stop();
        IsRevealed = true;

        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _revealTimer.Tick += (s, e) =>
        {
            _revealTimer.Stop();
            IsRevealed = false;
        };
        _revealTimer.Start();
    }

    public void Hide()
    {
        _revealTimer?.Stop();
        IsRevealed = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Partial class: TypeThing paste-as-typing logic, hotkey management, and P/Invoke helpers.
/// </summary>
public partial class MainWindow
{
    private readonly List<ClipboardHistoryItem> _typeThingHistory = new();
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
        var item = new ClipboardHistoryItem
        {
            Preview = preview,
            FullText = text,
            Time = DateTime.Now
        };
        _typeThingHistory.Insert(0, item);
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
            TypeThingHistoryList.Items.Add(entry);
        }
    }

    private void TypeThingHistoryReveal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is ClipboardHistoryItem item)
        {
            var duration = (int)(ClipboardRevealDurationSlider?.Value ?? 10);
            item.RevealFor(duration);
            _analytics.TrackFeature("typething.history_reveal");
        }
    }

    private void TypeThingHistoryRetype_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is ClipboardHistoryItem item)
        {
            StartTypeThingFromText(item.FullText);
            _analytics.TrackFeature("typething.history_retype");
        }
    }

    private void TypeThingHistoryClear_Click(object sender, RoutedEventArgs e)
    {
        // Hide all revealed items first
        foreach (var item in _typeThingHistory)
        {
            item.Hide();
        }
        _typeThingHistory.Clear();
        RefreshTypeThingHistoryUI();
        _analytics.TrackFeature("typething.history_clear");
    }

    private void ClipboardRevealDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ClipboardRevealDurationText != null)
        {
            ClipboardRevealDurationText.Text = $"Reveal for: {(int)e.NewValue} seconds";
        }
    }

    private void RefreshTemplateCombo()
    {
        if (TemplateCombo == null) return;
        var selected = TemplateCombo.SelectedItem;
        TemplateCombo.Items.Clear();
        foreach (var name in TemplateService.Instance.GetTemplateNames())
            TemplateCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = name });
        
        // Try to restore selection or clear preview if no templates
        if (selected != null && TemplateCombo.Items.Count > 0)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in TemplateCombo.Items)
            {
                if (item.Content?.ToString() == selected.ToString())
                {
                    TemplateCombo.SelectedItem = item;
                    break;
                }
            }
        }
        else
        {
            UpdateTemplatePreview(null);
        }
    }

    private void TemplateCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TemplateCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content is string name)
        {
            UpdateTemplatePreview(name);
        }
        else
        {
            UpdateTemplatePreview(null);
        }
    }

    private void UpdateTemplatePreview(string? templateName)
    {
        if (TemplatePreviewText == null) return;
        
        if (string.IsNullOrEmpty(templateName))
        {
            TemplatePreviewText.Text = "Select a template to preview its content...";
            if (TemplatePreviewStats != null)
                TemplatePreviewStats.Text = "";
            return;
        }

        var content = TemplateService.Instance.GetTemplate(templateName);
        if (string.IsNullOrEmpty(content))
        {
            TemplatePreviewText.Text = "(empty template)";
            if (TemplatePreviewStats != null)
                TemplatePreviewStats.Text = "0 characters";
        }
        else
        {
            // Show first 500 chars with ellipsis if longer
            const int maxPreviewLength = 500;
            if (content.Length > maxPreviewLength)
            {
                TemplatePreviewText.Text = content[..maxPreviewLength] + "\n... (truncated for preview)";
            }
            else
            {
                TemplatePreviewText.Text = content;
            }
            
            if (TemplatePreviewStats != null)
            {
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                TemplatePreviewStats.Text = $"{content.Length} characters | {lines} lines";
            }
        }
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
                CleanupTypeThingServiceSession("TypeThing exception");
                MarkTypeThingOperationFinished();
            }
        });
    }

    private bool _useServiceInput;

    private void TypeText(string text)
    {
        var isRdp = IsRemoteSession();
        var config = ConfigService.Instance.Config;

        // Determine input mode: Service or SendInput (default)
        _useServiceInput = false;
        
        if (Enum.TryParse<TypeThingInputMode>(config.TypeThingInputMode, true, out var inputMode))
        {
            if (inputMode == TypeThingInputMode.Service)
            {
                var serviceProvider = ServiceInputProvider.Instance;
                if (serviceProvider.IsReady || serviceProvider.Initialize())
                {
                    _useServiceInput = true;
                    Logger.Info("MainWindow", "TypeThing: Using Windows Service-based input");
                }
                else
                {
                    Logger.Warning("MainWindow", $"TypeThing: Service mode requested but not available ({serviceProvider.LastErrorSummary}), falling back to SendInput");
                    NotificationService.Instance.ShowWarning("TypeThing", $"Service input not available ({serviceProvider.LastErrorSummary}). Falling back to SendInput mode.");
                    
                    if (serviceProvider.ConsecutiveFailures >= 3)
                    {
                        config.TypeThingInputMode = "SendInput";
                        ConfigService.Instance.Save();
                        NotificationService.Instance.ShowWarning("Service Input", "Switched to SendInput after repeated service initialization failures. Install the service to use this feature.");
                    }
                }
            }
        }

        Logger.Info("MainWindow", $"TypeThing: Begin typing {text.Length} chars (RDP: {isRdp}, Service: {_useServiceInput})");
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
                HideTypeThingProgress();
                CleanupTypeThingServiceSession("TypeThing stopped by user");
                Logger.Info("MainWindow", "TypeThing: Typing stopped by user");
                _analytics.TrackFeature("typething.stopped");
                NotificationService.Instance.ShowInfo("TypeThing", "Typing stopped.");
                return;
            }

            if (index >= text.Length)
            {
                _typeThingTimer?.Stop();
                _typeThingTimer = null;
                HideTypeThingProgress();
                CleanupTypeThingServiceSession("TypeThing complete");
                MarkTypeThingOperationFinished();
                Logger.Info("MainWindow", "TypeThing: Typing complete");
                _analytics.TrackFeature("typething.completed");
                
                // Report stats to ViewModel for home tab display
                _viewModel?.ReportTypeThingUsage(text.Length);
                
                NotificationService.Instance.ShowInfo("TypeThing", $"Done! Typed {text.Length} characters.");
                return;
            }

            // Update progress UI
            UpdateTypeThingProgress(index, text.Length);

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
                index++;
            }
            else if (ch == '\n')
            {
                if (config.TypeThingTypeNewlines)
                {
                    SendKeyPress(0x0D); // VK_RETURN
                }
                // else: skip silently (don't fall through to SendCharacter)
                index++;
            }
            else if (ch == '\t')
            {
                SendKeyPress(0x09); // VK_TAB
                index++;
            }
            else
            {
                // Use retry logic for Service to improve reliability
                if (_useServiceInput)
                {
                    if (!ServiceInputProvider.Instance.SendCharacterWithRetry(ch))
                    {
                        Logger.Warning("MainWindow", $"TypeThing: Service SendCharacterWithRetry failed for '{ch}', attempting one silent re-init");
                        
                        // Attempt one silent re-initialization
                        ServiceInputProvider.Instance.ReleaseResources("Silent re-init on failure");
                        if (ServiceInputProvider.Instance.Initialize())
                        {
                            Logger.Info("MainWindow", "TypeThing: Service silent re-init successful, retrying character");
                            if (ServiceInputProvider.Instance.SendCharacterWithRetry(ch))
                            {
                                goto character_sent;
                            }
                        }

                        Logger.Warning("MainWindow", $"TypeThing: Service re-init failed or send still failed, falling back to SendInput");
                        SendCharacter(ch);
                    }
                }
                else
                {
                    SendCharacter(ch);
                }

                character_sent:
                index++;
            }

            // Show progress notification every 100 characters — but only the first one to avoid spam
            if (index == 100 && text.Length > 100)
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
        // Show progress UI for long strings
        if (text.Length > 50)
        {
            ShowTypeThingProgress(text.Length);
        }

        _typeThingTimer.Start();
    }

    private void ShowTypeThingProgress(int totalChars)
    {
        if (TypeThingProgressOverlay != null)
        {
            TypeThingProgressOverlay.Visibility = Visibility.Visible;
        }
        if (TypeThingProgressBar != null)
        {
            TypeThingProgressBar.Maximum = totalChars;
            TypeThingProgressBar.Value = 0;
        }
        if (TypeThingProgressText != null)
        {
            TypeThingProgressText.Text = $"0 / {totalChars} characters";
        }
    }

    private void UpdateTypeThingProgress(int current, int total)
    {
        if (TypeThingProgressBar != null)
        {
            TypeThingProgressBar.Value = current;
        }
        if (TypeThingProgressText != null)
        {
            TypeThingProgressText.Text = $"{current} / {total} characters";
        }
    }

    private void HideTypeThingProgress()
    {
        if (TypeThingProgressOverlay != null)
        {
            TypeThingProgressOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void TypeThingCancel_Click(object sender, RoutedEventArgs e)
    {
        StopTypeThing();
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
            HideTypeThingProgress();
            CleanupTypeThingServiceSession("StopTypeThing requested");
            MarkTypeThingOperationFinished();
            _analytics.TrackFeature("typething.stop_requested");
            NotificationService.Instance.ShowInfo("TypeThing", "TypeThing stopped.");
        });
    }

    private void CleanupTypeThingServiceSession(string reason)
    {
        if (!_useServiceInput)
        {
            return;
        }

        try
        {
            ServiceInputProvider.Instance.ReleaseResources(reason);
        }
        catch (Exception ex)
        {
            Logger.Debug("MainWindow", $"CleanupTypeThingServiceSession failed: {ex.Message}");
        }
        finally
        {
            _useServiceInput = false;
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

    #region P/Invoke SendInput helpers

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanW(char ch);
#pragma warning restore SYSLIB1054

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

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
        // Route through service provider if active
        if (_useServiceInput)
        {
            if (ServiceInputProvider.Instance.SendVirtualKey(vk))
                return;
            Logger.Debug("MainWindow", $"TypeThing: Service SendVirtualKey failed for VK 0x{vk:X4}, falling back to SendInput");
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
        // SECURITY: Use validated SendInput with buffer size check
        SendInputSafe(inputs);
    }

    private void SendCharacter(char ch)
    {
        // Route through service provider if active
        if (_useServiceInput)
        {
            if (ServiceInputProvider.Instance.SendCharacter(ch))
                return;
            Logger.Debug("MainWindow", $"TypeThing: Service SendCharacter failed for '{ch}', falling back to SendInput");
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
                // SECURITY: Use validated SendInput with buffer size check
                SendInputSafe(inputs);
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
        // SECURITY: Use validated SendInput with buffer size check
        SendInputSafe(uInputs);
    }

    #endregion
}
