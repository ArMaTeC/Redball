using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Windows.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Monitors CPU temperature via multiple methods (WMI, Performance Counters) 
/// and optionally auto-pauses keep-awake if temperature exceeds a configured threshold (thermal protection).
/// </summary>
public class TemperatureMonitorService
{
    private static readonly Lazy<TemperatureMonitorService> _instance = new(() => new TemperatureMonitorService());
    public static TemperatureMonitorService Instance => _instance.Value;

    private readonly DispatcherTimer _pollTimer;
    private bool _thermalPaused;
    private string _lastError = "";
    private int _consecutiveFailures;
    private readonly string[] _wmiClassesToTry = 
    {
        "MSAcpi_ThermalZoneTemperature",
        "Win32_PerfFormattedData_Counters_ThermalZoneInformation",
        "CIM_Sensor"
    };

    public double? CurrentCpuTemp { get; private set; }
    public bool IsOverThreshold { get; private set; }
    public string LastError => _lastError;

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
        _lastError = "";
        double? temp = null;

        // Try multiple methods in order of preference
        temp ??= TryPerformanceCounters();
        temp ??= TryWmi_MSAcpiThermalZone();
        temp ??= TryWmi_ThermalZoneInfo();
        temp ??= TryWmi_CimSensor();
        temp ??= TryWmi_TemperatureProbe();
        temp ??= TryWmi_AmdProcessor();
        temp ??= TryWmi_IntelProcessor();

