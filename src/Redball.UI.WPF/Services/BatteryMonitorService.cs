using System;
using System.Management;

namespace Redball.UI.Services;

/// <summary>
/// Monitors battery status and auto-pauses keep-awake when battery is low.
/// Port of Get-BatteryStatus, Test-BatteryThreshold, Update-BatteryAwareState.
/// </summary>
public class BatteryMonitorService
{
    private DateTime? _lastCheck;
    private BatteryStatus? _cachedStatus;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromSeconds(60);

    public bool IsEnabled { get; set; }
    public int Threshold { get; set; } = 20;

    /// <summary>
    /// Gets the current battery status, cached for 60 seconds.
    /// </summary>
    public BatteryStatus GetStatus()
    {
        if (_lastCheck.HasValue && _cachedStatus != null &&
            (DateTime.Now - _lastCheck.Value) < CacheExpiry)
        {
            return _cachedStatus;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_Battery");
            using var results = searcher.Get();

            foreach (ManagementObject battery in results)
            {
                var chargePercent = Convert.ToInt32(battery["EstimatedChargeRemaining"] ?? 0);
                var batteryStatus = Convert.ToInt32(battery["BatteryStatus"] ?? 0);
                // BatteryStatus: 1=Discharging, 2=AC, 3-5=various charging states
                var isOnBattery = batteryStatus == 1;

                _cachedStatus = new BatteryStatus
                {
                    HasBattery = true,
                    IsOnBattery = isOnBattery,
                    ChargePercent = chargePercent
                };
                _lastCheck = DateTime.Now;
                return _cachedStatus;
            }

            _cachedStatus = new BatteryStatus { HasBattery = false };
            _lastCheck = DateTime.Now;
            return _cachedStatus;
        }
        catch (Exception ex)
        {
            Logger.Debug("BatteryMonitor", $"Battery query failed: {ex.Message}");
            return new BatteryStatus { HasBattery = false };
        }
    }

    /// <summary>
    /// Checks battery and auto-pauses/resumes keep-awake as needed.
    /// </summary>
    public void CheckAndUpdate(KeepAwakeService keepAwake)
    {
        if (!IsEnabled) return;

        var status = GetStatus();
        if (!status.HasBattery) return;

        var belowThreshold = status.IsOnBattery && status.ChargePercent <= Threshold;

        if (belowThreshold && keepAwake.IsActive && !keepAwake.AutoPausedBattery)
        {
            Logger.Info("BatteryMonitor", $"Auto-pausing: battery at {status.ChargePercent}% (threshold: {Threshold}%)");
            keepAwake.AutoPause("Battery");
        }
        else if (!belowThreshold && keepAwake.AutoPausedBattery)
        {
            Logger.Info("BatteryMonitor", "Auto-resuming: power restored or battery charged");
            keepAwake.AutoResume("Battery");
        }
    }
}

public class BatteryStatus
{
    public bool HasBattery { get; set; }
    public bool IsOnBattery { get; set; }
    public int ChargePercent { get; set; }
}
