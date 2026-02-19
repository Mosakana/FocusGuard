namespace FocusGuard.Core.Data.Entities;

public class FocusSessionEntity
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int PlannedDurationMinutes { get; set; }
    public int ActualDurationMinutes { get; set; }
    public int PomodoroCompletedCount { get; set; }
    public bool WasUnlockedEarly { get; set; }
    public string State { get; set; } = "Ended"; // Idle, Working, ShortBreak, LongBreak, Ended
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
