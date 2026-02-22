using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.App.Models;
using FocusGuard.App.Services;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Scheduling;
using FocusGuard.Core.Security;
using FocusGuard.Core.Sessions;
using FocusGuard.Core.Statistics;
using Microsoft.Extensions.Logging;

namespace FocusGuard.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly BlockingOrchestrator _orchestrator;
    private readonly IDialogService _dialogService;
    private readonly PomodoroTimer _pomodoroTimer;
    private readonly SoundAlertService _soundAlertService;
    private readonly IScheduledSessionRepository _scheduledSessionRepository;
    private readonly OccurrenceExpander _occurrenceExpander;
    private readonly IStatisticsService _statisticsService;
    private readonly IGoalService _goalService;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty]
    private string _statusText = "Idle — No active focus session";

    [ObservableProperty]
    private bool _isBlocking;

    [ObservableProperty]
    private bool _isSessionActive;

    [ObservableProperty]
    private bool _isPomodoroSession;

    [ObservableProperty]
    private string _activeProfileName = string.Empty;

    [ObservableProperty]
    private string _sessionTimeRemaining = string.Empty;

    // Timer visualization properties
    [ObservableProperty]
    private string _timerDisplay = "00:00";

    [ObservableProperty]
    private double _timerProgress;

    [ObservableProperty]
    private string _currentIntervalLabel = string.Empty;

    [ObservableProperty]
    private int _pomodoroCompletedCount;

    [ObservableProperty]
    private int _pomodoroTotalIntervals;

    // Dashboard statistics
    [ObservableProperty]
    private double[] _weeklyChartValues = new double[7];

    [ObservableProperty]
    private int _currentStreak;

    [ObservableProperty]
    private string _weeklyFocusDisplay = string.Empty;

    public ObservableCollection<GoalProgress> GoalProgressItems { get; } = [];
    public ObservableCollection<ProfileSummary> Profiles { get; } = [];
    public ObservableCollection<CalendarTimeBlock> TodaySessions { get; } = [];

    public DashboardViewModel(
        IProfileRepository profileRepository,
        ISettingsRepository settingsRepository,
        BlockingOrchestrator orchestrator,
        IDialogService dialogService,
        PomodoroTimer pomodoroTimer,
        SoundAlertService soundAlertService,
        IScheduledSessionRepository scheduledSessionRepository,
        OccurrenceExpander occurrenceExpander,
        IStatisticsService statisticsService,
        IGoalService goalService,
        ILogger<DashboardViewModel> logger)
    {
        _profileRepository = profileRepository;
        _settingsRepository = settingsRepository;
        _orchestrator = orchestrator;
        _dialogService = dialogService;
        _pomodoroTimer = pomodoroTimer;
        _soundAlertService = soundAlertService;
        _scheduledSessionRepository = scheduledSessionRepository;
        _occurrenceExpander = occurrenceExpander;
        _statisticsService = statisticsService;
        _goalService = goalService;
        _logger = logger;

        // Subscribe to session state changes
        _orchestrator.SessionManager.StateChanged += OnSessionStateChanged;

        // Subscribe to Pomodoro timer ticks for live UI updates
        _pomodoroTimer.TimerTick += OnTimerTick;
        _pomodoroTimer.IntervalStarted += OnIntervalStarted;
        _pomodoroTimer.IntervalCompleted += OnIntervalCompleted;
    }

    public override async void OnNavigatedTo()
    {
        await LoadProfilesAsync();
        await LoadTodaySessionsAsync();
        await LoadStatsSummaryAsync();
        UpdateBlockingStatus();
    }

    [RelayCommand]
    private async Task StartSession(Guid profileId)
    {
        try
        {
            var profile = Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile is null) return;

            var result = await _dialogService.ShowStartSessionDialogAsync(
                profile.Id, profile.Name, profile.Color);

            if (result is null) return;

            // Save password difficulty preference
            await _settingsRepository.SetAsync(SettingsKeys.PasswordDifficulty,
                result.Difficulty.ToString());

            await _orchestrator.SessionManager.StartSessionAsync(
                profileId, result.DurationMinutes, result.EnablePomodoro);

            // Start the Pomodoro timer if Pomodoro mode is enabled
            if (result.EnablePomodoro)
            {
                await _pomodoroTimer.StartAsync();
                await _soundAlertService.PlayWorkStartAsync();
            }

            UpdateBlockingStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start session");
            MessageBox.Show($"Failed to start session: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task EndSessionEarly()
    {
        try
        {
            var password = _orchestrator.SessionManager.GetUnlockPassword();
            if (password is null) return;

            var result = await _dialogService.ShowUnlockDialogAsync(password);

            // If unlocked, the session manager already ended the session
            // Just update the UI status
            UpdateBlockingStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unlock session");
        }
    }

    private void OnSessionStateChanged(object? sender, FocusSessionState state)
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            UpdateBlockingStatus();

            if (state == FocusSessionState.Idle || state == FocusSessionState.Ended)
            {
                await _soundAlertService.PlaySessionEndAsync();
            }
        });
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var remaining = _pomodoroTimer.IntervalRemaining;
            TimerDisplay = remaining.TotalHours >= 1
                ? remaining.ToString(@"h\:mm\:ss")
                : remaining.ToString(@"mm\:ss");
            TimerProgress = _pomodoroTimer.IntervalProgress;

            // Also update the session-level remaining time
            UpdateSessionRemaining();
        });
    }

    private void OnIntervalStarted(object? sender, FocusSessionState state)
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            CurrentIntervalLabel = state switch
            {
                FocusSessionState.Working => "Focus Time",
                FocusSessionState.ShortBreak => "Short Break",
                FocusSessionState.LongBreak => "Long Break",
                _ => string.Empty
            };

            if (state == FocusSessionState.Working)
                await _soundAlertService.PlayWorkStartAsync();
            else if (state is FocusSessionState.ShortBreak or FocusSessionState.LongBreak)
                await _soundAlertService.PlayBreakStartAsync();
        });
    }

    private void OnIntervalCompleted(object? sender, FocusSessionState completedState)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var session = _orchestrator.SessionManager.CurrentSession;
            if (session is not null)
            {
                PomodoroCompletedCount = session.PomodoroCompletedCount;
            }
        });
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            var profiles = await _profileRepository.GetAllAsync();
            Profiles.Clear();

            foreach (var p in profiles)
            {
                var websites = JsonSerializer.Deserialize<List<string>>(p.BlockedWebsites) ?? [];
                var apps = JsonSerializer.Deserialize<List<string>>(p.BlockedApplications) ?? [];

                Profiles.Add(new ProfileSummary
                {
                    Id = p.Id,
                    Name = p.Name,
                    Color = p.Color,
                    WebsiteCount = websites.Count,
                    AppCount = apps.Count,
                    IsPreset = p.IsPreset
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load profiles");
        }
    }

    private async Task LoadStatsSummaryAsync()
    {
        try
        {
            // Weekly chart: get daily focus for this week (Mon-Sun)
            var today = DateTime.UtcNow.Date;
            var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            var weekStart = today.AddDays(-daysSinceMonday);
            var weekEnd = weekStart.AddDays(7);

            var daily = await _statisticsService.GetDailyFocusAsync(weekStart, weekEnd);
            var values = new double[7];
            foreach (var d in daily)
            {
                var idx = ((int)d.Date.DayOfWeek + 6) % 7; // Mon=0, Sun=6
                if (idx >= 0 && idx < 7)
                    values[idx] = d.TotalFocusMinutes;
            }
            WeeklyChartValues = values;

            var weeklyTotal = values.Sum();
            var hours = weeklyTotal / 60;
            WeeklyFocusDisplay = hours >= 1 ? $"{hours:F1}h this week" : $"{weeklyTotal:F0}m this week";

            // Streak
            var streak = await _statisticsService.GetStreakInfoAsync();
            CurrentStreak = streak.CurrentStreak;

            // Goals
            var progress = await _goalService.GetAllProgressAsync();
            GoalProgressItems.Clear();
            foreach (var g in progress)
                GoalProgressItems.Add(g);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load stats summary");
        }
    }

    private void UpdateBlockingStatus()
    {
        var sessionManager = _orchestrator.SessionManager;
        IsBlocking = _orchestrator.IsActive;
        IsSessionActive = sessionManager.CurrentState != FocusSessionState.Idle
                       && sessionManager.CurrentState != FocusSessionState.Ended;

        if (IsSessionActive)
        {
            var session = sessionManager.CurrentSession;
            ActiveProfileName = session?.ProfileName ?? _orchestrator.ActiveProfileName ?? "Unknown";
            IsPomodoroSession = session?.IsPomodoroEnabled ?? false;
            PomodoroCompletedCount = session?.PomodoroCompletedCount ?? 0;

            // Calculate total expected work intervals from session duration
            if (IsPomodoroSession)
            {
                var config = _pomodoroTimer.Configuration;
                var totalMinutes = session?.TotalPlanned.TotalMinutes ?? 0;
                PomodoroTotalIntervals = totalMinutes > 0
                    ? (int)Math.Ceiling(totalMinutes / (config.WorkMinutes + config.ShortBreakMinutes))
                    : 4;

                CurrentIntervalLabel = sessionManager.CurrentState switch
                {
                    FocusSessionState.Working => "Focus Time",
                    FocusSessionState.ShortBreak => "Short Break",
                    FocusSessionState.LongBreak => "Long Break",
                    _ => string.Empty
                };
            }

            UpdateSessionRemaining();
            UpdateTimerFromSession(session);
        }
        else
        {
            ActiveProfileName = string.Empty;
            SessionTimeRemaining = string.Empty;
            StatusText = "Idle — No active focus session";
            IsPomodoroSession = false;
            TimerDisplay = "00:00";
            TimerProgress = 0;
            CurrentIntervalLabel = string.Empty;
            PomodoroCompletedCount = 0;
            PomodoroTotalIntervals = 0;
        }
    }

    private void UpdateSessionRemaining()
    {
        var session = _orchestrator.SessionManager.CurrentSession;
        if (session is null) return;

        var remaining = session.CurrentIntervalRemaining;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        SessionTimeRemaining = remaining.TotalHours >= 1
            ? remaining.ToString(@"h\:mm\:ss")
            : remaining.ToString(@"mm\:ss");

        StatusText = IsPomodoroSession
            ? $"Active — \"{ActiveProfileName}\" (Pomodoro #{PomodoroCompletedCount + 1})"
            : $"Active — \"{ActiveProfileName}\" ({SessionTimeRemaining} remaining)";
    }

    private async Task LoadTodaySessionsAsync()
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);
            var sessions = await _scheduledSessionRepository.GetEnabledAsync();
            var profiles = await _profileRepository.GetAllAsync();
            var profileLookup = profiles.ToDictionary(p => p.Id);

            TodaySessions.Clear();

            foreach (var session in sessions)
            {
                var occurrences = _occurrenceExpander.Expand(session, today, tomorrow);
                foreach (var occ in occurrences)
                {
                    profileLookup.TryGetValue(occ.ProfileId, out var profile);
                    TodaySessions.Add(new CalendarTimeBlock
                    {
                        ScheduledSessionId = occ.ScheduledSessionId,
                        ProfileId = occ.ProfileId,
                        ProfileName = profile?.Name ?? "Unknown",
                        ProfileColor = profile?.Color ?? "#4A90D9",
                        StartTime = occ.StartTime,
                        EndTime = occ.EndTime,
                        IsRecurring = session.IsRecurring,
                        PomodoroEnabled = occ.PomodoroEnabled
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load today's sessions");
        }
    }

    private void UpdateTimerFromSession(FocusSessionInfo? session)
    {
        if (session is null) return;

        if (IsPomodoroSession && _pomodoroTimer.IsRunning)
        {
            // Pomodoro timer drives the display
            var remaining = _pomodoroTimer.IntervalRemaining;
            TimerDisplay = remaining.TotalHours >= 1
                ? remaining.ToString(@"h\:mm\:ss")
                : remaining.ToString(@"mm\:ss");
            TimerProgress = _pomodoroTimer.IntervalProgress;
        }
        else
        {
            // Simple session: use overall session remaining
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
    }
}
