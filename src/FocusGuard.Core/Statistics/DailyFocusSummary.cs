namespace FocusGuard.Core.Statistics;

public record DailyFocusSummary(
    DateTime Date,
    double TotalFocusMinutes,
    int SessionCount,
    int PomodoroCount,
    int BlockedAttemptCount);
