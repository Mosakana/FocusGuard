using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Security;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Sessions;

public class PomodoroTimer : IDisposable
{
    private readonly IFocusSessionManager _sessionManager;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<PomodoroTimer> _logger;

    private System.Timers.Timer? _tickTimer;
    private DateTime _intervalStartTimeUtc;
    private TimeSpan _intervalDuration;
    private PomodoroConfiguration _config = new();
    private bool _isRunning;

    /// <summary>Remaining time in the current interval (work/short break/long break).</summary>
    public TimeSpan IntervalRemaining { get; private set; }

    /// <summary>Progress within the current interval (0.0 = just started, 1.0 = complete).</summary>
    public double IntervalProgress { get; private set; }

    /// <summary>The current Pomodoro configuration.</summary>
    public PomodoroConfiguration Configuration => _config;

    /// <summary>Whether the timer is currently running.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>Fires every second while a session is active, for UI refresh.</summary>
    public event EventHandler? TimerTick;

    /// <summary>Fires when an interval starts (work, short break, long break).</summary>
    public event EventHandler<FocusSessionState>? IntervalStarted;

    /// <summary>Fires when an interval completes (before advancing to next).</summary>
    public event EventHandler<FocusSessionState>? IntervalCompleted;

    public PomodoroTimer(
        IFocusSessionManager sessionManager,
        ISettingsRepository settingsRepository,
        ILogger<PomodoroTimer> logger)
    {
        _sessionManager = sessionManager;
        _settingsRepository = settingsRepository;
        _logger = logger;

        _sessionManager.StateChanged += OnSessionStateChanged;
        _sessionManager.PomodoroIntervalChanged += OnPomodoroIntervalChanged;
    }

    /// <summary>
    /// Starts the 1-second tick timer for the current session's work interval.
    /// Called when session enters Working state.
    /// </summary>
    public async Task StartAsync()
    {
        await LoadConfigurationAsync();
        StartInterval(_sessionManager.CurrentState);
    }

    /// <summary>Stops the tick timer.</summary>
    public void Stop()
    {
        StopTickTimer();
        _isRunning = false;
        IntervalRemaining = TimeSpan.Zero;
        IntervalProgress = 0;
    }

    private void StartInterval(FocusSessionState state)
    {
        var durationMinutes = state switch
        {
            FocusSessionState.Working => _config.WorkMinutes,
            FocusSessionState.ShortBreak => _config.ShortBreakMinutes,
            FocusSessionState.LongBreak => _config.LongBreakMinutes,
            _ => 0
        };

        if (durationMinutes <= 0)
            return;

        _intervalDuration = TimeSpan.FromMinutes(durationMinutes);
        _intervalStartTimeUtc = DateTime.UtcNow;
        IntervalRemaining = _intervalDuration;
        IntervalProgress = 0;
        _isRunning = true;

        StopTickTimer();
        _tickTimer = new System.Timers.Timer(1000);
        _tickTimer.AutoReset = true;
        _tickTimer.Elapsed += OnTick;
        _tickTimer.Start();

        _logger.LogDebug("Pomodoro interval started: {State}, {Duration}min", state, durationMinutes);
        IntervalStarted?.Invoke(this, state);
    }

    private void OnTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        var elapsed = DateTime.UtcNow - _intervalStartTimeUtc;
        IntervalRemaining = _intervalDuration - elapsed;

        if (IntervalRemaining <= TimeSpan.Zero)
        {
            IntervalRemaining = TimeSpan.Zero;
            IntervalProgress = 1.0;
            TimerTick?.Invoke(this, EventArgs.Empty);

            StopTickTimer();

            var completedState = _sessionManager.CurrentState;
            _logger.LogDebug("Pomodoro interval completed: {State}", completedState);
            IntervalCompleted?.Invoke(this, completedState);

            // Advance to next interval via the session manager
            _sessionManager.AdvancePomodoroInterval();
            return;
        }

        IntervalProgress = elapsed.TotalSeconds / _intervalDuration.TotalSeconds;
        TimerTick?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionStateChanged(object? sender, FocusSessionState state)
    {
        if (state == FocusSessionState.Idle || state == FocusSessionState.Ended)
        {
            Stop();
        }
    }

    private void OnPomodoroIntervalChanged(object? sender, FocusSessionState newState)
    {
        // The session manager has advanced the interval; start tracking the new one
        if (newState != FocusSessionState.Idle && newState != FocusSessionState.Ended)
        {
            StartInterval(newState);
        }
    }

    private async Task LoadConfigurationAsync()
    {
        _config = new PomodoroConfiguration();

        var workMin = await _settingsRepository.GetAsync(SettingsKeys.PomodoroWorkMinutes);
        if (workMin is not null && int.TryParse(workMin, out var w) && w > 0)
            _config.WorkMinutes = w;

        var shortBreak = await _settingsRepository.GetAsync(SettingsKeys.PomodoroShortBreakMinutes);
        if (shortBreak is not null && int.TryParse(shortBreak, out var sb) && sb > 0)
            _config.ShortBreakMinutes = sb;

        var longBreak = await _settingsRepository.GetAsync(SettingsKeys.PomodoroLongBreakMinutes);
        if (longBreak is not null && int.TryParse(longBreak, out var lb) && lb > 0)
            _config.LongBreakMinutes = lb;

        var interval = await _settingsRepository.GetAsync(SettingsKeys.PomodoroLongBreakInterval);
        if (interval is not null && int.TryParse(interval, out var i) && i > 0)
            _config.LongBreakInterval = i;

        var autoStart = await _settingsRepository.GetAsync(SettingsKeys.PomodoroAutoStart);
        if (autoStart is not null && bool.TryParse(autoStart, out var auto))
            _config.AutoStartNext = auto;
    }

    private void StopTickTimer()
    {
        if (_tickTimer is not null)
        {
            _tickTimer.Stop();
            _tickTimer.Elapsed -= OnTick;
            _tickTimer.Dispose();
            _tickTimer = null;
        }
    }

    public void Dispose()
    {
        _sessionManager.StateChanged -= OnSessionStateChanged;
        _sessionManager.PomodoroIntervalChanged -= OnPomodoroIntervalChanged;
        StopTickTimer();
    }
}
