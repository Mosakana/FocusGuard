namespace FocusGuard.Core.Hardening;

public interface IWatchdogLauncher
{
    void Launch();
    void SignalStop();
    bool IsWatchdogRunning { get; }
}
