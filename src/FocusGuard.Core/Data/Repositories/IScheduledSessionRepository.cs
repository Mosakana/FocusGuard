using FocusGuard.Core.Data.Entities;

namespace FocusGuard.Core.Data.Repositories;

public interface IScheduledSessionRepository
{
    Task<ScheduledSessionEntity> CreateAsync(ScheduledSessionEntity entity);
    Task<ScheduledSessionEntity?> GetByIdAsync(Guid id);
    Task<List<ScheduledSessionEntity>> GetAllAsync();
    Task<List<ScheduledSessionEntity>> GetEnabledAsync();
    Task<List<ScheduledSessionEntity>> GetByDateRangeAsync(DateTime start, DateTime end);
    Task UpdateAsync(ScheduledSessionEntity entity);
    Task DeleteAsync(Guid id);
}
