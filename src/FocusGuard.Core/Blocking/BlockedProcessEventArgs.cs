namespace FocusGuard.Core.Blocking;

public class BlockedProcessEventArgs : EventArgs
{
    public string ProcessName { get; }
    public DateTime Timestamp { get; }

    public BlockedProcessEventArgs(string processName)
    {
        ProcessName = processName;
        Timestamp = DateTime.UtcNow;
    }
}
