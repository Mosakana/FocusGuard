using FocusGuard.Core.Data.Entities;

namespace FocusGuard.Core.Data.Repositories;

public interface IBlockedAttemptRepository
{
    Task CreateAsync(BlockedAttemptEntity attempt);
    Task<List<BlockedAttemptEntity>> GetBySessionIdAsync(Guid sessionId);
    Task<List<BlockedAttemptEntity>> GetByDateRangeAsync(DateTime start, DateTime end);
    Task<int> GetCountByDateRangeAsync(DateTime start, DateTime end);
}
