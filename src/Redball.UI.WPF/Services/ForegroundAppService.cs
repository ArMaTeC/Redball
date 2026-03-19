using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Monitors the foreground window and applies per-app keep-awake rules.
/// Rules can specify "always keep awake when X is foreground" or "auto-pause when Y is foreground".
/// </summary>
public class ForegroundAppService
{
    private static readonly Lazy<ForegroundAppService> _instance = new(() => new ForegroundAppService());
    public static ForegroundAppService Instance => _instance.Value;

    private readonly DispatcherTimer _pollTimer;
    private bool _enabled;
    private string _lastForegroundApp = "";
    private bool _ruleActivatedKeepAwake;

    public bool IsEnabled => _enabled;
    public string CurrentForegroundApp => _lastForegroundApp;

    public event EventHandler<string>? ForegroundAppChanged;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private ForegroundAppService()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += PollTimer_Tick;
        Logger.Verbose("ForegroundAppService", "Instance created");
    }

    public void Start()
    {
        if (_enabled) return;
        _enabled = true;
        _pollTimer.Start();
        Logger.Info("ForegroundAppService", "Foreground app monitoring started");
    }

    public void Stop()
    {
        _enabled = false;
        _pollTimer.Stop();
        _lastForegroundApp = "";
        Logger.Info("ForegroundAppService", "Foreground app monitoring stopped");
    }

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_enabled) return;

        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return;

            var proc = Process.GetProcessById((int)pid);
            var appName = proc.ProcessName;

            if (appName == _lastForegroundApp) return;

            _lastForegroundApp = appName;
            ForegroundAppChanged?.Invoke(this, appName);

            // Apply rules
            var config = ConfigService.Instance.Config;
            var keepAwakeApps = ParseAppList(config.KeepAwakeApps);
            var pauseApps = ParseAppList(config.PauseApps);

            if (keepAwakeApps.Any(a => appName.Contains(a, StringComparison.OrdinalIgnoreCase)))
            {
                if (!KeepAwakeService.Instance.IsActive)
                {
                    KeepAwakeService.Instance.SetActive(true);
                    _ruleActivatedKeepAwake = true;
                    Logger.Info("ForegroundAppService", $"Rule match: keeping awake for {appName}");
                }
            }
            else if (pauseApps.Any(a => appName.Contains(a, StringComparison.OrdinalIgnoreCase)))
            {
                if (KeepAwakeService.Instance.IsActive)
                {
                    KeepAwakeService.Instance.SetActive(false);
                    _ruleActivatedKeepAwake = false;
                    Logger.Info("ForegroundAppService", $"Rule match: pausing for {appName}");
                }
            }
            else if (_ruleActivatedKeepAwake)
            {
                // No matching rule and we previously activated — deactivate
                KeepAwakeService.Instance.SetActive(false);
                _ruleActivatedKeepAwake = false;
                Logger.Info("ForegroundAppService", $"No rule match for {appName}, reverting keep-awake");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("ForegroundAppService", $"Poll error: {ex.Message}");
        }
    }

    private static List<string> ParseAppList(string? appList)
    {
        if (string.IsNullOrWhiteSpace(appList)) return new List<string>();
        return appList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }
}
