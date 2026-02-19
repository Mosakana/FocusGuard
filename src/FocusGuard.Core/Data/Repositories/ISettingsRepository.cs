namespace FocusGuard.Core.Data.Repositories;

public interface ISettingsRepository
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task<bool> ExistsAsync(string key);
}
