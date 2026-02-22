namespace FocusGuard.Core.Scheduling;

public class ScheduledOccurrence
{
    public Guid ScheduledSessionId { get; init; }
    public Guid ProfileId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public bool PomodoroEnabled { get; init; }
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;
}
