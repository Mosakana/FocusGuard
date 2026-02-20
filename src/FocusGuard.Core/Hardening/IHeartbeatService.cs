namespace FocusGuard.Core.Hardening;

public interface IHeartbeatService
{
    void Start(Guid? sessionId, Guid? profileId);
    void Stop();
    void UpdateSession(Guid? sessionId, Guid? profileId);
}
