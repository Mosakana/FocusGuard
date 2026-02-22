using FocusGuard.Core.Scheduling;

namespace FocusGuard.App.Models;

public class ScheduleSessionDialogResult
{
    public Guid ProfileId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public bool PomodoroEnabled { get; init; }
    public bool IsRecurring { get; init; }
    public RecurrenceRule? RecurrenceRule { get; init; }
}
