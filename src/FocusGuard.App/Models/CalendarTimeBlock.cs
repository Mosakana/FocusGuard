namespace FocusGuard.App.Models;

public class CalendarTimeBlock
{
    public Guid ScheduledSessionId { get; init; }
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public string ProfileColor { get; init; } = "#4A90D9";
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public bool IsRecurring { get; init; }
    public bool PomodoroEnabled { get; init; }
    public string TimeRangeDisplay => $"{StartTime.ToLocalTime():HH:mm} – {EndTime.ToLocalTime():HH:mm}";
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;
}
