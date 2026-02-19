namespace FocusGuard.Core.Blocking;

public interface IApplicationBlocker
{
    void StartBlocking(IEnumerable<string> processNames);
    void StopBlocking();
    bool IsActive { get; }
    event EventHandler<BlockedProcessEventArgs>? ProcessBlocked;
}
