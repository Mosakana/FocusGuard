namespace FocusGuard.Core.Data.Entities;

public class ScheduledSessionEntity
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurrenceRule { get; set; } // JSON — null if not recurring
    public bool PomodoroEnabled { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
