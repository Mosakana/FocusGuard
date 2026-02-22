using FocusGuard.Core.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Statistics;

public class StatisticsService : IStatisticsService
{
    private readonly IFocusSessionRepository _sessionRepository;
    private readonly IBlockedAttemptRepository _attemptRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly ILogger<StatisticsService> _logger;

    public StatisticsService(
        IFocusSessionRepository sessionRepository,
        IBlockedAttemptRepository attemptRepository,
        IProfileRepository profileRepository,
        ILogger<StatisticsService> logger)
    {
        _sessionRepository = sessionRepository;
        _attemptRepository = attemptRepository;
        _profileRepository = profileRepository;
        _logger = logger;
    }

    public async Task<PeriodStatistics> GetStatisticsAsync(DateTime start, DateTime end)
    {
        var daily = await GetDailyFocusAsync(start, end);
        var profileBreakdown = await GetProfileBreakdownAsync(start, end);
        var streak = await GetStreakInfoAsync();

        var totalMinutes = daily.Sum(d => d.TotalFocusMinutes);
        var totalSessions = daily.Sum(d => d.SessionCount);
        var totalPomodoro = daily.Sum(d => d.PomodoroCount);
        var totalBlocked = daily.Sum(d => d.BlockedAttemptCount);

        return new PeriodStatistics(
            start, end, totalMinutes, totalSessions, totalPomodoro, totalBlocked,
            daily, profileBreakdown, streak);
    }

    public async Task<StreakInfo> GetStreakInfoAsync()
    {
        // Look back up to 365 days for streak calculation
        var end = DateTime.UtcNow.Date.AddDays(1);
        var start = end.AddDays(-365);
        var sessions = await _sessionRepository.GetByDateRangeAsync(start, end);

        var focusDays = sessions
            .GroupBy(s => s.StartTime.Date)
            .Where(g => g.Sum(s => s.ActualDurationMinutes) >= 1)
            .Select(g => g.Key)
            .OrderByDescending(d => d)
            .ToHashSet();

        if (focusDays.Count == 0)
            return new StreakInfo(0, 0, null);

        // Calculate current streak (consecutive days ending today or yesterday)
        var today = DateTime.UtcNow.Date;
        var currentStreak = 0;
        DateTime? streakStart = null;
        var checkDate = focusDays.Contains(today) ? today : today.AddDays(-1);

        if (focusDays.Contains(checkDate))
        {
            while (focusDays.Contains(checkDate))
            {
                currentStreak++;
                streakStart = checkDate;
                checkDate = checkDate.AddDays(-1);
            }
        }

        // Calculate longest streak
        var sortedDays = focusDays.OrderBy(d => d).ToList();
        var longestStreak = 0;
        var currentRun = 1;

        for (int i = 1; i < sortedDays.Count; i++)
        {
            if ((sortedDays[i] - sortedDays[i - 1]).Days == 1)
            {
                currentRun++;
            }
            else
            {
                longestStreak = Math.Max(longestStreak, currentRun);
                currentRun = 1;
            }
        }
        longestStreak = Math.Max(longestStreak, currentRun);

        return new StreakInfo(currentStreak, longestStreak, streakStart);
    }

    public async Task<List<DailyFocusSummary>> GetDailyFocusAsync(DateTime start, DateTime end)
    {
        var sessions = await _sessionRepository.GetByDateRangeAsync(start, end);
        var attempts = await _attemptRepository.GetByDateRangeAsync(start, end);

        var sessionsByDay = sessions
            .GroupBy(s => s.StartTime.Date)
            .ToDictionary(
                g => g.Key,
                g => (
                    Minutes: g.Sum(s => (double)s.ActualDurationMinutes),
                    Count: g.Count(),
                    Pomodoro: g.Sum(s => s.PomodoroCompletedCount)
                ));

        var attemptsByDay = attempts
            .GroupBy(a => a.Timestamp.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = new List<DailyFocusSummary>();
        for (var date = start.Date; date < end.Date; date = date.AddDays(1))
        {
            sessionsByDay.TryGetValue(date, out var sessionData);
            attemptsByDay.TryGetValue(date, out var attemptCount);

            result.Add(new DailyFocusSummary(
                date,
                sessionData.Minutes,
                sessionData.Count,
                sessionData.Pomodoro,
                attemptCount));
        }

        return result;
    }

    public async Task<List<ProfileFocusSummary>> GetProfileBreakdownAsync(DateTime start, DateTime end)
    {
        var sessions = await _sessionRepository.GetByDateRangeAsync(start, end);
        var profiles = await _profileRepository.GetAllAsync();
        var profileLookup = profiles.ToDictionary(p => p.Id);

        return sessions
            .GroupBy(s => s.ProfileId)
            .Select(g =>
            {
                profileLookup.TryGetValue(g.Key, out var profile);
                return new ProfileFocusSummary(
                    g.Key,
                    profile?.Name ?? "Unknown",
                    profile?.Color ?? "#4A90D9",
                    g.Sum(s => (double)s.ActualDurationMinutes),
                    g.Count());
            })
            .OrderByDescending(p => p.TotalFocusMinutes)
            .ToList();
    }

    public async Task<List<DailyFocusSummary>> GetHeatmapDataAsync(int days = 91)
    {
        var end = DateTime.UtcNow.Date.AddDays(1);
        var start = end.AddDays(-days);
        return await GetDailyFocusAsync(start, end);
    }
}
