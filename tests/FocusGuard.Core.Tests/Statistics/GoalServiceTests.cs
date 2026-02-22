using FocusGuard.Core.Data;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Statistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FocusGuard.Core.Tests.Statistics;

public class GoalServiceTests : IDisposable
{
    private readonly DbContextOptions<FocusGuardDbContext> _options;
    private readonly FocusSessionRepository _sessionRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly GoalService _service;

    public GoalServiceTests()
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
        _settingsRepository = new SettingsRepository(factory, new Mock<ILogger<SettingsRepository>>().Object);
        _profileRepository = new ProfileRepository(factory, new Mock<ILogger<ProfileRepository>>().Object);

        _service = new GoalService(
            _settingsRepository,
            _sessionRepository,
            _profileRepository,
            new Mock<ILogger<GoalService>>().Object);
    }

    [Fact]
    public async Task GetGoalAsync_NoGoal_ReturnsNull()
    {
        var result = await _service.GetGoalAsync(GoalPeriod.Daily);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetGoalAsync_ThenGetGoalAsync_ReturnsGoal()
    {
        var goal = new FocusGoal { Period = GoalPeriod.Daily, TargetMinutes = 120 };

        await _service.SetGoalAsync(goal);
        var result = await _service.GetGoalAsync(GoalPeriod.Daily);

        Assert.NotNull(result);
        Assert.Equal(GoalPeriod.Daily, result.Period);
        Assert.Equal(120, result.TargetMinutes);
    }

    [Fact]
    public async Task SetGoalAsync_WeeklyGoal_PersistsCorrectly()
    {
        var goal = new FocusGoal { Period = GoalPeriod.Weekly, TargetMinutes = 600 };

        await _service.SetGoalAsync(goal);
        var result = await _service.GetGoalAsync(GoalPeriod.Weekly);

        Assert.NotNull(result);
        Assert.Equal(GoalPeriod.Weekly, result.Period);
        Assert.Equal(600, result.TargetMinutes);
    }

    [Fact]
    public async Task RemoveGoalAsync_RemovesGoal()
    {
        var goal = new FocusGoal { Period = GoalPeriod.Daily, TargetMinutes = 60 };
        await _service.SetGoalAsync(goal);

        await _service.RemoveGoalAsync(GoalPeriod.Daily);
        var result = await _service.GetGoalAsync(GoalPeriod.Daily);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllProgressAsync_WithGoalAndSessions_CalculatesProgress()
    {
        var profileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var goal = new FocusGoal { Period = GoalPeriod.Daily, TargetMinutes = 120 };
        await _service.SetGoalAsync(goal);

        // Add a session today
        var today = DateTime.UtcNow.Date;
        await _sessionRepository.CreateAsync(new FocusSessionEntity
        {
            ProfileId = profileId,
            StartTime = today.AddHours(9),
            EndTime = today.AddHours(10),
            ActualDurationMinutes = 60,
            State = "Ended"
        });

        var progress = await _service.GetAllProgressAsync();

        Assert.Single(progress);
        Assert.Equal(60, progress[0].CurrentMinutes);
        Assert.Equal(50, progress[0].CompletionPercent);
        Assert.False(progress[0].IsCompleted);
        Assert.Equal(60, progress[0].RemainingMinutes);
    }

    [Fact]
    public async Task GetAllProgressAsync_GoalCompleted_ShowsCompleted()
    {
        var profileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var goal = new FocusGoal { Period = GoalPeriod.Daily, TargetMinutes = 30 };
        await _service.SetGoalAsync(goal);

        var today = DateTime.UtcNow.Date;
        await _sessionRepository.CreateAsync(new FocusSessionEntity
        {
            ProfileId = profileId,
            StartTime = today.AddHours(9),
            EndTime = today.AddHours(10),
            ActualDurationMinutes = 60,
            State = "Ended"
        });

        var progress = await _service.GetAllProgressAsync();

        Assert.Single(progress);
        Assert.True(progress[0].IsCompleted);
        Assert.Equal(100, progress[0].CompletionPercent);
        Assert.Equal(0, progress[0].RemainingMinutes);
    }

    [Fact]
    public async Task GetAllProgressAsync_NoGoals_ReturnsEmpty()
    {
        var progress = await _service.GetAllProgressAsync();
        Assert.Empty(progress);
    }

    [Fact]
    public async Task SetGoalAsync_ProfileSpecific_PersistsCorrectly()
    {
        var profileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var goal = new FocusGoal
        {
            Period = GoalPeriod.Daily,
            TargetMinutes = 90,
            ProfileId = profileId
        };

        await _service.SetGoalAsync(goal);
        var result = await _service.GetGoalAsync(GoalPeriod.Daily, profileId);

        Assert.NotNull(result);
        Assert.Equal(profileId, result.ProfileId);
        Assert.Equal(90, result.TargetMinutes);
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
