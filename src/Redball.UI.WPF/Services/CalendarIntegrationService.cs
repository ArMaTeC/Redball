using Redball.Core.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Reads calendar events from a local JSON file and auto-activates keep-awake
/// during meetings and auto-deactivates during breaks.
/// Users can export their calendar to JSON or use a companion script.
/// File format: array of { "title": "...", "start": "ISO8601", "end": "ISO8601" }
/// </summary>
public class CalendarIntegrationService
{
    private static readonly Lazy<CalendarIntegrationService> _instance = new(() => new CalendarIntegrationService());
    public static CalendarIntegrationService Instance => _instance.Value;

    private readonly string _calendarFile;
    private readonly DispatcherTimer _checkTimer;
    private List<CalendarEvent> _events = new();
    private bool _calendarActivated;

    public bool IsEnabled { get; set; }
    public CalendarEvent? CurrentEvent { get; private set; }
    public CalendarEvent? NextEvent { get; private set; }

    private CalendarIntegrationService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Redball");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _calendarFile = Path.Combine(dir, "calendar.json");
        _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _checkTimer.Tick += CheckTimer_Tick;
        Logger.Verbose("CalendarIntegrationService", $"Calendar file: {_calendarFile}");
    }

    public void Start()
    {
        LoadEvents();
        _checkTimer.Start();
        CheckNow();
        Logger.Info("CalendarIntegrationService", $"Started with {_events.Count} events");
    }

    public void Stop()
    {
        _checkTimer.Stop();
        if (_calendarActivated)
        {
            KeepAwakeService.Instance.SetActive(false);
            _calendarActivated = false;
        }
        Logger.Info("CalendarIntegrationService", "Stopped");
    }

    public void LoadEvents()
    {
        try
        {
            if (File.Exists(_calendarFile))
            {
                var json = File.ReadAllText(_calendarFile);
                // SECURITY: Use SecureJsonSerializer with size limit and max depth
                _events = SecureJsonSerializer.Deserialize<List<CalendarEvent>>(json) ?? new();
                // Remove past events
                _events = _events.Where(e => e.End > DateTime.Now).ToList();
                Logger.Info("CalendarIntegrationService", $"Loaded {_events.Count} upcoming events");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("CalendarIntegrationService", "Failed to load calendar events", ex);
            _events = new();
        }
    }

    private void CheckTimer_Tick(object? sender, EventArgs e)
    {
        CheckNow();
    }

    private void CheckNow()
    {
        if (!IsEnabled) return;

        var now = DateTime.Now;
        CurrentEvent = _events.FirstOrDefault(e => e.Start <= now && e.End > now);
        NextEvent = _events.Where(e => e.Start > now).OrderBy(e => e.Start).FirstOrDefault();

        if (CurrentEvent != null && !_calendarActivated)
        {
            Logger.Info("CalendarIntegrationService", $"Meeting started: {CurrentEvent.Title}");
            KeepAwakeService.Instance.SetActive(true);
            _calendarActivated = true;
            NotificationService.Instance.ShowInfo("Calendar",
                $"Meeting \"{CurrentEvent.Title}\" started — keeping awake until {CurrentEvent.End:HH:mm}.");
        }
        else if (CurrentEvent == null && _calendarActivated)
        {
            Logger.Info("CalendarIntegrationService", "Meeting ended — deactivating");
            KeepAwakeService.Instance.SetActive(false);
            _calendarActivated = false;
            NotificationService.Instance.ShowInfo("Calendar", "Meeting ended — sleep allowed.");
        }
    }

    public string GetStatusText()
    {
        if (!IsEnabled) return "Calendar integration disabled";
        if (CurrentEvent != null)
            return $"In meeting: {CurrentEvent.Title} (until {CurrentEvent.End:HH:mm})";
        if (NextEvent != null)
            return $"Next: {NextEvent.Title} at {NextEvent.Start:HH:mm}";
        return $"No upcoming events ({_events.Count} loaded)";
    }
}

public class CalendarEvent
{
    public string Title { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}
