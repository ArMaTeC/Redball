using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Redball.UI.Services;

/// <summary>
/// Manages schedule-based activation/deactivation of keep-awake.
/// Port of Test-ScheduleActive, Update-ScheduleState.
/// </summary>
public class ScheduleService
{
    public bool IsEnabled { get; set; }
    public string StartTime { get; set; } = "09:00";
    public string StopTime { get; set; } = "18:00";
    public List<string> Days { get; set; } = new() { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };

    /// <summary>
    /// Returns true if the current time falls within the scheduled window.
    /// </summary>
    public bool IsInSchedule()
    {
        if (!IsEnabled) return false;

        try
        {
            var now = DateTime.Now;
            var currentDay = now.DayOfWeek.ToString();

            if (!Days.Contains(currentDay, StringComparer.OrdinalIgnoreCase))
                return false;

            var start = DateTime.ParseExact(StartTime, "HH:mm", CultureInfo.InvariantCulture);
            var stop = DateTime.ParseExact(StopTime, "HH:mm", CultureInfo.InvariantCulture);

            var todayStart = now.Date.Add(start.TimeOfDay);
            var todayStop = now.Date.Add(stop.TimeOfDay);

            return now >= todayStart && now <= todayStop;
        }
        catch (Exception ex)
        {
            Logger.Debug("ScheduleService", $"Schedule check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks schedule and auto-activates/pauses keep-awake as needed.
    /// </summary>
    public void CheckAndUpdate(KeepAwakeService keepAwake)
    {
        if (!IsEnabled) return;

        var shouldBeActive = IsInSchedule();

        if (shouldBeActive && !keepAwake.IsActive && !keepAwake.AutoPausedSchedule)
        {
            Logger.Info("ScheduleService", $"Auto-starting per schedule ({StartTime}-{StopTime})");
            keepAwake.AutoResume("Schedule");
            if (!keepAwake.IsActive)
            {
                // If AutoResume didn't activate (wasn't auto-paused), activate directly
                keepAwake.SetActive(true);
            }
        }
        else if (!shouldBeActive && keepAwake.IsActive && !keepAwake.AutoPausedSchedule)
        {
            Logger.Info("ScheduleService", "Auto-stopping per schedule end");
            keepAwake.AutoPause("Schedule");
        }
        else if (shouldBeActive && keepAwake.AutoPausedSchedule)
        {
            // Back in schedule window, clear schedule pause flag
            keepAwake.AutoResume("Schedule");
        }
    }
}
