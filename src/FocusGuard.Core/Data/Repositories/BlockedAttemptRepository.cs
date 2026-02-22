using FocusGuard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Data.Repositories;

public class BlockedAttemptRepository : IBlockedAttemptRepository
{
    private readonly IDbContextFactory<FocusGuardDbContext> _contextFactory;
    private readonly ILogger<BlockedAttemptRepository> _logger;

    public BlockedAttemptRepository(IDbContextFactory<FocusGuardDbContext> contextFactory, ILogger<BlockedAttemptRepository> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task CreateAsync(BlockedAttemptEntity attempt)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        attempt.Id = Guid.NewGuid();
        context.BlockedAttempts.Add(attempt);
        await context.SaveChangesAsync();

        _logger.LogDebug("Logged blocked attempt: {Type} - {Target}", attempt.Type, attempt.Target);
    }

    public async Task<List<BlockedAttemptEntity>> GetBySessionIdAsync(Guid sessionId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.BlockedAttempts
            .Where(a => a.SessionId == sessionId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();
    }

    public async Task<List<BlockedAttemptEntity>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.BlockedAttempts
            .Where(a => a.Timestamp >= start && a.Timestamp < end)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();
    }

    public async Task<int> GetCountByDateRangeAsync(DateTime start, DateTime end)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.BlockedAttempts
            .CountAsync(a => a.Timestamp >= start && a.Timestamp < end);
    }
}
