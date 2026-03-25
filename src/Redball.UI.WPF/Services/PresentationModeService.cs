using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace Redball.UI.Services;

/// <summary>
/// Detects presentation mode (PowerPoint, Teams screen share, Windows presentation settings)
/// and auto-activates keep-awake during presentations.
/// Port of Test-PresentationMode, Update-PresentationModeState.
/// </summary>
public class PresentationModeService
{
    private DateTime? _lastCheck;
    private PresentationStatus _cachedStatus = new();
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromSeconds(10);

    public bool IsEnabled { get; set; }

    // Track whether we auto-activated for a presentation
    private bool _autoActivated;

    /// <summary>
    /// Checks if a presentation is currently active. Results cached for 10 seconds.
    /// </summary>
    public PresentationStatus GetStatus()
    {
        if (_lastCheck.HasValue && (DateTime.Now - _lastCheck.Value) < CacheExpiry)
        {
            return _cachedStatus;
        }

        try
        {
            // Check for PowerPoint presentation mode
            var powerPoint = Process.GetProcessesByName("POWERPNT");
            if (powerPoint.Length > 0)
            {
                _cachedStatus = new PresentationStatus { IsPresenting = true, Source = "PowerPoint" };
                _lastCheck = DateTime.Now;
                return _cachedStatus;
            }

            // Check for Teams screen sharing
            var teams = Process.GetProcessesByName("Teams");
            if (teams.Length > 0)
            {
                var teamsTitle = teams.FirstOrDefault(t => !string.IsNullOrEmpty(t.MainWindowTitle))?.MainWindowTitle ?? "";
                if (teamsTitle.Contains("Sharing", StringComparison.OrdinalIgnoreCase) ||
                    teamsTitle.Contains("Presenting", StringComparison.OrdinalIgnoreCase) ||
                    teamsTitle.Contains("Screen sharing", StringComparison.OrdinalIgnoreCase))
                {
                    _cachedStatus = new PresentationStatus { IsPresenting = true, Source = "Teams" };
                    _lastCheck = DateTime.Now;
                    return _cachedStatus;
                }
            }

            // Check Windows presentation mode registry
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\MobilePC\AdaptableSettings");
                var value = key?.GetValue("PresentationMode");
                if (value is int mode && mode == 1)
                {
                    _cachedStatus = new PresentationStatus { IsPresenting = true, Source = "Windows Presentation Mode" };
                    _lastCheck = DateTime.Now;
                    return _cachedStatus;
                }
            }
            catch
            {
                // Registry access may fail, ignore
            }

            _cachedStatus = new PresentationStatus { IsPresenting = false };
            _lastCheck = DateTime.Now;
            return _cachedStatus;
        }
        catch (Exception ex)
        {
            Logger.Debug("PresentationMode", $"Presentation check failed: {ex.Message}");
            return new PresentationStatus { IsPresenting = false };
        }
    }

    /// <summary>
    /// Checks for presentation mode and auto-activates keep-awake if detected.
    /// </summary>
    public void CheckAndUpdate(KeepAwakeService keepAwake)
    {
        if (!IsEnabled) return;

        var status = GetStatus();

        if (status.IsPresenting && !keepAwake.IsActive && !_autoActivated)
        {
            Logger.Info("PresentationMode", $"Auto-activating for {status.Source} presentation");
            _autoActivated = true;
            keepAwake.SetActive(true);
        }
        else if (!status.IsPresenting && _autoActivated)
        {
            Logger.Info("PresentationMode", "Presentation ended, clearing auto-activate flag");
            _autoActivated = false;
            // Don't auto-stop - user may want to keep it on
        }
    }
}

public class PresentationStatus
{
    public bool IsPresenting { get; set; }
    public string Source { get; set; } = "";
}
