namespace FocusGuard.Core.Statistics;

public record StreakInfo(
    int CurrentStreak,
    int LongestStreak,
    DateTime? StreakStartDate);
