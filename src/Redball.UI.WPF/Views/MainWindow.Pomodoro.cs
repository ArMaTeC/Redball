using System;
using System.Windows;
using Redball.UI.Services;

namespace Redball.UI.Views;

/// <summary>
/// Partial class: Pomodoro timer UI handlers and service integration.
/// </summary>
public partial class MainWindow
{
    private void InitializePomodoro()
    {
        var pomodoro = PomodoroService.Instance;
        pomodoro.StateChanged += Pomodoro_StateChanged;
        pomodoro.PhaseCompleted += Pomodoro_PhaseCompleted;

        // Load config into sliders
        var config = ConfigService.Instance.Config;
        PomodoroFocusSlider.Value = config.PomodoroFocusMinutes;
        PomodoroBreakSlider.Value = config.PomodoroBreakMinutes;
        PomodoroLongBreakSlider.Value = config.PomodoroLongBreakMinutes;
        PomodoroLongBreakIntervalSlider.Value = config.PomodoroLongBreakInterval;
        PomodoroAutoStartCheck.IsChecked = config.PomodoroAutoStart;
        PomodoroKeepAwakeBreakCheck.IsChecked = config.PomodoroKeepAwakeDuringBreak;

        UpdatePomodoroUI();
    }

    private void Pomodoro_StateChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(UpdatePomodoroUI);
    }

    private void Pomodoro_PhaseCompleted(object? sender, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            NotificationService.Instance.ShowInfo("Pomodoro", message);
        });
    }

    private void UpdatePomodoroUI()
    {
        var pomodoro = PomodoroService.Instance;
        var isRunning = pomodoro.IsRunning;

        PomodoroStartBtn.IsEnabled = !isRunning;
        PomodoroStopBtn.IsEnabled = isRunning;
        PomodoroSkipBtn.IsEnabled = isRunning;

        PomodoroPhaseText.Text = pomodoro.CurrentPhase switch
        {
            PomodoroService.PomodoroPhase.Focus => "Focus",
            PomodoroService.PomodoroPhase.Break => "Break",
            PomodoroService.PomodoroPhase.LongBreak => "Long Break",
            _ => "Idle"
        };

        var r = pomodoro.Remaining;
        PomodoroTimerText.Text = isRunning ? $"{r.Minutes:D2}:{r.Seconds:D2}" : "--:--";
        PomodoroSessionsText.Text = $"Sessions completed: {pomodoro.CompletedSessions}";
    }

    private void PomodoroStart_Click(object sender, RoutedEventArgs e)
    {
        SavePomodoroSettings();
        PomodoroService.Instance.Start();
        _analytics.TrackFeature("pomodoro.started");
    }

    private void PomodoroStop_Click(object sender, RoutedEventArgs e)
    {
        PomodoroService.Instance.Stop();
        _analytics.TrackFeature("pomodoro.stopped");
    }

    private void PomodoroSkip_Click(object sender, RoutedEventArgs e)
    {
        PomodoroService.Instance.Skip();
        _analytics.TrackFeature("pomodoro.skipped");
    }

    private void PomodoroSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PomodoroFocusText != null)
            PomodoroFocusText.Text = $"Focus: {(int)PomodoroFocusSlider.Value} min";
        if (PomodoroBreakText != null)
            PomodoroBreakText.Text = $"Break: {(int)PomodoroBreakSlider.Value} min";
        if (PomodoroLongBreakText != null)
            PomodoroLongBreakText.Text = $"Long break: {(int)PomodoroLongBreakSlider.Value} min";
        if (PomodoroLongBreakIntervalText != null)
            PomodoroLongBreakIntervalText.Text = $"Long break after: {(int)PomodoroLongBreakIntervalSlider.Value} sessions";

        SavePomodoroSettings();
    }

    private void PomodoroSettingChanged(object sender, RoutedEventArgs e)
    {
        SavePomodoroSettings();
    }

    private void SavePomodoroSettings()
    {
        if (_isLoadingSettings) return;
        var config = ConfigService.Instance.Config;
        config.PomodoroFocusMinutes = (int)PomodoroFocusSlider.Value;
        config.PomodoroBreakMinutes = (int)PomodoroBreakSlider.Value;
        config.PomodoroLongBreakMinutes = (int)PomodoroLongBreakSlider.Value;
        config.PomodoroLongBreakInterval = (int)PomodoroLongBreakIntervalSlider.Value;
        config.PomodoroAutoStart = PomodoroAutoStartCheck.IsChecked ?? true;
        config.PomodoroKeepAwakeDuringBreak = PomodoroKeepAwakeBreakCheck.IsChecked ?? false;
        ConfigService.Instance.Save();
    }
}
