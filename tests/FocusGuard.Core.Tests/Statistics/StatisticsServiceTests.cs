using FocusGuard.Core.Data;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Statistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FocusGuard.Core.Tests.Statistics;

public class StatisticsServiceTests : IDisposable
{
    private readonly DbContextOptions<FocusGuardDbContext> _options;
    private readonly FocusSessionRepository _sessionRepository;
    private readonly BlockedAttemptRepository _attemptRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly StatisticsService _service;

    public StatisticsServiceTests()
    {
        _options = new DbContextOptionsBuilder<FocusGuardDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using (var context = new FocusGuardDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        var factory = new TestDbContextFactory(_options);
        _sessionRepository = new FocusSessionRepository(factory, new Mock<ILogger<FocusSessionRepository>>().Object);
        _attemptRepository = new BlockedAttemptRepository(factory, new Mock<ILogger<BlockedAttemptRepository>>().Object);
        _profileRepository = new ProfileRepository(factory, new Mock<ILogger<ProfileRepository>>().Object);

        _service = new StatisticsService(
            _sessionRepository,
            _attemptRepository,
            _profileRepository,
            new Mock<ILogger<StatisticsService>>().Object);
    }

    [Fact]
    public async Task GetDailyFocusAsync_EmptyRange_ReturnsZeroFilledDays()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 8, 0, 0, 0, DateTimeKind.Utc);

        var result = await _service.GetDailyFocusAsync(start, end);

        Assert.Equal(7, result.Count);
        Assert.All(result, d =>
        {
            Assert.Equal(0, d.TotalFocusMinutes);
            Assert.Equal(0, d.SessionCount);
        });
    }

    [Fact]
    public async Task GetDailyFocusAsync_WithSessions_GroupsByDay()
    {
        var profileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var day1 = new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        await _sessionRepository.CreateAsync(new FocusSessionEntity
        {
            ProfileId = profileId,
            StartTime = day1.AddHours(9),
            EndTime = day1.AddHours(9).AddMinutes(30),
            PlannedDurationMinutes = 30,
            ActualDurationMinutes = 30,
            PomodoroCompletedCount = 1,
            State = "Ended"
        });
        await _sessionRepository.CreateAsync(new FocusSessionEntity
        {
            ProfileId = profileId,
            StartTime = day1.AddHours(14),
            EndTime = day1.AddHours(14).AddMinutes(45),
            PlannedDurationMinutes = 45,
            ActualDurationMinutes = 45,
            PomodoroCompletedCount = 2,
            State = "Ended"
        });

        var result = await _service.GetDailyFocusAsync(day1, day1.AddDays(1));

        Assert.Single(result);
        Assert.Equal(75, result[0].TotalFocusMinutes);
        Assert.Equal(2, result[0].SessionCount);
        Assert.Equal(3, result[0].PomodoroCount);
    }

    [Fact]
    public async Task GetDailyFocusAsync_WithBlockedAttempts_IncludesCount()
    {
        var day = new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        await _attemptRepository.CreateAsync(new BlockedAttemptEntity
        {
            Timestamp = day.AddHours(10),
            Type = "Application",
            Target = "steam.exe"
        });
        await _attemptRepository.CreateAsync(new BlockedAttemptEntity
        {
            Timestamp = day.AddHours(11),
            Type = "Application",
            Target = "discord.exe"
        });

        var result = await _service.GetDailyFocusAsync(day, day.AddDays(1));

        Assert.Single(result);
        Assert.Equal(2, result[0].BlockedAttemptCount);
    }

    [Fact]
    public async Task GetProfileBreakdownAsync_GroupsByProfile()
    {
        var profile1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var profile2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var day = new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        await _sessionRepository.CreateAsync(new FocusSessionEntity
        {
            ProfileId = profile1,
            StartTime = day.AddHours(9),
            ActualDurationMinutes = 60,
            State = "Ended"
        });
        await _sessionRepository.CreateAsync(new FocusSessionEntity
        {
            ProfileId = profile2,
            StartTime = day.AddHours(14),
            ActualDurationMinutes = 30,
            State = "Ended"
        });

        var result = await _service.GetProfileBreakdownAsync(day, day.AddDays(1));

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.ProfileId == profile1 && p.TotalFocusMinutes == 60);
        Assert.Contains(result, p => p.ProfileId == profile2 && p.TotalFocusMinutes == 30);
    }

    [Fact]
    public async Task GetStreakInfoAsync_NoSessions_ReturnsZero()
    {
        var result = await _service.GetStreakInfoAsync();

        Assert.Equal(0, result.CurrentStreak);
        Assert.Equal(0, result.LongestStreak);
        Assert.Null(result.StreakStartDate);
    }

    [Fact]
    public async Task GetStreakInfoAsync_ConsecutiveDays_CalculatesStreak()
    {
        var profileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var today = DateTime.UtcNow.Date;

        // Create sessions for last 3 consecutive days
        for (int i = 0; i < 3; i++)
        {
            await _sessionRepository.CreateAsync(new FocusSessionEntity
            {
                ProfileId = profileId,
                StartTime = today.AddDays(-i).AddHours(10),
                EndTime = today.AddDays(-i).AddHours(10).AddMinutes(30),
                ActualDurationMinutes = 30,
                State = "Ended"
            });
        }

        var result = await _service.GetStreakInfoAsync();

        Assert.Equal(3, result.CurrentStreak);
        Assert.Equal(3, result.LongestStreak);
        Assert.NotNull(result.StreakStartDate);
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsCompletePeriod()
    {
        var profileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var start = new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(7);

        await _sessionRepository.CreateAsync(new FocusSessionEntity
        {
            ProfileId = profileId,
            StartTime = start.AddHours(9),
            ActualDurationMinutes = 60,
            PomodoroCompletedCount = 2,
            State = "Ended"
        });

        var result = await _service.GetStatisticsAsync(start, end);

        Assert.Equal(start, result.PeriodStart);
        Assert.Equal(end, result.PeriodEnd);
        Assert.Equal(60, result.TotalFocusMinutes);
        Assert.Equal(1, result.TotalSessions);
        Assert.Equal(2, result.TotalPomodoroCount);
        Assert.Equal(7, result.DailyBreakdown.Count);
    }

    [Fact]
    public async Task GetHeatmapDataAsync_Returns91Days()
    {
        var result = await _service.GetHeatmapDataAsync(91);

        Assert.Equal(91, result.Count);
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
