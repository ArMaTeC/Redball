using System;
using System.Windows.Threading;

namespace Redball.UI.Services;

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

    private PomodoroService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
    }

    public void Start()
    {
        if (IsRunning) return;
        _completedFocusSessions = 0;
        StartPhase(PomodoroPhase.Focus);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _timer.Stop();
        CurrentPhase = PomodoroPhase.Idle;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Skip()
    {
        if (!IsRunning) return;
        AdvancePhase();
    }

    private void StartPhase(PomodoroPhase phase)
    {
        CurrentPhase = phase;
        _phaseStartTime = DateTime.Now;
        _timer.Start();

        var config = ConfigService.Instance.Config;
        if (phase == PomodoroPhase.Focus)
        {
            KeepAwakeService.Instance.SetActive(true);
        }
        else if (!config.PomodoroKeepAwakeDuringBreak)
        {
            KeepAwakeService.Instance.SetActive(false);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (GetRemaining() <= TimeSpan.Zero)
        {
            AdvancePhase();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AdvancePhase()
    {
        var config = ConfigService.Instance.Config;
        switch (CurrentPhase)
        {
            case PomodoroPhase.Focus:
                _completedFocusSessions++;
                PhaseCompleted?.Invoke(this, "Focus session complete! Time for a break.");
                if (_completedFocusSessions % config.PomodoroLongBreakInterval == 0)
                {
                    StartPhase(PomodoroPhase.LongBreak);
                }
                else
                {
                    StartPhase(PomodoroPhase.Break);
                }
                break;
            case PomodoroPhase.Break:
            case PomodoroPhase.LongBreak:
                PhaseCompleted?.Invoke(this, "Break over! Ready to focus.");
                if (config.PomodoroAutoStart)
                {
                    StartPhase(PomodoroPhase.Focus);
                }
                else
                {
                    Stop();
                }
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
        var remaining = GetPhaseDuration() - (DateTime.Now - _phaseStartTime);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
