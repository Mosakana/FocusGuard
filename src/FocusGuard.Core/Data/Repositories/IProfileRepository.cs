using FocusGuard.Core.Data.Entities;

namespace FocusGuard.Core.Data.Repositories;

public interface IProfileRepository
{
    Task<List<ProfileEntity>> GetAllAsync();
    Task<ProfileEntity?> GetByIdAsync(Guid id);
    Task<ProfileEntity> CreateAsync(ProfileEntity profile);
    Task UpdateAsync(ProfileEntity profile);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ExistsAsync(string name, Guid? excludeId = null);
}
