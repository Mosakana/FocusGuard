namespace FocusGuard.Core.Statistics;

public interface IStatisticsService
{
    Task<PeriodStatistics> GetStatisticsAsync(DateTime start, DateTime end);
    Task<StreakInfo> GetStreakInfoAsync();
    Task<List<DailyFocusSummary>> GetDailyFocusAsync(DateTime start, DateTime end);
    Task<List<ProfileFocusSummary>> GetProfileBreakdownAsync(DateTime start, DateTime end);
    Task<List<DailyFocusSummary>> GetHeatmapDataAsync(int days = 91);
}
