namespace FocusGuard.Core.Recovery;

public interface ICrashRecoveryService
{
    Task CleanupHostsFileAsync();
    Task<int> CleanupOrphanedSessionsAsync();
    Task RecoverAsync();
}
