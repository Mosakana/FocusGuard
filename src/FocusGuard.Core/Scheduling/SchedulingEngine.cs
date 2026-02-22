using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Scheduling;

public class SchedulingEngine : ISchedulingEngine, IDisposable
{
    private readonly IScheduledSessionRepository _repository;
    private readonly OccurrenceExpander _expander;
    private readonly IFocusSessionManager _sessionManager;
    private readonly ILogger<SchedulingEngine> _logger;

    private System.Timers.Timer? _pollTimer;
    private List<ScheduledOccurrence> _upcomingOccurrences = [];
    private readonly HashSet<string> _firedStartKeys = [];
    private readonly HashSet<string> _firedEndKeys = [];

    public event EventHandler<ScheduledOccurrence>? SessionStarting;
    public event EventHandler<ScheduledOccurrence>? SessionEnding;

    public SchedulingEngine(
        IScheduledSessionRepository repository,
        OccurrenceExpander expander,
        IFocusSessionManager sessionManager,
        ILogger<SchedulingEngine> logger)
    {
        _repository = repository;
        _expander = expander;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        await RefreshAsync();

        _pollTimer = new System.Timers.Timer(15_000); // 15 seconds
        _pollTimer.AutoReset = true;
        _pollTimer.Elapsed += async (_, _) => await PollAsync();
        _pollTimer.Start();

        _logger.LogInformation("Scheduling engine started");
    }

    public void Stop()
    {
        if (_pollTimer is not null)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _pollTimer = null;
        }
        _logger.LogInformation("Scheduling engine stopped");
    }

    public async Task RefreshAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var rangeEnd = now.AddHours(24);
            var sessions = await _repository.GetEnabledAsync();

            var occurrences = new List<ScheduledOccurrence>();
            foreach (var session in sessions)
            {
                occurrences.AddRange(_expander.Expand(session, now, rangeEnd));
            }

            _upcomingOccurrences = occurrences.OrderBy(o => o.StartTime).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh scheduled sessions");
        }
    }

    public ScheduledOccurrence? GetNextOccurrence()
    {
        var now = DateTime.UtcNow;
        return _upcomingOccurrences.FirstOrDefault(o => o.StartTime > now);
    }

    private async Task PollAsync()
    {
        try
        {
            var now = DateTime.UtcNow;

            // Refresh every poll to pick up new/modified schedules
            await RefreshAsync();

            foreach (var occ in _upcomingOccurrences)
            {
                var startKey = $"{occ.ScheduledSessionId}:{occ.StartTime:O}:start";
                var endKey = $"{occ.ScheduledSessionId}:{occ.StartTime:O}:end";

                // Fire SessionStarting if we've passed the start time and haven't fired yet
                if (now >= occ.StartTime && now < occ.EndTime && !_firedStartKeys.Contains(startKey))
                {
                    // Don't auto-start if there's already an active session
                    if (_sessionManager.CurrentState == FocusSessionState.Idle)
                    {
                        _firedStartKeys.Add(startKey);
                        _logger.LogInformation("Scheduled session starting: {Id} at {Time}", occ.ScheduledSessionId, occ.StartTime);
                        SessionStarting?.Invoke(this, occ);
                    }
                }

                // Fire SessionEnding if we've passed the end time and haven't fired yet
                if (now >= occ.EndTime && !_firedEndKeys.Contains(endKey))
                {
                    _firedEndKeys.Add(endKey);
                    _logger.LogInformation("Scheduled session ending: {Id} at {Time}", occ.ScheduledSessionId, occ.EndTime);
                    SessionEnding?.Invoke(this, occ);
                }
            }

            // Clean up old keys (older than 25 hours)
            CleanupFiredKeys(now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduling poll");
        }
    }

    private void CleanupFiredKeys(DateTime now)
    {
        var cutoff = now.AddHours(-25);
        _firedStartKeys.RemoveWhere(k =>
        {
            var parts = k.Split(':');
            return parts.Length >= 2 && DateTime.TryParse(parts[1], out var dt) && dt < cutoff;
        });
        _firedEndKeys.RemoveWhere(k =>
        {
            var parts = k.Split(':');
            return parts.Length >= 2 && DateTime.TryParse(parts[1], out var dt) && dt < cutoff;
        });
    }

    public void Dispose()
    {
        Stop();
    }
}
