using FocusGuard.Core.Blocking;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Recovery;

public class CrashRecoveryService : ICrashRecoveryService
{
    private const string HostsFilePath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string MarkerStart = "# >>> FocusGuard START";

    private readonly IWebsiteBlocker _websiteBlocker;
    private readonly IFocusSessionRepository _sessionRepository;
    private readonly ILogger<CrashRecoveryService> _logger;

    public CrashRecoveryService(
        IWebsiteBlocker websiteBlocker,
        IFocusSessionRepository sessionRepository,
        ILogger<CrashRecoveryService> logger)
    {
        _websiteBlocker = websiteBlocker;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task CleanupHostsFileAsync()
    {
        try
        {
            if (!File.Exists(HostsFilePath))
                return;

            var content = await File.ReadAllTextAsync(HostsFilePath);
            if (content.Contains(MarkerStart))
            {
                _logger.LogWarning("Found stale FocusGuard entries in hosts file — cleaning up");
                await _websiteBlocker.RemoveBlocklistAsync();
                _logger.LogInformation("Hosts file cleanup completed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup hosts file during crash recovery");
        }
    }

    public async Task<int> CleanupOrphanedSessionsAsync()
    {
        try
        {
            var orphaned = await _sessionRepository.GetOrphanedSessionsAsync();
            if (orphaned.Count == 0)
                return 0;

            foreach (var session in orphaned)
            {
                _logger.LogWarning("Cleaning up orphaned session {Id} in state {State}", session.Id, session.State);
                session.State = FocusSessionState.Ended.ToString();
                session.EndTime = DateTime.UtcNow;
                session.WasUnlockedEarly = true;
                session.ActualDurationMinutes = (int)(DateTime.UtcNow - session.StartTime).TotalMinutes;
                await _sessionRepository.UpdateAsync(session);
            }

            _logger.LogInformation("Cleaned up {Count} orphaned sessions", orphaned.Count);
            return orphaned.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup orphaned sessions during crash recovery");
            return 0;
        }
    }

    public async Task RecoverAsync()
    {
        _logger.LogInformation("Running crash recovery checks...");
        await CleanupHostsFileAsync();
        var orphanCount = await CleanupOrphanedSessionsAsync();
        _logger.LogInformation("Crash recovery complete. Orphaned sessions cleaned: {Count}", orphanCount);
    }
}
