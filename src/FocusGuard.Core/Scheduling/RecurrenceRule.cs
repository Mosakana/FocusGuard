namespace FocusGuard.Core.Scheduling;

public enum RecurrenceType
{
    Daily,
    Weekdays,
    Weekly,
    Custom
}

public class RecurrenceRule
{
    public RecurrenceType Type { get; set; }
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
    public int IntervalWeeks { get; set; } = 1;
    public DateTime? EndDate { get; set; }
}
