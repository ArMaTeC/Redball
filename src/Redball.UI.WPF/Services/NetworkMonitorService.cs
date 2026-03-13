using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace Redball.UI.Services;

/// <summary>
/// Monitors network connectivity and auto-pauses keep-awake when disconnected.
/// Port of Get-NetworkStatus, Update-NetworkAwareState.
/// </summary>
public class NetworkMonitorService
{
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Checks if any operational network adapter is available.
    /// </summary>
    public bool IsConnected()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(ni => ni.OperationalStatus == OperationalStatus.Up &&
                           ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                           ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel);
        }
        catch (Exception ex)
        {
            Logger.Debug("NetworkMonitor", $"Network query failed: {ex.Message}");
            return true; // Assume connected on error
        }
    }

    /// <summary>
    /// Checks network and auto-pauses/resumes keep-awake as needed.
    /// </summary>
    public void CheckAndUpdate(KeepAwakeService keepAwake)
    {
        if (!IsEnabled) return;

        var connected = IsConnected();

        if (!connected && keepAwake.IsActive && !keepAwake.AutoPausedNetwork)
        {
            Logger.Info("NetworkMonitor", "Auto-pausing: network disconnected");
            keepAwake.AutoPause("Network");
        }
        else if (connected && keepAwake.AutoPausedNetwork)
        {
            Logger.Info("NetworkMonitor", "Auto-resuming: network reconnected");
            keepAwake.AutoResume("Network");
        }
    }
}
