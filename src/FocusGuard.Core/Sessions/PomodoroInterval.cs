namespace FocusGuard.Core.Sessions;

public class PomodoroInterval
{
    public FocusSessionState Type { get; init; }
    public int DurationMinutes { get; init; }
    public int SequenceNumber { get; init; }
}
