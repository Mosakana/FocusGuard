namespace FocusGuard.Core.Hardening;

public class HeartbeatData
{
    public int ProcessId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool HasActiveSession { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public Guid? ActiveProfileId { get; set; }
    public string MainAppPath { get; set; } = string.Empty;
}
