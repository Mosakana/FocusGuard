using System.Text;
using FocusGuard.Core.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Statistics;

public class CsvExporter
{
    private readonly IFocusSessionRepository _sessionRepository;
    private readonly IStatisticsService _statisticsService;
    private readonly ILogger<CsvExporter> _logger;

    public CsvExporter(
        IFocusSessionRepository sessionRepository,
        IStatisticsService statisticsService,
        ILogger<CsvExporter> logger)
    {
        _sessionRepository = sessionRepository;
        _statisticsService = statisticsService;
        _logger = logger;
    }

    public async Task ExportSessionsAsync(string filePath, DateTime start, DateTime end)
    {
        var sessions = await _sessionRepository.GetByDateRangeAsync(start, end);
        var sb = new StringBuilder();

        sb.AppendLine("SessionId,ProfileId,StartTime,EndTime,PlannedDurationMinutes,ActualDurationMinutes,PomodoroCompletedCount,WasUnlockedEarly");

        foreach (var s in sessions)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(s.Id.ToString()),
                EscapeCsv(s.ProfileId.ToString()),
                EscapeCsv(s.StartTime.ToString("yyyy-MM-dd HH:mm:ss")),
                EscapeCsv(s.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""),
                s.PlannedDurationMinutes,
                s.ActualDurationMinutes,
                s.PomodoroCompletedCount,
                s.WasUnlockedEarly ? "true" : "false"));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        _logger.LogInformation("Exported {Count} sessions to {Path}", sessions.Count, filePath);
    }

    public async Task ExportDailySummaryAsync(string filePath, DateTime start, DateTime end)
    {
        var daily = await _statisticsService.GetDailyFocusAsync(start, end);
        var sb = new StringBuilder();

        sb.AppendLine("Date,TotalFocusMinutes,SessionCount,PomodoroCount,BlockedAttemptCount");

        foreach (var d in daily)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(d.Date.ToString("yyyy-MM-dd")),
                d.TotalFocusMinutes,
                d.SessionCount,
                d.PomodoroCount,
                d.BlockedAttemptCount));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        _logger.LogInformation("Exported daily summary to {Path}", filePath);
    }

    public static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        // Prefix injection characters with single quote
        if (value.Length > 0 && "=+-@".Contains(value[0]))
            value = "'" + value;

        // Escape if contains comma, quote, or newline
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            value = "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
