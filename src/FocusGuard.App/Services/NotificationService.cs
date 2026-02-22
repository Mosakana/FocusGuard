using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Security;
using FocusGuard.Core.Sessions;
using FocusGuard.Core.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace FocusGuard.App.Services;

public class NotificationService : INotificationService
{
    private readonly IFocusSessionManager _sessionManager;
    private readonly PomodoroTimer _pomodoroTimer;
    private readonly BlockedAttemptLogger _blockedAttemptLogger;
    private readonly IGoalService _goalService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationService> _logger;

    private DateTime _lastBlockedNotification = DateTime.MinValue;
    private static readonly TimeSpan BlockedDebounceInterval = TimeSpan.FromSeconds(30);
    private bool _disposed;

    public NotificationService(
        IFocusSessionManager sessionManager,
        PomodoroTimer pomodoroTimer,
        BlockedAttemptLogger blockedAttemptLogger,
        IGoalService goalService,
        IServiceProvider serviceProvider,
        ILogger<NotificationService> logger)
    {
        _sessionManager = sessionManager;
        _pomodoroTimer = pomodoroTimer;
        _blockedAttemptLogger = blockedAttemptLogger;
        _goalService = goalService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void Initialize()
    {
        _sessionManager.StateChanged += OnSessionStateChanged;
        _sessionManager.SessionEnded += OnSessionEnded;
        _pomodoroTimer.IntervalStarted += OnIntervalStarted;
        _pomodoroTimer.IntervalCompleted += OnIntervalCompleted;
        _blockedAttemptLogger.AttemptLogged += OnBlockedAttemptLogged;

        _logger.LogInformation("Notification service initialized");
    }

    private async void OnSessionStateChanged(object? sender, FocusSessionState state)
    {
        try
        {
            if (state != FocusSessionState.Working) return;
            if (!await IsEnabledAsync(SettingsKeys.NotifySessionEnabled)) return;

            var session = _sessionManager.CurrentSession;
            if (session is null) return;

            var durationStr = session.TotalPlanned.TotalMinutes >= 60
                ? $"{session.TotalPlanned.TotalHours:F1}h"
                : $"{session.TotalPlanned.TotalMinutes:F0}min";

            ShowToast("Focus Session Started",
                $"Profile: {session.ProfileName} — {durationStr}",
                "session_started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session state change notification");
        }
    }

    private async void OnSessionEnded(object? sender, EventArgs e)
    {
        try
        {
            if (!await IsEnabledAsync(SettingsKeys.NotifySessionEnabled)) return;

            ShowToast("Focus Session Complete",
                "Great work! Your focus session has ended.",
                "session_ended");

            // Check if any goals were reached
            if (await IsEnabledAsync(SettingsKeys.NotifyGoalEnabled))
            {
                var progress = await _goalService.GetAllProgressAsync();
                foreach (var goal in progress)
                {
                    if (goal.IsCompleted)
                    {
                        ShowToast("Goal Reached!",
                            $"{goal.DisplayLabel} — target achieved!",
                            $"goal_{goal.Goal.Period}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session ended notification");
        }
    }

    private async void OnIntervalStarted(object? sender, FocusSessionState state)
    {
        try
        {
            if (!await IsEnabledAsync(SettingsKeys.NotifyPomodoroEnabled)) return;

            var (title, body) = state switch
            {
                FocusSessionState.Working => ("Back to Work!", "Focus interval starting now."),
                FocusSessionState.ShortBreak => ("Short Break", "Take a quick break."),
                FocusSessionState.LongBreak => ("Long Break", "You've earned a longer break!"),
                _ => (string.Empty, string.Empty)
            };

            if (!string.IsNullOrEmpty(title))
            {
                ShowToast(title, body, $"interval_{state}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling interval started notification");
        }
    }

    private async void OnIntervalCompleted(object? sender, FocusSessionState completedState)
    {
        try
        {
            if (!await IsEnabledAsync(SettingsKeys.NotifyPomodoroEnabled)) return;

            if (completedState == FocusSessionState.Working)
            {
                var session = _sessionManager.CurrentSession;
                var count = session?.PomodoroCompletedCount ?? 0;
                ShowToast("Pomodoro Complete!",
                    $"You've completed {count} pomodoro{(count != 1 ? "s" : "")}.",
                    "pomodoro_complete");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling interval completed notification");
        }
    }

    private async void OnBlockedAttemptLogged(object? sender, BlockedAttemptEntity attempt)
    {
        try
        {
            // Debounce: only show one notification per 30 seconds
            var now = DateTime.UtcNow;
            if (now - _lastBlockedNotification < BlockedDebounceInterval)
                return;
            _lastBlockedNotification = now;

            if (!await IsEnabledAsync(SettingsKeys.NotifyBlockedEnabled)) return;

            ShowToast("Distraction Blocked",
                $"{attempt.Type}: {attempt.Target}",
                "blocked_attempt");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling blocked attempt notification");
        }
    }

    private async Task<bool> IsEnabledAsync(string settingsKey)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var value = await settings.GetAsync(settingsKey);

            // Default to enabled if setting not set
            if (value is null) return true;
            return bool.TryParse(value, out var enabled) && enabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading notification setting {Key}", settingsKey);
            return true; // Default to enabled on error
        }
    }

    private void ShowToast(string title, string body, string tag)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show(toast =>
                {
                    toast.Tag = tag;
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show toast notification: {Title}", title);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sessionManager.StateChanged -= OnSessionStateChanged;
        _sessionManager.SessionEnded -= OnSessionEnded;
        _pomodoroTimer.IntervalStarted -= OnIntervalStarted;
        _pomodoroTimer.IntervalCompleted -= OnIntervalCompleted;
        _blockedAttemptLogger.AttemptLogged -= OnBlockedAttemptLogged;

        try
        {
            ToastNotificationManagerCompat.History.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear toast notification history");
        }
    }
}
