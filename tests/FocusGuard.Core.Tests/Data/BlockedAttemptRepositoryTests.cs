using FocusGuard.Core.Data;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FocusGuard.Core.Tests.Data;

public class BlockedAttemptRepositoryTests : IDisposable
{
    private readonly DbContextOptions<FocusGuardDbContext> _options;
    private readonly BlockedAttemptRepository _repository;

    public BlockedAttemptRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<FocusGuardDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using (var context = new FocusGuardDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        var factory = new TestDbContextFactory(_options);
        var logger = new Mock<ILogger<BlockedAttemptRepository>>();
        _repository = new BlockedAttemptRepository(factory, logger.Object);
    }

    [Fact]
    public async Task CreateAsync_CreatesAttempt()
    {
        var attempt = new BlockedAttemptEntity
        {
            SessionId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Type = "Application",
            Target = "steam.exe"
        };

        await _repository.CreateAsync(attempt);

        Assert.NotEqual(Guid.Empty, attempt.Id);
    }

    [Fact]
    public async Task GetBySessionIdAsync_ReturnsMatchingAttempts()
    {
        var sessionId = Guid.NewGuid();

        await _repository.CreateAsync(new BlockedAttemptEntity
        {
            SessionId = sessionId, Timestamp = DateTime.UtcNow,
            Type = "Application", Target = "steam.exe"
        });
        await _repository.CreateAsync(new BlockedAttemptEntity
        {
            SessionId = sessionId, Timestamp = DateTime.UtcNow,
            Type = "Application", Target = "discord.exe"
        });
        await _repository.CreateAsync(new BlockedAttemptEntity
        {
            SessionId = Guid.NewGuid(), Timestamp = DateTime.UtcNow,
            Type = "Application", Target = "other.exe"
        });

        var results = await _repository.GetBySessionIdAsync(sessionId);

        Assert.Equal(2, results.Count);
        Assert.All(results, a => Assert.Equal(sessionId, a.SessionId));
    }

    [Fact]
    public async Task GetByDateRangeAsync_ReturnsAttemptsInRange()
    {
        var baseDate = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        await _repository.CreateAsync(new BlockedAttemptEntity
        {
            Timestamp = baseDate, Type = "Application", Target = "a.exe"
        });
        await _repository.CreateAsync(new BlockedAttemptEntity
        {
            Timestamp = baseDate.AddHours(5), Type = "Application", Target = "b.exe"
        });
        await _repository.CreateAsync(new BlockedAttemptEntity
        {
            Timestamp = baseDate.AddDays(2), Type = "Application", Target = "outside.exe"
        });

        var start = baseDate.Date;
        var end = baseDate.Date.AddDays(1);
        var results = await _repository.GetByDateRangeAsync(start, end);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetCountByDateRangeAsync_ReturnsCorrectCount()
    {
        var baseDate = new DateTime(2025, 2, 10, 8, 0, 0, DateTimeKind.Utc);

        await _repository.CreateAsync(new BlockedAttemptEntity
        {
            Timestamp = baseDate, Type = "Application", Target = "a.exe"
        });
        await _repository.CreateAsync(new BlockedAttemptEntity
        {
            Timestamp = baseDate.AddMinutes(30), Type = "Website", Target = "reddit.com"
        });

        var count = await _repository.GetCountByDateRangeAsync(baseDate.Date, baseDate.Date.AddDays(1));

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetCountByDateRangeAsync_EmptyRange_ReturnsZero()
    {
        var count = await _repository.GetCountByDateRangeAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        Assert.Equal(0, count);
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
