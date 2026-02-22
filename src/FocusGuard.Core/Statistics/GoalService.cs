using System.Text.Json;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Security;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Statistics;

public class GoalService : IGoalService
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly IFocusSessionRepository _sessionRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly ILogger<GoalService> _logger;

    public GoalService(
        ISettingsRepository settingsRepository,
        IFocusSessionRepository sessionRepository,
        IProfileRepository profileRepository,
        ILogger<GoalService> logger)
    {
        _settingsRepository = settingsRepository;
        _sessionRepository = sessionRepository;
        _profileRepository = profileRepository;
        _logger = logger;
    }

    public async Task<FocusGoal?> GetGoalAsync(GoalPeriod period, Guid? profileId = null)
    {
        var key = BuildKey(period, profileId);
        var json = await _settingsRepository.GetAsync(key);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<FocusGoal>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetGoalAsync(FocusGoal goal)
    {
        var key = BuildKey(goal.Period, goal.ProfileId);
        var json = JsonSerializer.Serialize(goal);
        await _settingsRepository.SetAsync(key, json);

        // Update goal index
        await AddToIndexAsync(key);

        _logger.LogInformation("Set goal: {Key} = {TargetMinutes}min", key, goal.TargetMinutes);
    }

    public async Task RemoveGoalAsync(GoalPeriod period, Guid? profileId = null)
    {
        var key = BuildKey(period, profileId);
        await _settingsRepository.SetAsync(key, "");

        // Remove from goal index
        await RemoveFromIndexAsync(key);

        _logger.LogInformation("Removed goal: {Key}", key);
    }

    public async Task<List<GoalProgress>> GetAllProgressAsync()
    {
        var keys = await GetIndexAsync();
        var result = new List<GoalProgress>();

        foreach (var key in keys)
        {
            var json = await _settingsRepository.GetAsync(key);
            if (string.IsNullOrEmpty(json)) continue;

            FocusGoal? goal;
            try
            {
                goal = JsonSerializer.Deserialize<FocusGoal>(json);
            }
            catch
            {
                continue;
            }

            if (goal is null) continue;

            var currentMinutes = await CalculateCurrentMinutesAsync(goal);
            var completionPercent = goal.TargetMinutes > 0
                ? Math.Min(100, currentMinutes / goal.TargetMinutes * 100)
                : 0;
            var isCompleted = currentMinutes >= goal.TargetMinutes;
            var remaining = Math.Max(0, goal.TargetMinutes - currentMinutes);
            var label = BuildLabel(goal);

            result.Add(new GoalProgress(goal, currentMinutes, completionPercent, isCompleted, remaining, label));
        }

        return result;
    }

    private async Task<double> CalculateCurrentMinutesAsync(FocusGoal goal)
    {
        var (start, end) = GetPeriodRange(goal.Period);
        var sessions = await _sessionRepository.GetByDateRangeAsync(start, end);

        if (goal.ProfileId.HasValue)
            sessions = sessions.Where(s => s.ProfileId == goal.ProfileId.Value).ToList();

        return sessions.Sum(s => (double)s.ActualDurationMinutes);
    }

    private static (DateTime start, DateTime end) GetPeriodRange(GoalPeriod period)
    {
        var today = DateTime.UtcNow.Date;

        if (period == GoalPeriod.Daily)
            return (today, today.AddDays(1));

        // Weekly: start from Monday
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var weekStart = today.AddDays(-daysSinceMonday);
        return (weekStart, weekStart.AddDays(7));
    }

    private static string BuildKey(GoalPeriod period, Guid? profileId)
    {
        var prefix = period == GoalPeriod.Daily
            ? SettingsKeys.GoalDailyPrefix
            : SettingsKeys.GoalWeeklyPrefix;

        return profileId.HasValue ? $"{prefix}.{profileId.Value}" : prefix;
    }

    private string BuildLabel(FocusGoal goal)
    {
        var periodLabel = goal.Period == GoalPeriod.Daily ? "Daily" : "Weekly";
        var hours = goal.TargetMinutes / 60;
        var minutes = goal.TargetMinutes % 60;
        var target = hours > 0
            ? minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h"
            : $"{minutes}m";

        return goal.ProfileId.HasValue
            ? $"{periodLabel} Goal: {target}"
            : $"{periodLabel} Goal: {target}";
    }

    private async Task<List<string>> GetIndexAsync()
    {
        var json = await _settingsRepository.GetAsync(SettingsKeys.GoalIndex);
        if (string.IsNullOrEmpty(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task AddToIndexAsync(string key)
    {
        var keys = await GetIndexAsync();
        if (!keys.Contains(key))
        {
            keys.Add(key);
            await _settingsRepository.SetAsync(SettingsKeys.GoalIndex, JsonSerializer.Serialize(keys));
        }
    }

    private async Task RemoveFromIndexAsync(string key)
    {
        var keys = await GetIndexAsync();
        if (keys.Remove(key))
        {
            await _settingsRepository.SetAsync(SettingsKeys.GoalIndex, JsonSerializer.Serialize(keys));
        }
    }
}
