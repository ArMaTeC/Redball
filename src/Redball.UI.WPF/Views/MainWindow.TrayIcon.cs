using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
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
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Check for TaskbarCreated message (Explorer restart)
        if (msg == _taskbarCreatedMsg)
        {
            Logger.Info("MainWindow", "TaskbarCreated message received - Explorer likely restarted, recreating tray icon");
            handled = true;
            // Recreate tray icon with delay to ensure Explorer is ready
            var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            delayTimer.Tick += (_, _) =>
            {
                delayTimer.Stop();
                try
                {
                    RecreateTrayIcon();
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

    private void RecreateTrayIcon()
    {
        Logger.Info("MainWindow", "Recreating tray icon...");
        try
        {
            // Dispose existing tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visibility = Visibility.Collapsed;
                _trayIcon = null;
                Logger.Debug("MainWindow", "Existing tray icon hidden");
            }

            _isTrayIconInitialized = false;

            // Small delay to ensure cleanup
            System.Threading.Thread.Sleep(100);

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

            _trayIconRefreshTimer.Tick += (s, e) =>
            {
                try
                {
                    UpdateTrayIconCountdown();

                    // Check if tray icon needs refreshing
                    if (_trayIcon == null || !_isTrayIconInitialized)
                    {
                        retryCount++;
                        if (retryCount <= maxRetries)
                        {
                            Logger.Warning("MainWindow", $"Tray icon not initialized, attempt {retryCount}/{maxRetries} to recreate...");
                            RecreateTrayIcon();
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
        }
        catch (Exception ex)
        {
            Logger.Error("MainWindow", "Failed to setup tray icon refresh timer", ex);
        }
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
        KeepAwakeService.Instance.ActiveStateChanged += OnKeepAwakeStateChanged;
    }

    private void OnKeepAwakeStateChanged(object? sender, bool isActive)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateTrayIconState(isActive);
            UpdateTrayIconCountdown();
        });
    }

    private void UpdateTrayIconState(bool isActive)
    {
        if (_trayIcon == null || _originalTrayIcon == null) return;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = $"v{version?.Major}.{version?.Minor}.{version?.Build}";

        if (isActive)
        {
            var until = KeepAwakeService.Instance.Until;
            if (until.HasValue)
            {
                var minsLeft = Math.Max(0, (int)(until.Value - DateTime.Now).TotalMinutes);
                _trayIcon.ToolTipText = $"Redball {versionStr} — Timed: {minsLeft} min left";
                _trayIcon.Icon = GenerateStateIcon(_originalTrayIcon, System.Drawing.Color.FromArgb(253, 126, 20)); // Orange for timed
            }
            else
            {
                _trayIcon.ToolTipText = $"Redball {versionStr} — Active";
                _trayIcon.Icon = GenerateStateIcon(_originalTrayIcon, System.Drawing.Color.FromArgb(76, 175, 80)); // Green for active
            }
        }
        else
        {
            _trayIcon.ToolTipText = $"Redball {versionStr} — Paused";
            _trayIcon.Icon = GenerateStateIcon(_originalTrayIcon, System.Drawing.Color.FromArgb(108, 117, 125)); // Gray for paused
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

            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return baseIcon;
        }
    }

    private void UpdateTrayIconCountdown()
    {
        if (_trayIcon == null || _originalTrayIcon == null) return;

        var until = KeepAwakeService.Instance.Until;
        if (until.HasValue && KeepAwakeService.Instance.IsActive)
        {
            var minsLeft = Math.Max(0, (int)(until.Value - DateTime.Now).TotalMinutes);
            var text = minsLeft > 99 ? "99+" : minsLeft.ToString();
            _trayIcon.Icon = GenerateCountdownIcon(_originalTrayIcon, text);
        }
        else
        {
            // Restore original icon when no timed session
            if (_trayIcon.Icon != _originalTrayIcon)
                _trayIcon.Icon = _originalTrayIcon;
        }
    }

    private static Icon GenerateCountdownIcon(Icon baseIcon, string text)
    {
        try
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Draw original icon as background
            using var baseBmp = baseIcon.ToBitmap();
            g.DrawImage(baseBmp, 0, 0, 16, 16);

            // Draw semi-transparent background for text
            using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(200, 0, 0, 0));
            g.FillRectangle(bgBrush, 0, 7, 16, 9);

            // Draw countdown text
            using var font = new Font("Segoe UI", 6f, System.Drawing.FontStyle.Bold);
            using var textBrush = new SolidBrush(System.Drawing.Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, new RectangleF(0, 7, 16, 9), sf);

            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }
        catch (Exception ex)
        {
            Logger.Debug("MainWindow", $"Failed to generate countdown icon: {ex.Message}");
            return baseIcon;
        }
    }
}
