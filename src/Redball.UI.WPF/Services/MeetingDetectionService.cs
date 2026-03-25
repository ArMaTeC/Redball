using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Detects if the user is in a meeting by checking active camera/microphone usage
/// via Windows Privacy registry keys.
/// </summary>
public class MeetingDetectionService
{
    private static readonly Lazy<MeetingDetectionService> _instance = new(() => new MeetingDetectionService());
    public static MeetingDetectionService Instance => _instance.Value;

    private const string WebcamKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
    private const string MicrophoneKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    private bool _isMeetingActive;
    public bool IsMeetingActive => _isMeetingActive;

    public event EventHandler<bool>? MeetingStateChanged;

    private MeetingDetectionService()
    {
    }

    /// <summary>
    /// Polls the system for active media captures.
    /// CheckAndUpdate should be called by a timer (e.g. from KeepAwakeService).
    /// </summary>
    public void CheckAndUpdate()
    {
        bool cameraActive = IsCaptureDeviceInUse(WebcamKey);
        bool micActive = IsCaptureDeviceInUse(MicrophoneKey);
        
        bool currentMeeting = cameraActive || micActive;
        
        if (currentMeeting != _isMeetingActive)
        {
            _isMeetingActive = currentMeeting;
            Logger.Info("MeetingDetectionService", $"Meeting state changed: {currentMeeting} (Cam={cameraActive}, Mic={micActive})");
            MeetingStateChanged?.Invoke(this, currentMeeting);
        }
    }

    private bool IsCaptureDeviceInUse(string registryPath)
    {
        try
        {
            using var rootKey = Registry.CurrentUser.OpenSubKey(registryPath);
            if (rootKey == null) return false;

            // Windows 10/11 stores app-specific usage info in subkeys
            foreach (var subKeyName in rootKey.GetSubKeyNames())
            {
                if (subKeyName == "NonPackaged")
                {
                    // For desktop apps (Zoom, Teams, etc.), info is in NonPackaged sub-subkeys
                    using var nonPackagedKey = rootKey.OpenSubKey(subKeyName);
                    if (nonPackagedKey != null)
                    {
                        foreach (var appKeyName in nonPackagedKey.GetSubKeyNames())
                        {
                            if (IsAppUsingDevice(nonPackagedKey.OpenSubKey(appKeyName)))
                                return true;
                        }
                    }
                }
                else
                {
                    // For Store apps
                    if (IsAppUsingDevice(rootKey.OpenSubKey(subKeyName)))
                        return true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("MeetingDetectionService", $"Error checking {registryPath}: {ex.Message}");
        }
        return false;
    }

    private bool IsAppUsingDevice(RegistryKey? key)
    {
        if (key == null) return false;
        try
        {
            using (key)
            {
                // LastUsedTimeStop is 0 if the device is currently in use
                var stopTime = key.GetValue("LastUsedTimeStop");
                if (stopTime is long timeValue && timeValue == 0)
                {
                    // Double check LastUsedTimeStart is NOT 0
                    var startTime = key.GetValue("LastUsedTimeStart");
                    return startTime is long startValue && startValue > 0;
                }
            }
        }
        catch { }
        return false;
    }
}
