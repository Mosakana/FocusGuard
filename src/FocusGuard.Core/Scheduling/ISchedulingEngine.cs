namespace FocusGuard.Core.Scheduling;

public interface ISchedulingEngine
{
    Task StartAsync();
    void Stop();
    Task RefreshAsync();
    ScheduledOccurrence? GetNextOccurrence();

    event EventHandler<ScheduledOccurrence>? SessionStarting;
    event EventHandler<ScheduledOccurrence>? SessionEnding;
}
