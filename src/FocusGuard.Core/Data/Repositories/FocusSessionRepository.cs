using FocusGuard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Data.Repositories;

public class FocusSessionRepository : IFocusSessionRepository
{
    private readonly IDbContextFactory<FocusGuardDbContext> _contextFactory;
    private readonly ILogger<FocusSessionRepository> _logger;

    public FocusSessionRepository(IDbContextFactory<FocusGuardDbContext> contextFactory, ILogger<FocusSessionRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<FocusSessionEntity> CreateAsync(FocusSessionEntity session)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        session.Id = Guid.NewGuid();
        session.CreatedAt = DateTime.UtcNow;
        context.FocusSessions.Add(session);
        await context.SaveChangesAsync();

        _logger.LogInformation("Created focus session {Id} for profile {ProfileId}", session.Id, session.ProfileId);
        return session;
    }

    public async Task UpdateAsync(FocusSessionEntity session)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var existing = await context.FocusSessions.FindAsync(session.Id)
            ?? throw new InvalidOperationException($"Focus session with ID '{session.Id}' not found.");

        existing.EndTime = session.EndTime;
        existing.ActualDurationMinutes = session.ActualDurationMinutes;
        existing.PomodoroCompletedCount = session.PomodoroCompletedCount;
        existing.WasUnlockedEarly = session.WasUnlockedEarly;
        existing.State = session.State;

        await context.SaveChangesAsync();
        _logger.LogInformation("Updated focus session {Id}, State={State}", session.Id, session.State);
    }

    public async Task<FocusSessionEntity?> GetByIdAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.FocusSessions.FindAsync(id);
    }

    public async Task<FocusSessionEntity?> GetActiveSessionAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.FocusSessions
            .Where(s => s.State != "Ended")
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();
    }

    public async Task<List<FocusSessionEntity>> GetRecentAsync(int count = 10)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.FocusSessions
            .OrderByDescending(s => s.StartTime)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<FocusSessionEntity>> GetOrphanedSessionsAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.FocusSessions
            .Where(s => s.State != "Ended" && s.State != "Idle")
            .ToListAsync();
    }
}
