using System;
using System.Runtime.InteropServices;
using Redball.UI.Interop;

namespace Redball.UI.Services;

/// <summary>
/// Detects user idle time and auto-pauses keep-awake when idle too long.
/// Port of Get-IdleTimeMinute, Update-IdleAwareState.
/// </summary>
public class IdleDetectionService
{
    public bool IsEnabled { get; set; }
    public int ThresholdMinutes { get; set; } = 30;

    /// <summary>
    /// Gets the current user idle time in minutes.
    /// </summary>
    public double GetIdleMinutes()
    {
        try
        {
            var lii = new NativeMethods.LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
            };

            if (NativeMethods.GetLastInputInfo(ref lii))
            {
                var idleMs = (uint)Environment.TickCount - lii.dwTime;
                return idleMs / 60000.0;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Logger.Debug("IdleDetection", $"Idle query failed: {ex.Message}");
            return 0;
        }
    }

    public event EventHandler? OnUserIdleDetected;
    public event EventHandler? OnUserActivityDetected;

    private bool _wasIdle;

    /// <summary>
    /// Checks idle time and auto-pauses/resumes keep-awake as needed.
    /// </summary>
    public void CheckAndUpdate(KeepAwakeService keepAwake)
    {
        if (!IsEnabled) return;

        var idleMinutes = GetIdleMinutes();
        bool isIdle = idleMinutes > ThresholdMinutes;

        if (isIdle && !_wasIdle)
        {
            _wasIdle = true;
            OnUserIdleDetected?.Invoke(this, EventArgs.Empty);
            Logger.Info("IdleDetection", $"User declared idle after {idleMinutes:F1} minutes.");
        }
        else if (!isIdle && _wasIdle)
        {
            _wasIdle = false;
            OnUserActivityDetected?.Invoke(this, EventArgs.Empty);
            Logger.Info("IdleDetection", "User activity detected. System is no longer idle.");
        }

        if (idleMinutes > ThresholdMinutes && keepAwake.IsActive && !keepAwake.AutoPausedIdle)
        {
            Logger.Info("IdleDetection", $"Auto-pausing: user idle for {idleMinutes:F0} minutes (threshold: {ThresholdMinutes})");
            keepAwake.AutoPause("Idle");
        }
        else if (idleMinutes < 1 && keepAwake.AutoPausedIdle)
        {
            Logger.Info("IdleDetection", "Auto-resuming: user activity detected");
            keepAwake.AutoResume("Idle");
        }
    }
}
