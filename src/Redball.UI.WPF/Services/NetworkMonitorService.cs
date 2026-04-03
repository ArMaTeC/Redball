using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace Redball.UI.Services;

/// <summary>
/// Monitors network connectivity and auto-pauses keep-awake when disconnected.
/// Also provides VPN detection and WiFi network identification.
/// Port of Get-NetworkStatus, Update-NetworkAwareState.
/// </summary>
public class NetworkMonitorService
{
    public bool IsEnabled { get; set; }
    public bool VpnAutoKeepAwake { get; set; }

    private bool _vpnWasActive;

    /// <summary>
    /// Checks if a VPN connection is currently active by looking for PPP or tunnel adapters.
    /// </summary>
    public bool IsVpnConnected()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(ni => ni.OperationalStatus == OperationalStatus.Up &&
                           (ni.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
                            ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                            ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                            ni.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) ||
                            ni.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            Logger.Debug("NetworkMonitor", $"VPN detection failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns the name of the connected WiFi network, or null if not connected via WiFi.
    /// </summary>
    public string? GetConnectedWifiName()
    {
        try
        {
            var wifi = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                      ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            return wifi?.Name;
        }
        catch (Exception ex)
        {
            Logger.Debug("NetworkMonitor", $"Failed to get WiFi name: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns a list of active network adapter descriptions for diagnostics.
    /// </summary>
    public List<string> GetActiveAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(ni => $"{ni.Name} ({ni.NetworkInterfaceType})")
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Debug("NetworkMonitor", $"Failed to get active adapters: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Checks VPN status and auto-enables keep-awake when VPN is active.
    /// </summary>
    public void CheckVpnAndUpdate(KeepAwakeService keepAwake)
    {
        if (!VpnAutoKeepAwake) return;

        var vpnActive = IsVpnConnected();
        if (vpnActive && !_vpnWasActive)
        {
            Logger.Info("NetworkMonitor", "VPN detected — auto-activating keep-awake");
            keepAwake.SetActive(true);
            NotificationService.Instance.ShowInfo("VPN Detected", "VPN connected — keeping system awake.");
        }
        else if (!vpnActive && _vpnWasActive)
        {
            Logger.Info("NetworkMonitor", "VPN disconnected — deactivating keep-awake");
            keepAwake.SetActive(false);
            NotificationService.Instance.ShowInfo("VPN Disconnected", "VPN disconnected — sleep allowed.");
        }
        _vpnWasActive = vpnActive;
    }

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
