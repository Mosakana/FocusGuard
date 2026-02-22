namespace FocusGuard.Core.Statistics;

public record PeriodStatistics(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    double TotalFocusMinutes,
    int TotalSessions,
    int TotalPomodoroCount,
    int TotalBlockedAttempts,
    List<DailyFocusSummary> DailyBreakdown,
    List<ProfileFocusSummary> ProfileBreakdown,
    StreakInfo Streak);
