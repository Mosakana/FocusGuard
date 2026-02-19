using FocusGuard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly IDbContextFactory<FocusGuardDbContext> _contextFactory;
    private readonly ILogger<SettingsRepository> _logger;

    public SettingsRepository(IDbContextFactory<FocusGuardDbContext> contextFactory, ILogger<SettingsRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Settings.FindAsync(key);
        return entity?.Value;
    }

    public async Task SetAsync(string key, string value)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Settings.FindAsync(key);

        if (entity is null)
        {
            entity = new SettingEntity { Key = key, Value = value };
            context.Settings.Add(entity);
        }
        else
        {
            entity.Value = value;
        }

        await context.SaveChangesAsync();
        _logger.LogDebug("Setting updated: {Key}", key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Settings.AnyAsync(s => s.Key == key);
    }
}