        if (temp.HasValue)
        {
            CurrentCpuTemp = temp.Value;
            _consecutiveFailures = 0;
            TemperatureUpdated?.Invoke(this, temp.Value);
        }
        else
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= 3)
            {
                CurrentCpuTemp = null;
            }
        }
    }

    private double? TryPerformanceCounters()
    {
        try
        {
            // Try to read from performance counters (more reliable on modern Windows)
            using var pc = new PerformanceCounter("Thermal Zone Information", "Temperature", "\\_TZ.THRM", true);
            var value = pc.NextValue();
            if (value > 0)
            {
                // Performance counter returns temperature in Kelvin * 10 (tenths of Kelvin)
                var celsius = (value / 10.0) - 273.15;
                if (celsius > 0 && celsius < 150) // Sanity check
                {
                    Logger.Verbose("TemperatureMonitor", $"Got temp from performance counter: {celsius:F1}C");
                    return celsius;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Verbose("TemperatureMonitor", $"Performance counter failed: {ex.Message}");
        }
        return null;
    }

    private double? TryWmi_MSAcpiThermalZone()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature WHERE Active = True");

            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                var tempKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                // WMI returns temperature in tenths of Kelvin
                var tempCelsius = (tempKelvin / 10.0) - 273.15;
                if (tempCelsius > 0 && tempCelsius < 150)
                {
                    Logger.Verbose("TemperatureMonitor", $"Got temp from MSAcpi_ThermalZoneTemperature: {tempCelsius:F1}C");
                    return tempCelsius;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Verbose("TemperatureMonitor", $"MSAcpi_ThermalZoneTemperature failed: {ex.Message}");
        }
        return null;
    }

    private double? TryWmi_ThermalZoneInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT * FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");

            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                var temp = obj["HighPrecisionTemperature"];
                if (temp != null)
                {
                    var tempValue = Convert.ToDouble(temp);
                    // This is already in tenths of Kelvin
                    var celsius = (tempValue / 10.0) - 273.15;
                    if (celsius > 0 && celsius < 150)
                    {
                        Logger.Verbose("TemperatureMonitor", $"Got temp from ThermalZoneInformation: {celsius:F1}C");
                        return celsius;
                    }
                }
                
                temp = obj["Temperature"];
                if (temp != null)
                {
                    var tempValue = Convert.ToDouble(temp);
                    var celsius = (tempValue / 10.0) - 273.15;
                    if (celsius > 0 && celsius < 150)
                    {
                        Logger.Verbose("TemperatureMonitor", $"Got temp from ThermalZoneInformation (Temperature): {celsius:F1}C");
                        return celsius;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Verbose("TemperatureMonitor", $"ThermalZoneInformation failed: {ex.Message}");
        }
        return null;
    }

    private double? TryWmi_CimSensor()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM CIM_Sensor WHERE SensorType = 'Temperature'");

            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                var currentReading = obj["CurrentReading"];
                if (currentReading != null)
                {
                    var celsius = Convert.ToDouble(currentReading);
                    if (celsius > 0 && celsius < 150)
                    {
                        var name = obj["Name"]?.ToString() ?? "";
                        if (name.Contains("CPU", StringComparison.OrdinalIgnoreCase) || 
                            name.Contains("Processor", StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Verbose("TemperatureMonitor", $"Got temp from CIM_Sensor ({name}): {celsius:F1}C");
                            return celsius;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Verbose("TemperatureMonitor", $"CIM_Sensor failed: {ex.Message}");
        }
        return null;
    }

    private double? TryWmi_TemperatureProbe()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_TemperatureProbe");

            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                var temp = obj["CurrentReading"];
                if (temp != null)
                {
                    var celsius = Convert.ToDouble(temp);
                    if (celsius > 0 && celsius < 150)
                    {
                        Logger.Verbose("TemperatureMonitor", $"Got temp from Win32_TemperatureProbe: {celsius:F1}C");
                        return celsius;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Verbose("TemperatureMonitor", $"Win32_TemperatureProbe failed: {ex.Message}");
        }
        return null;
    }

    private double? TryWmi_AmdProcessor()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT * FROM AMDK7Temp WHERE InstanceName LIKE '%CPU%'");

            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                var temp = obj["CurrentTemperature"];
                if (temp != null)
                {
                    var celsius = Convert.ToDouble(temp);
                    if (celsius > 0 && celsius < 150)
                    {
                        Logger.Verbose("TemperatureMonitor", $"Got temp from AMDK7Temp: {celsius:F1}C");
                        return celsius;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Verbose("TemperatureMonitor", $"AMDK7Temp failed: {ex.Message}");
        }
        return null;
    }

    private double? TryWmi_IntelProcessor()
    {
        try
        {
            // Try Intel processor temperature via Intel WMI provider
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT * FROM Win32_TemperatureProbe WHERE Name LIKE '%Intel%' OR Name LIKE '%CPU%'");

            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                var temp = obj["CurrentReading"];
                if (temp != null)
                {
                    var celsius = Convert.ToDouble(temp);
                    if (celsius > 0 && celsius < 150)
                    {
                        Logger.Verbose("TemperatureMonitor", $"Got temp from Intel WMI: {celsius:F1}C");
                        return celsius;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Verbose("TemperatureMonitor", $"Intel WMI failed: {ex.Message}");
        }
        return null;
    }

    private void CheckThreshold()
    {
        var config = ConfigService.Instance.Config;
        if (!config.ThermalProtectionEnabled || !CurrentCpuTemp.HasValue) return;

        IsOverThreshold = CurrentCpuTemp.Value >= config.ThermalThreshold;

        if (IsOverThreshold && !_thermalPaused && KeepAwakeService.Instance.IsActive)
        {
            Logger.Warning("TemperatureMonitor", $"CPU temp {CurrentCpuTemp:F1}C exceeds threshold {config.ThermalThreshold}C - pausing");
            KeepAwakeService.Instance.SetActive(false);
            _thermalPaused = true;
            NotificationService.Instance.ShowWarning("Thermal Protection",
                $"CPU temperature ({CurrentCpuTemp:F1}C) exceeded {config.ThermalThreshold}C. Keep-awake paused.");
        }
        else if (!IsOverThreshold && _thermalPaused)
        {
            Logger.Info("TemperatureMonitor", $"CPU temp {CurrentCpuTemp:F1}C below threshold - resuming");
            KeepAwakeService.Instance.SetActive(true);
            _thermalPaused = false;
            NotificationService.Instance.ShowInfo("Thermal Protection",
                $"CPU temperature ({CurrentCpuTemp:F1}C) back to normal. Keep-awake resumed.");
        }
    }

    public string GetStatusText()
    {
        if (!CurrentCpuTemp.HasValue)
        {
            if (!string.IsNullOrEmpty(_lastError))
                return $"CPU Temperature: Not available ({_lastError})";
            return "CPU Temperature: Not available (no compatible sensors detected)";
        }

        var config = ConfigService.Instance.Config;
        var status = config.ThermalProtectionEnabled
            ? (IsOverThreshold ? " (OVER THRESHOLD)" : $" (threshold: {config.ThermalThreshold}C)")
            : "";
        return $"CPU Temperature: {CurrentCpuTemp:F1}C{status}";
    }
}
