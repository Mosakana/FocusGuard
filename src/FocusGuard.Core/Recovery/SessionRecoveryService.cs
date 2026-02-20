using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Recovery;

public class SessionRecoveryService : ISessionRecoveryService
{
    private readonly IFocusSessionRepository _sessionRepository;
    private readonly IFocusSessionManager _sessionManager;
    private readonly ILogger<SessionRecoveryService> _logger;

    public SessionRecoveryService(
        IFocusSessionRepository sessionRepository,
        IFocusSessionManager sessionManager,
        ILogger<SessionRecoveryService> logger)
    {
        _sessionRepository = sessionRepository;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<bool> TryRecoverSessionAsync()
    {
        var orphaned = await _sessionRepository.GetOrphanedSessionsAsync();
        if (orphaned.Count == 0)
        {
            _logger.LogInformation("No active sessions to recover");
            return false;
        }

        // Take the most recent orphaned session
        var session = orphaned.OrderByDescending(s => s.StartTime).First();
        var elapsed = DateTime.UtcNow - session.StartTime;
        var planned = TimeSpan.FromMinutes(session.PlannedDurationMinutes);

        if (elapsed >= planned)
        {
            // Session has expired — mark as ended
            _logger.LogInformation("Orphaned session {Id} has expired (elapsed={Elapsed}, planned={Planned}). Marking as ended.",
                session.Id, elapsed, planned);
            session.State = FocusSessionState.Ended.ToString();
            session.EndTime = DateTime.UtcNow;
            session.WasUnlockedEarly = false;
            session.ActualDurationMinutes = session.PlannedDurationMinutes;
            await _sessionRepository.UpdateAsync(session);
            return false;
        }

        // Session still has time — resume it
        var remainingMinutes = (int)Math.Ceiling((planned - elapsed).TotalMinutes);
        _logger.LogInformation("Recovering session {Id} with {Remaining} minutes remaining",
            session.Id, remainingMinutes);

        await _sessionManager.ResumeSessionAsync(session.Id, remainingMinutes);
        return true;
    }
}
