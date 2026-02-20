namespace FocusGuard.Core.Recovery;

public interface ISessionRecoveryService
{
    Task<bool> TryRecoverSessionAsync();
}
