using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Monitors uptime and optionally reminds the user to restart Redball or reboot the PC
/// after a configurable number of days.
/// </summary>
public class ScheduledRestartService
{
    private static readonly Lazy<ScheduledRestartService> _instance = new(() => new ScheduledRestartService());
    public static ScheduledRestartService Instance => _instance.Value;

    private readonly DispatcherTimer _checkTimer;
    private readonly DateTime _appStartTime;
    private bool _reminderSent;

    public DateTime AppStartTime => _appStartTime;
    public TimeSpan Uptime => DateTime.Now - _appStartTime;

    public event EventHandler? RestartRequested;

    private ScheduledRestartService()
    {
        _appStartTime = DateTime.Now;
        _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _checkTimer.Tick += CheckTimer_Tick;
        _checkTimer.Start();
        Logger.Verbose("ScheduledRestartService", "Instance created");
    }

    private void CheckTimer_Tick(object? sender, EventArgs e)
    {
        var config = ConfigService.Instance.Config;
        if (!config.RestartReminderEnabled || config.RestartReminderDays <= 0) return;

        var uptimeDays = Uptime.TotalDays;
        if (uptimeDays >= config.RestartReminderDays && !_reminderSent)
        {
            _reminderSent = true;
            Logger.Info("ScheduledRestartService", $"Uptime exceeded {config.RestartReminderDays} days — sending reminder");
            NotificationService.Instance.ShowWarning("Restart Reminder",
                $"Redball has been running for {(int)uptimeDays} days. Consider restarting for optimal performance.");

            if (config.AutoRestartEnabled)
            {
                Logger.Info("ScheduledRestartService", "Auto-restart triggered");
                RestartRequested?.Invoke(this, EventArgs.Empty);
                RestartApplication();
            }
        }
    }

    public void RestartApplication()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                Logger.Info("ScheduledRestartService", $"Restarting: {exePath}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
                if (System.Windows.Application.Current?.MainWindow is Views.MainWindow mainWindow)
                {
                    mainWindow.ExitApplication();
                    return;
                }

                System.Windows.Application.Current?.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("ScheduledRestartService", "Failed to restart application", ex);
        }
    }

    public string GetStatusText()
    {
        var config = ConfigService.Instance.Config;
        var uptimeDays = Uptime.TotalDays;
        if (!config.RestartReminderEnabled)
            return $"Uptime: {FormatUptime(Uptime)} (reminder disabled)";

        var remaining = config.RestartReminderDays - uptimeDays;
        return remaining > 0
            ? $"Uptime: {FormatUptime(Uptime)} (restart reminder in {remaining:F1} days)"
            : $"Uptime: {FormatUptime(Uptime)} (restart recommended)";
    }

    private static string FormatUptime(TimeSpan ts)
    {
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{(int)ts.TotalMinutes}m";
    }
}
