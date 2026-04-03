using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Partial class: Tray icon setup, refresh, recovery, and WndProc handling.
/// </summary>
public partial class MainWindow
{
    private Icon? _originalTrayIcon;
    private Icon? _generatedTrayIcon;
    private bool _isRecreatingTrayIcon;
    private DispatcherTimer? _animationTimer;
    private int _animationFrame = 0;
    private const int AnimationMaxFrames = 10;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Check for TaskbarCreated message (Explorer restart)
        if (msg == _taskbarCreatedMsg)
        {
            Logger.Info("MainWindow", "TaskbarCreated message received - Explorer likely restarted, recreating tray icon");
            handled = true;
            // Recreate tray icon with delay to ensure Explorer is ready
            var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            delayTimer.Tick += async (_, _) =>
            {
                delayTimer.Stop();
                try
                {
                    await RecreateTrayIconAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "Failed to recreate tray icon after Explorer restart", ex);
                }
            };
            delayTimer.Start();
        }
        return IntPtr.Zero;
    }

    private async Task RecreateTrayIconAsync()
    {
        if (_isRecreatingTrayIcon)
        {
            Logger.Debug("MainWindow", "Tray icon recreation already in progress, skipping duplicate request");
            return;
        }

        _isRecreatingTrayIcon = true;
        Logger.Info("MainWindow", "Recreating tray icon...");
        try
        {
            // Dispose existing tray icon
            if (_trayIcon != null)
            {
                if (_generatedTrayIcon != null)
                {
                    _generatedTrayIcon.Dispose();
                    _generatedTrayIcon = null;
                }

                _trayIcon.Visibility = Visibility.Collapsed;
                _trayIcon = null;
                Logger.Debug("MainWindow", "Existing tray icon hidden");
            }

            _isTrayIconInitialized = false;

            // Small async delay to ensure cleanup without blocking the UI thread
            await Task.Delay(100);

            // Re-setup tray icon
            SetupTrayIcon();

            if (_trayIcon != null)
            {
                // Force refresh by toggling visibility
                _trayIcon.Visibility = Visibility.Collapsed;
                _trayIcon.Visibility = Visibility.Visible;
                Logger.Info("MainWindow", "Tray icon recreated successfully");
            }
            else
            {
                Logger.Warning("MainWindow", "Tray icon recreation failed - will retry on next timer tick");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Error recreating tray icon", ex);
        }
        finally
        {
            _isRecreatingTrayIcon = false;
        }
    }

    private void SetupTrayIconRefreshTimer()
    {
        Logger.Info("MainWindow", "Setting up tray icon refresh timer...");
        try
        {
            _trayIconRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30) // Check every 30 seconds
            };

            int retryCount = 0;
            const int maxRetries = 3;

            _trayIconRefreshTimer.Tick += async (s, e) =>
            {
                try
                {
                    // Check if tray icon needs refreshing
                    if (_trayIcon == null || !_isTrayIconInitialized)
                    {
                        retryCount++;
                        if (retryCount <= maxRetries)
                        {
                            Logger.Warning("MainWindow", $"Tray icon not initialized, attempt {retryCount}/{maxRetries} to recreate...");
                            await RecreateTrayIconAsync();
                        }
                        else
                        {
                            Logger.Error("MainWindow", "Max tray icon retry attempts reached, giving up until next timer cycle");
                            retryCount = 0; // Reset for next cycle
                        }
                    }
                    else
                    {
                        // Icon exists, ensure visibility is set correctly
                        if (_trayIcon.Visibility != Visibility.Visible)
                        {
                            Logger.Warning("MainWindow", "Tray icon visibility was not Visible, correcting...");
                            _trayIcon.Visibility = Visibility.Visible;
                        }
                        retryCount = 0; // Reset counter on success
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "Error in tray icon refresh timer", ex);
                }
            };

            _trayIconRefreshTimer.Start();
            Logger.Info("MainWindow", "Tray icon refresh timer started (30s interval)");

            SetupAnimationTimer();
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to setup tray icon refresh timer", ex);
        }
    }

    private void SetupAnimationTimer()
    {
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200) // 5 FPS for smooth pulsing
        };
        _animationTimer.Tick += (s, e) =>
        {
            if (KeepAwakeService.Instance.IsActive)
            {
                _animationFrame = (_animationFrame + 1) % AnimationMaxFrames;
                UpdateTrayIconState(true);
            }
            else
            {
                _animationFrame = 0;
            }
        };
        _animationTimer.Start();
    }

    private void SetupTrayIcon()
    {
        // Prevent duplicate initialization
        if (_isTrayIconInitialized)
        {
            Logger.Debug("MainWindow", "Tray icon already initialized, skipping");
            return;
        }

        Logger.Info("MainWindow", "Setting up tray icon...");

        _isTrayIconInitialized = true;

        // Tray icon is defined in XAML, ensure it's properly initialized
        _trayIcon = TrayIcon;
        Logger.Debug("MainWindow", $"TrayIcon from XAML: {_trayIcon != null}");

        if (_trayIcon != null)
        {
            // Load icon from multiple possible locations
            var iconPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "redball.ico"),
                Path.Combine(AppContext.BaseDirectory, "redball.ico"),
                Path.Combine(Environment.CurrentDirectory, "Assets", "redball.ico"),
                Path.Combine(Environment.CurrentDirectory, "redball.ico")
            };

            string? foundPath = null;
            foreach (var path in iconPaths)
            {
                bool exists = File.Exists(path);
                Logger.Verbose("MainWindow", $"Checking icon at: {path} - Exists: {exists}");
                if (exists)
                {
                    foundPath = path;
                    break;
                }
            }

            if (foundPath != null)
            {
                try
                {
                    Logger.Info("MainWindow", $"Loading icon from: {foundPath}");
                    _generatedTrayIcon?.Dispose();
                    _generatedTrayIcon = null;
                    _originalTrayIcon?.Dispose();
                    var icon = new System.Drawing.Icon(foundPath);
                    _trayIcon.Icon = icon;
                    Logger.Info("MainWindow", "Icon loaded successfully");
                    _originalTrayIcon = icon;
                }
                catch (Exception ex)
                {
                    Logger.Error("MainWindow", "Failed to load icon", ex);
                }
            }
            else
            {
                Logger.Warning("MainWindow", "Icon file not found in any expected location");
            }

            // Ensure visibility is set
            _trayIcon.Visibility = Visibility.Visible;
            NotificationService.Instance.SetTrayIcon(_trayIcon);

            // Set tooltip with actual version
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            _trayIcon.ToolTipText = $"Redball v{version?.Major}.{version?.Minor}.{version?.Build}";
            Logger.Info("MainWindow", $"Tray tooltip set to: {_trayIcon.ToolTipText}");
        }
        else
        {
            Logger.Error("MainWindow", "TrayIcon not found in XAML!");
        }

        Logger.Info("MainWindow", "Tray icon setup complete");

        // Subscribe to state changes for icon updates
        KeepAwakeService.Instance.ActiveStateChanged -= OnKeepAwakeStateChanged;
        KeepAwakeService.Instance.ActiveStateChanged += OnKeepAwakeStateChanged;
    }

    private void OnKeepAwakeStateChanged(object? sender, bool isActive)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateTrayIconState(isActive);
            UpdateTrayIconCountdown();
            
            // Show sleek HUD feedback for hotkey/UI changes
            var status = isActive ? "ACTIVATED" : "PAUSED";
            var icon = isActive ? "🚀" : "⏸️";
            HUDWindow.ShowStatus("Keep-Awake", status, icon);
        });
    }

    private void UpdateTrayIconState(bool isActive)
    {
        if (_trayIcon == null || _originalTrayIcon == null) return;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = $"v{version?.Major}.{version?.Minor}.{version?.Build}";

        if (isActive)
        {
            // Calculate pulse alpha based on frame (breathe effect)
            int intensity = (int)(180 + 75 * Math.Sin(Math.PI * _animationFrame / (AnimationMaxFrames / 2.0)));
            
            var until = KeepAwakeService.Instance.Until;
            
            // Check higher priority states
            if (GamingModeService.Instance.IsGaming)
            {
                _trayIcon.ToolTipText = $"Redball {versionStr} — Gaming Mode Active (Optimized)";
                SetGeneratedTrayIcon(GenerateColorizedIcon(_originalTrayIcon, System.Drawing.Color.MediumPurple, intensity)); // Purple ball
            }
            else if (MeetingDetectionService.Instance.IsMeetingActive)
            {
                _trayIcon.ToolTipText = $"Redball {versionStr} — Meeting Mode Active";
                SetGeneratedTrayIcon(GenerateColorizedIcon(_originalTrayIcon, System.Drawing.Color.DodgerBlue, intensity)); // Blue ball
            }
            else if (BatteryMonitorService.Instance.IsOnBattery && BatteryMonitorService.Instance.BatteryPercent < 25)
            {
                _trayIcon.ToolTipText = $"Redball {versionStr} — Active (Battery Low: {BatteryMonitorService.Instance.BatteryPercent}%)";
                SetGeneratedTrayIcon(GenerateColorizedIcon(_originalTrayIcon, System.Drawing.Color.Red, intensity)); // Red ball pulse
            }
            else if (until.HasValue)
            {
                var minsLeft = Math.Max(0, (int)(until.Value - DateTime.Now).TotalMinutes);
                _trayIcon.ToolTipText = $"Redball {versionStr} — Timed Session ({minsLeft} min left)";
                var text = minsLeft > 99 ? "99+" : minsLeft.ToString();
                SetGeneratedTrayIcon(GenerateCountdownIcon(_originalTrayIcon, text, System.Drawing.Color.FromArgb(intensity, 255, 140, 0))); // Orange countdown
            }
            else
            {
                _trayIcon.ToolTipText = $"Redball {versionStr} — Active";
                SetGeneratedTrayIcon(GenerateColorizedIcon(_originalTrayIcon, System.Drawing.Color.LimeGreen, intensity)); // Green ball
            }
        }
        else
        {
            _trayIcon.ToolTipText = $"Redball {versionStr} — Paused";
            SetGeneratedTrayIcon(GenerateColorizedIcon(_originalTrayIcon, System.Drawing.Color.Gray, 120)); // Dimmed Gray
        }
    }

    private void SetGeneratedTrayIcon(Icon icon)
    {
        if (ReferenceEquals(icon, _originalTrayIcon))
        {
            RestoreOriginalTrayIcon();
            return;
        }

        if (_generatedTrayIcon != null)
        {
            _generatedTrayIcon.Dispose();
        }

        _generatedTrayIcon = icon;

        if (_trayIcon != null)
        {
            _trayIcon.Icon = icon;
        }
    }

    private void RestoreOriginalTrayIcon()
    {
        if (_generatedTrayIcon != null && !ReferenceEquals(_generatedTrayIcon, _originalTrayIcon))
        {
            _generatedTrayIcon.Dispose();
        }

        _generatedTrayIcon = null;

        if (_trayIcon != null && _originalTrayIcon != null)
        {
            _trayIcon.Icon = _originalTrayIcon;
        }
    }

    private void DisposeTrayIcons()
    {
        if (_generatedTrayIcon != null && !ReferenceEquals(_generatedTrayIcon, _originalTrayIcon))
        {
            _generatedTrayIcon.Dispose();
        }

        _generatedTrayIcon = null;

        if (_originalTrayIcon != null)
        {
            _originalTrayIcon.Dispose();
            _originalTrayIcon = null;
        }
    }

    private static Icon GenerateColorizedIcon(Icon baseIcon, System.Drawing.Color targetColor, int alpha)
    {
        try
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            
            // ColorMatrix to tint the icon
            float r = targetColor.R / 255f;
            float gVal = targetColor.G / 255f;
            float b = targetColor.B / 255f;
            float a = alpha / 255f;

            var cm = new System.Drawing.Imaging.ColorMatrix(new float[][]
            {
                new float[] {r, 0, 0, 0, 0},
                new float[] {0, gVal, 0, 0, 0},
                new float[] {0, 0, b, 0, 0},
                new float[] {0, 0, 0, a, 0},
                new float[] {0, 0, 0, 0, 1}
            });

            using var ia = new System.Drawing.Imaging.ImageAttributes();
            ia.SetColorMatrix(cm);

            using var baseBmp = baseIcon.ToBitmap();
            g.DrawImage(baseBmp, new System.Drawing.Rectangle(0, 0, 16, 16), 0, 0, baseBmp.Width, baseBmp.Height, System.Drawing.GraphicsUnit.Pixel, ia);

            var handle = bmp.GetHicon();
            using var tempIcon = System.Drawing.Icon.FromHandle(handle);
            var clonedIcon = (Icon)tempIcon.Clone();
            DestroyIcon(handle);
            return clonedIcon;
        }
        catch (Exception ex)
        {
            Logger.Debug("MainWindow", $"Failed to colorize icon: {ex.Message}");
            return baseIcon;
        }
    }

    private static Icon GenerateStateIcon(Icon baseIcon, System.Drawing.Color dotColor)
    {
        try
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var baseBmp = baseIcon.ToBitmap();
            g.DrawImage(baseBmp, 0, 0, 16, 16);

            // Draw a small status dot in the bottom-right corner
            using var brush = new SolidBrush(dotColor);
            g.FillEllipse(brush, 10, 10, 6, 6);
            // White outline for visibility
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1f);
            g.DrawEllipse(pen, 10, 10, 6, 6);

            var handle = bmp.GetHicon();
            using var tempIcon = System.Drawing.Icon.FromHandle(handle);
            var clonedIcon = (Icon)tempIcon.Clone();
            DestroyIcon(handle);
            return clonedIcon;
        }
        catch (Exception ex)
        {
            Logger.Debug("MainWindow", $"Failed to generate state icon: {ex.Message}");
            return baseIcon;
        }
    }

    private void UpdateTrayIconCountdown()
    {
        UpdateTrayIconState(KeepAwakeService.Instance.IsActive);
    }

    private static Icon GenerateCountdownIcon(Icon baseIcon, string text, System.Drawing.Color bgColor)
    {
        try
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Draw original icon slightly dimmed
            using var baseBmp = baseIcon.ToBitmap();
            g.DrawImage(baseBmp, 0, 0, 16, 16);

            // Draw semi-transparent background for text using state color
            using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(bgColor.A, bgColor.R, bgColor.G, bgColor.B));
            g.FillRectangle(bgBrush, 0, 7, 16, 9);

            // Draw countdown text
            using var font = new Font("Segoe UI", 6f, System.Drawing.FontStyle.Bold);
            using var textBrush = new SolidBrush(System.Drawing.Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, new RectangleF(0, 7, 16, 9), sf);

            var handle = bmp.GetHicon();
            using var tempIcon = System.Drawing.Icon.FromHandle(handle);
            var clonedIcon = (Icon)tempIcon.Clone();
            DestroyIcon(handle);
            return clonedIcon;
        }
        catch (Exception ex)
        {
            Logger.Debug("MainWindow", $"Failed to generate countdown icon: {ex.Message}");
            return baseIcon;
        }
    }
}
