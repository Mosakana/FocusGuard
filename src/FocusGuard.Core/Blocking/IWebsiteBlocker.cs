namespace FocusGuard.Core.Blocking;

public interface IWebsiteBlocker
{
    Task ApplyBlocklistAsync(IEnumerable<string> domains);
    Task RemoveBlocklistAsync();
    IReadOnlyList<string> GetCurrentlyBlocked();
    bool IsActive { get; }
}
