using System;
using System.Management;
using System.Windows.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Monitors CPU temperature via WMI and optionally auto-pauses keep-awake
/// if temperature exceeds a configured threshold (thermal protection).
/// </summary>
public class TemperatureMonitorService
{
    private static readonly Lazy<TemperatureMonitorService> _instance = new(() => new TemperatureMonitorService());
    public static TemperatureMonitorService Instance => _instance.Value;

    private readonly DispatcherTimer _pollTimer;
    private bool _thermalPaused;

    public double? CurrentCpuTemp { get; private set; }
    public bool IsOverThreshold { get; private set; }

    public event EventHandler<double>? TemperatureUpdated;

    private TemperatureMonitorService()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _pollTimer.Tick += PollTimer_Tick;
        _pollTimer.Start();
        PollTemperature();
        Logger.Verbose("TemperatureMonitorService", "Instance created");
    }

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        PollTemperature();
        CheckThreshold();
    }

    private void PollTemperature()
    {
        try
        {
            // Try MSAcpi_ThermalZoneTemperature (requires admin on some systems)
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            foreach (ManagementObject obj in searcher.Get())
            {
                var tempKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                // WMI returns temperature in tenths of Kelvin
                var tempCelsius = (tempKelvin / 10.0) - 273.15;
                CurrentCpuTemp = tempCelsius;
                TemperatureUpdated?.Invoke(this, tempCelsius);
                return;
            }
        }
        catch
        {
            // MSAcpi not available — try Win32_TemperatureProbe as fallback
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_TemperatureProbe");

            foreach (ManagementObject obj in searcher.Get())
            {
                var temp = obj["CurrentReading"];
                if (temp != null)
                {
                    CurrentCpuTemp = Convert.ToDouble(temp);
                    TemperatureUpdated?.Invoke(this, CurrentCpuTemp.Value);
                    return;
                }
            }
        }
        catch
        {
            // Temperature reading not available on this system
        }

        CurrentCpuTemp = null;
    }

    private void CheckThreshold()
    {
        var config = ConfigService.Instance.Config;
        if (!config.ThermalProtectionEnabled || !CurrentCpuTemp.HasValue) return;

        IsOverThreshold = CurrentCpuTemp.Value >= config.ThermalThreshold;

        if (IsOverThreshold && !_thermalPaused && KeepAwakeService.Instance.IsActive)
        {
            Logger.Warning("TemperatureMonitor", $"CPU temp {CurrentCpuTemp:F1}°C exceeds threshold {config.ThermalThreshold}°C — pausing");
            KeepAwakeService.Instance.SetActive(false);
            _thermalPaused = true;
            NotificationService.Instance.ShowWarning("Thermal Protection",
                $"CPU temperature ({CurrentCpuTemp:F1}°C) exceeded {config.ThermalThreshold}°C. Keep-awake paused.");
        }
        else if (!IsOverThreshold && _thermalPaused)
        {
            Logger.Info("TemperatureMonitor", $"CPU temp {CurrentCpuTemp:F1}°C below threshold — resuming");
            KeepAwakeService.Instance.SetActive(true);
            _thermalPaused = false;
            NotificationService.Instance.ShowInfo("Thermal Protection",
                $"CPU temperature ({CurrentCpuTemp:F1}°C) back to normal. Keep-awake resumed.");
        }
    }

    public string GetStatusText()
    {
        if (!CurrentCpuTemp.HasValue)
            return "CPU Temperature: Not available";

        var config = ConfigService.Instance.Config;
        var status = config.ThermalProtectionEnabled
            ? (IsOverThreshold ? " (OVER THRESHOLD)" : $" (threshold: {config.ThermalThreshold}°C)")
            : "";
        return $"CPU Temperature: {CurrentCpuTemp:F1}°C{status}";
    }
}
