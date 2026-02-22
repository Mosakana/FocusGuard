using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusGuard.Core.Sessions;

namespace FocusGuard.App.ViewModels;

public partial class TimerOverlayViewModel : ObservableObject, IDisposable
{
    private readonly IFocusSessionManager _sessionManager;
    private readonly PomodoroTimer _pomodoroTimer;
    private bool _disposed;

    [ObservableProperty]
    private string _timerDisplay = "00:00";

    [ObservableProperty]
    private double _timerProgress;

    [ObservableProperty]
    private string _intervalLabel = string.Empty;

    [ObservableProperty]
    private string _pomodoroCountDisplay = string.Empty;

    [ObservableProperty]
    private string _profileName = string.Empty;

    public TimerOverlayViewModel(
        IFocusSessionManager sessionManager,
        PomodoroTimer pomodoroTimer)
    {
        _sessionManager = sessionManager;
        _pomodoroTimer = pomodoroTimer;

        _pomodoroTimer.TimerTick += OnTimerTick;
        _pomodoroTimer.IntervalStarted += OnIntervalStarted;
        _sessionManager.StateChanged += OnSessionStateChanged;

        // Initialize from current state
        UpdateFromSession();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_pomodoroTimer.IsRunning)
            {
                var remaining = _pomodoroTimer.IntervalRemaining;
                TimerDisplay = remaining.TotalHours >= 1
                    ? remaining.ToString(@"h\:mm\:ss")
                    : remaining.ToString(@"mm\:ss");
                TimerProgress = _pomodoroTimer.IntervalProgress;
            }
            else
            {
                UpdateFromSession();
            }
        });
    }

    private void OnIntervalStarted(object? sender, FocusSessionState state)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IntervalLabel = state switch
            {
                FocusSessionState.Working => "Focus",
                FocusSessionState.ShortBreak => "Short Break",
                FocusSessionState.LongBreak => "Long Break",
                _ => string.Empty
            };

            var session = _sessionManager.CurrentSession;
            if (session is not null)
            {
                PomodoroCountDisplay = session.IsPomodoroEnabled
                    ? $"#{session.PomodoroCompletedCount + 1}"
                    : string.Empty;
            }
        });
    }

    private void OnSessionStateChanged(object? sender, FocusSessionState state)
    {
        Application.Current?.Dispatcher.InvokeAsync(UpdateFromSession);
    }

    private void UpdateFromSession()
    {
        var session = _sessionManager.CurrentSession;
        if (session is null)
        {
            TimerDisplay = "00:00";
            TimerProgress = 0;
            IntervalLabel = string.Empty;
            PomodoroCountDisplay = string.Empty;
            ProfileName = string.Empty;
            return;
        }

        ProfileName = session.ProfileName;

        if (_pomodoroTimer.IsRunning)
        {
            var remaining = _pomodoroTimer.IntervalRemaining;
            TimerDisplay = remaining.TotalHours >= 1
                ? remaining.ToString(@"h\:mm\:ss")
                : remaining.ToString(@"mm\:ss");
            TimerProgress = _pomodoroTimer.IntervalProgress;
        }
        else
        {
            var remaining = session.CurrentIntervalRemaining;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            TimerDisplay = remaining.TotalHours >= 1
                ? remaining.ToString(@"h\:mm\:ss")
                : remaining.ToString(@"mm\:ss");

            var totalSeconds = session.TotalPlanned.TotalSeconds;
            TimerProgress = totalSeconds > 0
                ? session.Elapsed.TotalSeconds / totalSeconds
                : 0;
        }

        IntervalLabel = _sessionManager.CurrentState switch
        {
            FocusSessionState.Working => "Focus",
            FocusSessionState.ShortBreak => "Short Break",
            FocusSessionState.LongBreak => "Long Break",
            _ => string.Empty
        };

        PomodoroCountDisplay = session.IsPomodoroEnabled
            ? $"#{session.PomodoroCompletedCount + 1}"
            : string.Empty;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pomodoroTimer.TimerTick -= OnTimerTick;
        _pomodoroTimer.IntervalStarted -= OnIntervalStarted;
        _sessionManager.StateChanged -= OnSessionStateChanged;
    }
}
