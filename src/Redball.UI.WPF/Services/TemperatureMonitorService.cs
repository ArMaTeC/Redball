using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace Redball.UI.Services;

/// <summary>
/// Monitors CPU temperature via multiple methods (LibreHardwareMonitor, WMI, Performance Counters, MSR) 
/// and optionally auto-pauses keep-awake if temperature exceeds a configured threshold (thermal protection).
/// LibreHardwareMonitor provides the most reliable detection across modern CPUs and motherboards.
/// </summary>
public class TemperatureMonitorService : IDisposable
{
    private static readonly Lazy<TemperatureMonitorService> _instance = new(() => new TemperatureMonitorService());
    public static TemperatureMonitorService Instance => _instance.Value;

    private readonly DispatcherTimer _pollTimer;
    private readonly Computer? _computer;
    private bool _thermalPaused;
    private string _lastError = "";
    private int _consecutiveFailures;
    private bool _hasLoggedAllMethodsFailed;
    private bool _libreHardwareInitialized;
    private readonly string[] _wmiClassesToTry = 
    {
        "MSAcpi_ThermalZoneTemperature",
        "Win32_PerfFormattedData_Counters_ThermalZoneInformation",
        "CIM_Sensor"
    };

    public double? CurrentCpuTemp { get; private set; }
    public bool IsOverThreshold { get; private set; }
    public string LastError => _lastError;
    public string ActiveSensorName { get; private set; } = "None";

    public event EventHandler<double>? TemperatureUpdated;

    private TemperatureMonitorService()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _pollTimer.Tick += PollTimer_Tick;
        
        // Initialize LibreHardwareMonitor
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true  // For motherboard sensors like NCT/ITE
            };
            _computer.Open();
            _libreHardwareInitialized = true;
            Logger.Info("TemperatureMonitor", "LibreHardwareMonitor initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.Warning("TemperatureMonitor", $"Failed to initialize LibreHardwareMonitor: {ex.Message}");
            _computer = null;
            _libreHardwareInitialized = false;
        }
        
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

        // Try LibreHardwareMonitor first (most reliable for modern CPUs)
        if (_libreHardwareInitialized)
        {
            temp ??= TryLibreHardwareMonitor();
        }

        // Fallback to native methods
        temp ??= TryMsrDirect();  // Intel/AMD MSR registers
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
            _hasLoggedAllMethodsFailed = false; // Reset on success
            TemperatureUpdated?.Invoke(this, temp.Value);
        }
        else
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= 3)
            {
                CurrentCpuTemp = null;
            }
            // Only log once when all methods fail, not every poll
            if (!_hasLoggedAllMethodsFailed && _consecutiveFailures >= 3)
            {
                Logger.Debug("TemperatureMonitor", "No temperature sensors available (all methods failed)");
                _hasLoggedAllMethodsFailed = true;
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
        catch (Exception)
        {
            // Failures logged in aggregate after all methods tried
        }
        return null;
    }

    private double? TryLibreHardwareMonitor()
    {
        try
        {
            if (_computer == null) return null;
            
            // Update hardware tree to get fresh readings
            _computer.Accept(new UpdateVisitor());
            
            // Look for CPU temperature sensors
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    hardware.Update();
                    
                    // Check CPU package temperature first (most accurate)
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && 
                            sensor.Value.HasValue)
                        {
                            var name = sensor.Name?.ToLower() ?? "";
                            
                            // Prioritize package/core average over individual cores
                            if (name.Contains("package") || name.Contains("tdie") || name.Contains("tctl"))
                            {
                                var celsius = sensor.Value.Value;
                                if (celsius > 0 && celsius < 150)
                                {
                                    ActiveSensorName = $"CPU {sensor.Name}";
                                    Logger.Verbose("TemperatureMonitor", $"Got temp from LibreHardwareMonitor ({sensor.Name}): {celsius:F1}C");
                                    return celsius;
                                }
                            }
                        }
                    }
                    
                    // Fallback to any CPU temperature sensor
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && 
                            sensor.Value.HasValue)
                        {
                            var celsius = sensor.Value.Value;
                            if (celsius > 0 && celsius < 150)
                            {
                                ActiveSensorName = $"CPU {sensor.Name}";
                                Logger.Verbose("TemperatureMonitor", $"Got temp from LibreHardwareMonitor ({sensor.Name}): {celsius:F1}C");
                                return celsius;
                            }
                        }
                    }
                }
            }
            
            // Check motherboard sensors (some report CPU temp via Super I/O)
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Motherboard)
                {
                    hardware.Update();
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        foreach (var sensor in subHardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Temperature && 
                                sensor.Value.HasValue)
                            {
                                var name = sensor.Name?.ToLower() ?? "";
                                if (name.Contains("cpu") || name.Contains("processor"))
                                {
                                    var celsius = sensor.Value.Value;
                                    if (celsius > 0 && celsius < 150)
                                    {
                                        ActiveSensorName = $"MB {sensor.Name}";
                                        Logger.Verbose("TemperatureMonitor", $"Got temp from motherboard ({sensor.Name}): {celsius:F1}C");
                                        return celsius;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("TemperatureMonitor", $"LibreHardwareMonitor failed: {ex.Message}");
        }
        return null;
    }

    private class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) { }
        public void VisitHardware(IHardware hardware) { }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    private double? TryMsrDirect()
    {
        return null; // Placeholder - MSR reading requires Ring0 driver which LibreHardwareMonitor handles internally
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
        catch (Exception)
        {
            // Failures logged in aggregate after all methods tried
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
        catch (Exception)
        {
            // Failures logged in aggregate after all methods tried
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
        catch (Exception)
        {
            // Failures logged in aggregate after all methods tried
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
        catch (Exception)
        {
            // Failures logged in aggregate after all methods tried
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
        catch (Exception)
        {
            // Failures logged in aggregate after all methods tried
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
        catch (Exception)
        {
            // Failures logged in aggregate after all methods tried
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
        return $"CPU Temperature: {CurrentCpuTemp:F1}C [{ActiveSensorName}]{status}";
    }

    public void Dispose()
    {
        _pollTimer?.Stop();
        try
        {
            _computer?.Close();
        }
        catch (Exception ex)
        {
            Logger.Debug("TemperatureMonitor", $"Error disposing LibreHardwareMonitor: {ex.Message}");
        }
    }
}
