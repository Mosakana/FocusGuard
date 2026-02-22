namespace FocusGuard.Core.Statistics;

public interface IGoalService
{
    Task<FocusGoal?> GetGoalAsync(GoalPeriod period, Guid? profileId = null);
    Task SetGoalAsync(FocusGoal goal);
    Task RemoveGoalAsync(GoalPeriod period, Guid? profileId = null);
    Task<List<GoalProgress>> GetAllProgressAsync();
}
