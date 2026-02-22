namespace FocusGuard.Core.Statistics;

public enum GoalPeriod
{
    Daily,
    Weekly
}

public class FocusGoal
{
    public GoalPeriod Period { get; set; }
    public int TargetMinutes { get; set; }
    public Guid? ProfileId { get; set; } // null = global goal
}
