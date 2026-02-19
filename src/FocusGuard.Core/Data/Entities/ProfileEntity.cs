namespace FocusGuard.Core.Data.Entities;

public class ProfileEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#4A90D9";
    public string BlockedWebsites { get; set; } = "[]";
    public string BlockedApplications { get; set; } = "[]";
    public bool IsPreset { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
