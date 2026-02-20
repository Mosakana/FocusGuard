namespace FocusGuard.Core.Sessions;

public record FocusSessionInfo
{
    public Guid SessionId { get; init; }
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public FocusSessionState State { get; init; }
    public DateTime StartTime { get; init; }
    public TimeSpan Elapsed { get; init; }
    public TimeSpan TotalPlanned { get; init; }
    public TimeSpan CurrentIntervalRemaining { get; init; }
    public int PomodoroCompletedCount { get; init; }
    public bool IsPomodoroEnabled { get; init; }
    public string UnlockPassword { get; init; } = string.Empty;
}
