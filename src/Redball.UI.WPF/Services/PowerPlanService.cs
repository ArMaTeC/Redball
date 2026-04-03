using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Redball.UI.Services;

/// <summary>
/// Queries and switches Windows power plans via powercfg.
/// Allows Redball to switch to High Performance when active and restore the original plan on pause.
/// </summary>
public class PowerPlanService
{
    private static readonly Lazy<PowerPlanService> _instance = new(() => new PowerPlanService());
    public static PowerPlanService Instance => _instance.Value;

    private string? _originalPlanGuid;
    private bool _switched;

    public string? ActivePlanName { get; private set; }
    public string? ActivePlanGuid { get; private set; }

    private PowerPlanService()
    {
        RefreshActivePlan();
        Logger.Verbose("PowerPlanService", $"Active plan: {ActivePlanName} ({ActivePlanGuid})");
    }

    public List<(string Guid, string Name, bool IsActive)> GetPowerPlans()
    {
        var plans = new List<(string, string, bool)>();
        try
        {
            var output = RunPowercfg("/list");
            // Parse lines like: Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
            var regex = new Regex(@":\s+([0-9a-f\-]+)\s+\((.+?)\)(\s+\*)?", RegexOptions.IgnoreCase);
            foreach (var line in output.Split('\n'))
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    var guid = match.Groups[1].Value.Trim();
                    var name = match.Groups[2].Value.Trim();
                    var isActive = match.Groups[3].Success;
                    plans.Add((guid, name, isActive));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("PowerPlanService", "Failed to list power plans", ex);
        }
        return plans;
    }

    public void RefreshActivePlan()
    {
        try
        {
            var plans = GetPowerPlans();
            var active = plans.FirstOrDefault(p => p.IsActive);
            ActivePlanGuid = active.Guid;
            ActivePlanName = active.Name;
        }
        catch (Exception ex)
        {
            Logger.Error("PowerPlanService", "Failed to refresh active plan", ex);
        }
    }

    public bool SwitchPlan(string guid)
    {
        try
        {
            RunPowercfg($"/setactive {guid}");
            RefreshActivePlan();
            Logger.Info("PowerPlanService", $"Switched to plan: {ActivePlanName} ({guid})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("PowerPlanService", $"Failed to switch plan to {guid}", ex);
            return false;
        }
    }

    public void SwitchToHighPerformance()
    {
        if (_switched) return;

        _originalPlanGuid = ActivePlanGuid;
        var plans = GetPowerPlans();
        var highPerf = plans.FirstOrDefault(p =>
            p.Name.Contains("High performance", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(highPerf.Guid))
        {
            SwitchPlan(highPerf.Guid);
            _switched = true;
            Logger.Info("PowerPlanService", $"Switched to high performance, original: {_originalPlanGuid}");
        }
        else
        {
            Logger.Warning("PowerPlanService", "No High Performance plan found");
        }
    }

    public void RestoreOriginalPlan()
    {
        if (!_switched || string.IsNullOrEmpty(_originalPlanGuid)) return;

        SwitchPlan(_originalPlanGuid);
        _switched = false;
        Logger.Info("PowerPlanService", "Restored original power plan");
    }

    private static string RunPowercfg(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powercfg",
            Arguments = args,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        if (proc == null) return "";
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return output;
    }
}
