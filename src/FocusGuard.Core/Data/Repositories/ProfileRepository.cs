using FocusGuard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Data.Repositories;

public class ProfileRepository : IProfileRepository
{
    private readonly IDbContextFactory<FocusGuardDbContext> _contextFactory;
    private readonly ILogger<ProfileRepository> _logger;

    public ProfileRepository(IDbContextFactory<FocusGuardDbContext> contextFactory, ILogger<ProfileRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<List<ProfileEntity>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Profiles.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<ProfileEntity?> GetByIdAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Profiles.FindAsync(id);
    }

    public async Task<ProfileEntity> CreateAsync(ProfileEntity profile)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        if (await context.Profiles.AnyAsync(p => p.Name == profile.Name))
        {
            throw new InvalidOperationException($"A profile with the name '{profile.Name}' already exists.");
        }

        profile.Id = Guid.NewGuid();
        profile.CreatedAt = DateTime.UtcNow;
        context.Profiles.Add(profile);
        await context.SaveChangesAsync();

        _logger.LogInformation("Created profile: {Name} ({Id})", profile.Name, profile.Id);
        return profile;
    }

    public async Task UpdateAsync(ProfileEntity profile)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var existing = await context.Profiles.FindAsync(profile.Id)
            ?? throw new InvalidOperationException($"Profile with ID '{profile.Id}' not found.");

        if (await context.Profiles.AnyAsync(p => p.Name == profile.Name && p.Id != profile.Id))
        {
            throw new InvalidOperationException($"A profile with the name '{profile.Name}' already exists.");
        }

        existing.Name = profile.Name;
        existing.Color = profile.Color;
        existing.BlockedWebsites = profile.BlockedWebsites;
        existing.BlockedApplications = profile.BlockedApplications;

        await context.SaveChangesAsync();
        _logger.LogInformation("Updated profile: {Name} ({Id})", profile.Name, profile.Id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var profile = await context.Profiles.FindAsync(id);
        if (profile is null) return false;

        if (profile.IsPreset)
        {
            throw new InvalidOperationException("Preset profiles cannot be deleted.");
        }

        context.Profiles.Remove(profile);
        await context.SaveChangesAsync();

        _logger.LogInformation("Deleted profile: {Name} ({Id})", profile.Name, profile.Id);
        return true;
    }

    public async Task<bool> ExistsAsync(string name, Guid? excludeId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.Profiles.Where(p => p.Name == name);
        if (excludeId.HasValue)
        {
            query = query.Where(p => p.Id != excludeId.Value);
        }
        return await query.AnyAsync();
    }
}
