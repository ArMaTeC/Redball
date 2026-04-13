using System;
using System.Linq;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Monitors webcam usage to allow intelligent behavior during video calls.
/// Polling-based approach using Windows Registry keys that track active camera streams.
/// </summary>
public class WebcamDetectionService
{
    private static readonly Lazy<WebcamDetectionService> _instance = new(() => new WebcamDetectionService());
    public static WebcamDetectionService Instance => _instance.Value;

    private const string WebcamRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
    private bool _isWebcamInUse;

    public bool IsWebcamInUse
    {
        get => _isWebcamInUse;
        private set
        {
            if (_isWebcamInUse != value)
            {
                _isWebcamInUse = value;
                WebcamStateChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<bool>? WebcamStateChanged;

    private WebcamDetectionService() { }

    /// <summary>
    /// Checks the registry to see if any app currently has an active webcam stream.
    /// </summary>
    public bool CheckWebcamStatus()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(WebcamRegistryPath);
            if (key == null) return false;

            // Check non-packaged apps (Desktop apps)
            var desktopAppNames = key.GetSubKeyNames();
            foreach (var appName in desktopAppNames)
            {
                if (appName == "NonPackaged")
                {
                    using var nonPackagedKey = key.OpenSubKey("NonPackaged");
                    if (nonPackagedKey != null)
                    {
                        foreach (var subKeyName in nonPackagedKey.GetSubKeyNames())
                        {
                            using var subKey = nonPackagedKey.OpenSubKey(subKeyName);
                            if (subKey != null)
                            {
                                var lastUsedTimeStop = (long)(subKey.GetValue("LastUsedTimeStop") ?? 0L);
                                if (lastUsedTimeStop == 0) // No stop time means it's currently running
                                {
                                    IsWebcamInUse = true;
                                    return true;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Check packaged apps (UWP/Microsoft Store)
                    using var appKey = key.OpenSubKey(appName);
                    if (appKey != null)
                    {
                        var lastUsedTimeStop = (long)(appKey.GetValue("LastUsedTimeStop") ?? 0L);
                        if (lastUsedTimeStop == 0)
                        {
                            IsWebcamInUse = true;
                            return true;
                        }
                    }
                }
            }

            IsWebcamInUse = false;
            return false;
        }
        catch (Exception ex)
        {
            Logger.Debug("WebcamDetectionService", $"Failed to check webcam status: {ex.Message}");
            return false;
        }
    }
}
