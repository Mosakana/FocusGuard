namespace FocusGuard.App.Models;

public class ProfileSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#4A90D9";
    public int WebsiteCount { get; set; }
    public int AppCount { get; set; }
    public bool IsPreset { get; set; }
}
