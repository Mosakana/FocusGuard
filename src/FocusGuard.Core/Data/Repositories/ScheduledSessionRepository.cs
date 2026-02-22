using FocusGuard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FocusGuard.Core.Data.Repositories;

public class ScheduledSessionRepository : IScheduledSessionRepository
{
    private readonly IDbContextFactory<FocusGuardDbContext> _contextFactory;

    public ScheduledSessionRepository(IDbContextFactory<FocusGuardDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ScheduledSessionEntity> CreateAsync(ScheduledSessionEntity entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();
        context.ScheduledSessions.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task<ScheduledSessionEntity?> GetByIdAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ScheduledSessions.FindAsync(id);
    }

    public async Task<List<ScheduledSessionEntity>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ScheduledSessions
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<List<ScheduledSessionEntity>> GetEnabledAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ScheduledSessions
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<List<ScheduledSessionEntity>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ScheduledSessions
            .Where(s => s.StartTime < end && s.EndTime > start)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task UpdateAsync(ScheduledSessionEntity entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ScheduledSessions.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.ScheduledSessions.FindAsync(id);
        if (entity is not null)
        {
            context.ScheduledSessions.Remove(entity);
            await context.SaveChangesAsync();
        }
    }
}
