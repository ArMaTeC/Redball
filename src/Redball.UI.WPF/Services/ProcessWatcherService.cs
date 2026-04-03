using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Watches for a specific process and auto-activates/deactivates keep-awake based on its lifecycle.
/// </summary>
public class ProcessWatcherService
{
    private static readonly Lazy<ProcessWatcherService> _instance = new(() => new ProcessWatcherService());
    public static ProcessWatcherService Instance => _instance.Value;

    private readonly DispatcherTimer _pollTimer;
    private string _targetProcessName = "";
    private bool _wasRunning;
    private bool _enabled;

    public event EventHandler<bool>? ProcessStateChanged;

    public bool IsEnabled => _enabled;
    public string TargetProcessName => _targetProcessName;
    public bool IsTargetRunning { get; private set; }

    private ProcessWatcherService()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += PollTimer_Tick;
        Logger.Verbose("ProcessWatcherService", "Instance created");
    }

    public void Start(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;

        _targetProcessName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
        _enabled = true;
        _wasRunning = false;
        _pollTimer.Start();
        Logger.Info("ProcessWatcherService", $"Watching for process: {_targetProcessName}");

        // Immediate check
        PollTimer_Tick(null, EventArgs.Empty);
    }

    public void Stop()
    {
        _enabled = false;
        _pollTimer.Stop();
        _targetProcessName = "";
        IsTargetRunning = false;
        Logger.Info("ProcessWatcherService", "Process watching stopped");
    }

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_enabled || string.IsNullOrEmpty(_targetProcessName)) return;

        try
        {
            var running = Process.GetProcessesByName(_targetProcessName).Length > 0;
            IsTargetRunning = running;

            if (running && !_wasRunning)
            {
                // Process started
                Logger.Info("ProcessWatcherService", $"Process '{_targetProcessName}' detected — activating keep-awake");
                KeepAwakeService.Instance.SetActive(true);
                NotificationService.Instance.ShowInfo("Process Watcher", $"{_targetProcessName} detected — keeping awake.");
                ProcessStateChanged?.Invoke(this, true);
            }
            else if (!running && _wasRunning)
            {
                // Process exited
                Logger.Info("ProcessWatcherService", $"Process '{_targetProcessName}' exited — deactivating keep-awake");
                KeepAwakeService.Instance.SetActive(false);
                NotificationService.Instance.ShowInfo("Process Watcher", $"{_targetProcessName} exited — sleep allowed.");
                ProcessStateChanged?.Invoke(this, false);
            }

            _wasRunning = running;
        }
        catch (Exception ex)
        {
            Logger.Error("ProcessWatcherService", "Error polling processes", ex);
        }
    }

    public string[] GetRunningProcessNames()
    {
        try
        {
            return Process.GetProcesses()
                .Select(p => p.ProcessName)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();
        }
        catch (Exception ex)
        {
            Logger.Debug("ProcessWatcherService", $"Failed to get running processes: {ex.Message}");
            return Array.Empty<string>();
        }
    }
}
