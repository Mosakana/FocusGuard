using FocusGuard.Core.Data.Entities;

namespace FocusGuard.Core.Data.Repositories;

public interface IFocusSessionRepository
{
    Task<FocusSessionEntity> CreateAsync(FocusSessionEntity session);
    Task UpdateAsync(FocusSessionEntity session);
    Task<FocusSessionEntity?> GetByIdAsync(Guid id);
    Task<FocusSessionEntity?> GetActiveSessionAsync();
    Task<List<FocusSessionEntity>> GetRecentAsync(int count = 10);
    Task<List<FocusSessionEntity>> GetOrphanedSessionsAsync();
}
