namespace FocusGuard.Core.Data.Entities;

public class BlockedAttemptEntity
{
    public Guid Id { get; set; }
    public Guid? SessionId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty; // "Application" or "Website"
    public string Target { get; set; } = string.Empty;
}
