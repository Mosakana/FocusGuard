namespace FocusGuard.Core.Statistics;

public record GoalProgress(
    FocusGoal Goal,
    double CurrentMinutes,
    double CompletionPercent,
    bool IsCompleted,
    double RemainingMinutes,
    string DisplayLabel);
