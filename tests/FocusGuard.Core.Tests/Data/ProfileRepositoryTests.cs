using FocusGuard.Core.Data;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FocusGuard.Core.Tests.Data;

public class ProfileRepositoryTests : IDisposable
{
    private readonly DbContextOptions<FocusGuardDbContext> _options;
    private readonly ProfileRepository _repository;

    public ProfileRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<FocusGuardDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Seed the database
        using (var context = new FocusGuardDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        var factory = new TestDbContextFactory(_options);
        var logger = new Mock<ILogger<ProfileRepository>>();
        _repository = new ProfileRepository(factory, logger.Object);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSeededPresets()
    {
        var profiles = await _repository.GetAllAsync();
        Assert.True(profiles.Count >= 3);
        Assert.Contains(profiles, p => p.Name == "Social Media" && p.IsPreset);
        Assert.Contains(profiles, p => p.Name == "Gaming" && p.IsPreset);
        Assert.Contains(profiles, p => p.Name == "Entertainment" && p.IsPreset);
    }

    [Fact]
    public async Task CreateAsync_CreatesNewProfile()
    {
        var profile = new ProfileEntity
        {
            Name = "Test Profile",
            Color = "#FF0000",
            BlockedWebsites = "[\"test.com\"]",
            BlockedApplications = "[]"
        };

        var created = await _repository.CreateAsync(profile);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Test Profile", created.Name);

        var retrieved = await _repository.GetByIdAsync(created.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Test Profile", retrieved.Name);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsInvalidOperationException()
    {
        var profile1 = new ProfileEntity { Name = "Unique Name", Color = "#000000" };
        await _repository.CreateAsync(profile1);

        var profile2 = new ProfileEntity { Name = "Unique Name", Color = "#111111" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.CreateAsync(profile2));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExistingProfile()
    {
        var profile = new ProfileEntity { Name = "Before Update", Color = "#000000" };
        var created = await _repository.CreateAsync(profile);

        created.Name = "After Update";
        created.Color = "#FFFFFF";
        await _repository.UpdateAsync(created);

        var updated = await _repository.GetByIdAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal("After Update", updated.Name);
        Assert.Equal("#FFFFFF", updated.Color);
    }

    [Fact]
    public async Task DeleteAsync_DeletesNonPresetProfile()
    {
        var profile = new ProfileEntity { Name = "To Delete", Color = "#000000" };
        var created = await _repository.CreateAsync(profile);

        var result = await _repository.DeleteAsync(created.Id);
        Assert.True(result);

        var deleted = await _repository.GetByIdAsync(created.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAsync_PresetProfile_ThrowsInvalidOperationException()
    {
        var profiles = await _repository.GetAllAsync();
        var preset = profiles.First(p => p.IsPreset);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.DeleteAsync(preset.Id));
    }

    [Fact]
    public async Task DeleteAsync_NonexistentProfile_ReturnsFalse()
    {
        var result = await _repository.DeleteAsync(Guid.NewGuid());
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueForExistingName()
    {
        var result = await _repository.ExistsAsync("Social Media");
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseForNonexistentName()
    {
        var result = await _repository.ExistsAsync("Nonexistent");
        Assert.False(result);
    }

    [Fact]
    public async Task ExistsAsync_ExcludesSpecifiedId()
    {
        var profiles = await _repository.GetAllAsync();
        var socialMedia = profiles.First(p => p.Name == "Social Media");

        var result = await _repository.ExistsAsync("Social Media", socialMedia.Id);
        Assert.False(result);
    }

    public void Dispose()
    {
        using var context = new FocusGuardDbContext(_options);
        context.Database.EnsureDeleted();
    }

    private class TestDbContextFactory : IDbContextFactory<FocusGuardDbContext>
    {
        private readonly DbContextOptions<FocusGuardDbContext> _options;

        public TestDbContextFactory(DbContextOptions<FocusGuardDbContext> options)
        {
            _options = options;
        }

        public FocusGuardDbContext CreateDbContext()
        {
            return new FocusGuardDbContext(_options);
        }
    }
}
