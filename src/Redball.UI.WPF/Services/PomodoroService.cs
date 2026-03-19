using System;
using System.Windows.Threading;

namespace Redball.UI.Services;

/// <summary>
/// Pomodoro technique timer: focus → break → repeat.
/// Integrates with KeepAwakeService to keep awake during focus and optionally during breaks.
/// </summary>
public class PomodoroService
{
    private static readonly Lazy<PomodoroService> _instance = new(() => new PomodoroService());
    public static PomodoroService Instance => _instance.Value;

    private readonly DispatcherTimer _timer;
    private DateTime _phaseStartTime;
    private int _completedFocusSessions;

    public event EventHandler? StateChanged;
    public event EventHandler<string>? PhaseCompleted;

    public enum PomodoroPhase { Idle, Focus, Break, LongBreak }

    public PomodoroPhase CurrentPhase { get; private set; } = PomodoroPhase.Idle;
    public bool IsRunning => CurrentPhase != PomodoroPhase.Idle;
    public int CompletedSessions => _completedFocusSessions;
    public TimeSpan Remaining => GetRemaining();
    public TimeSpan PhaseDuration => GetPhaseDuration();

    private PomodoroService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        Logger.Verbose("PomodoroService", "Instance created");
    }

    public void Start()
    {
        if (IsRunning) return;
        _completedFocusSessions = 0;
        StartPhase(PomodoroPhase.Focus);
        Logger.Info("PomodoroService", "Pomodoro started");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _timer.Stop();
        CurrentPhase = PomodoroPhase.Idle;
        StateChanged?.Invoke(this, EventArgs.Empty);
        Logger.Info("PomodoroService", $"Pomodoro stopped after {_completedFocusSessions} sessions");
    }

    public void Skip()
    {
        if (!IsRunning) return;
        Logger.Info("PomodoroService", $"Skipping {CurrentPhase} phase");
        AdvancePhase();
    }

    private void StartPhase(PomodoroPhase phase)
    {
        CurrentPhase = phase;
        _phaseStartTime = DateTime.Now;
        _timer.Start();

        var config = ConfigService.Instance.Config;

        // Manage keep-awake based on phase
        if (phase == PomodoroPhase.Focus)
        {
            KeepAwakeService.Instance.SetActive(true);
        }
        else
        {
            // During breaks, only keep awake if configured
            if (!config.PomodoroKeepAwakeDuringBreak)
                KeepAwakeService.Instance.SetActive(false);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        Logger.Info("PomodoroService", $"Phase started: {phase}, duration: {GetPhaseDuration().TotalMinutes} min");
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var remaining = GetRemaining();
        if (remaining <= TimeSpan.Zero)
        {
            AdvancePhase();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AdvancePhase()
    {
        var previousPhase = CurrentPhase;
        var config = ConfigService.Instance.Config;

        switch (CurrentPhase)
        {
            case PomodoroPhase.Focus:
                _completedFocusSessions++;
                PhaseCompleted?.Invoke(this, "Focus session complete! Time for a break.");

                if (_completedFocusSessions % config.PomodoroLongBreakInterval == 0)
                    StartPhase(PomodoroPhase.LongBreak);
                else
                    StartPhase(PomodoroPhase.Break);
                break;

            case PomodoroPhase.Break:
            case PomodoroPhase.LongBreak:
                PhaseCompleted?.Invoke(this, "Break over! Ready to focus.");
                if (config.PomodoroAutoStart)
                    StartPhase(PomodoroPhase.Focus);
                else
                    Stop();
                break;

            default:
                Stop();
                break;
        }
    }

    private TimeSpan GetPhaseDuration()
    {
        var config = ConfigService.Instance.Config;
        return CurrentPhase switch
        {
            PomodoroPhase.Focus => TimeSpan.FromMinutes(config.PomodoroFocusMinutes),
            PomodoroPhase.Break => TimeSpan.FromMinutes(config.PomodoroBreakMinutes),
            PomodoroPhase.LongBreak => TimeSpan.FromMinutes(config.PomodoroLongBreakMinutes),
            _ => TimeSpan.Zero
        };
    }

    private TimeSpan GetRemaining()
    {
        if (!IsRunning) return TimeSpan.Zero;
        var elapsed = DateTime.Now - _phaseStartTime;
        var remaining = GetPhaseDuration() - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public string GetStatusText()
    {
        if (!IsRunning) return "Pomodoro: Idle";
        var phase = CurrentPhase switch
        {
            PomodoroPhase.Focus => "Focus",
            PomodoroPhase.Break => "Break",
            PomodoroPhase.LongBreak => "Long Break",
            _ => "Idle"
        };
        var r = Remaining;
        return $"Pomodoro: {phase} — {r.Minutes:D2}:{r.Seconds:D2} left (#{_completedFocusSessions + (CurrentPhase == PomodoroPhase.Focus ? 1 : 0)})";
    }
}
