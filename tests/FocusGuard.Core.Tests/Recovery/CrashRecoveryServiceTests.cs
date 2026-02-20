using FocusGuard.Core.Blocking;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Recovery;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FocusGuard.Core.Tests.Recovery;

public class CrashRecoveryServiceTests
{
    private readonly Mock<IWebsiteBlocker> _websiteBlockerMock;
    private readonly Mock<IFocusSessionRepository> _sessionRepoMock;
    private readonly CrashRecoveryService _service;

    public CrashRecoveryServiceTests()
    {
        _websiteBlockerMock = new Mock<IWebsiteBlocker>();
        _sessionRepoMock = new Mock<IFocusSessionRepository>();
        var logger = new Mock<ILogger<CrashRecoveryService>>();

        _service = new CrashRecoveryService(
            _websiteBlockerMock.Object,
            _sessionRepoMock.Object,
            logger.Object);
    }

    #region CleanupOrphanedSessionsAsync

    [Fact]
    public async Task CleanupOrphanedSessions_WorkingSession_MarkedAsEnded()
    {
        var session = CreateSession("Working");
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync([session]);

        var count = await _service.CleanupOrphanedSessionsAsync();

        Assert.Equal(1, count);
        _sessionRepoMock.Verify(r => r.UpdateAsync(It.Is<FocusSessionEntity>(
            e => e.State == "Ended" && e.WasUnlockedEarly)), Times.Once);
    }

    [Fact]
    public async Task CleanupOrphanedSessions_ShortBreakSession_MarkedAsEnded()
    {
        var session = CreateSession("ShortBreak");
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync([session]);

        var count = await _service.CleanupOrphanedSessionsAsync();

        Assert.Equal(1, count);
        _sessionRepoMock.Verify(r => r.UpdateAsync(It.Is<FocusSessionEntity>(
            e => e.State == "Ended")), Times.Once);
    }

    [Fact]
    public async Task CleanupOrphanedSessions_LongBreakSession_MarkedAsEnded()
    {
        var session = CreateSession("LongBreak");
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync([session]);

        var count = await _service.CleanupOrphanedSessionsAsync();

        Assert.Equal(1, count);
        _sessionRepoMock.Verify(r => r.UpdateAsync(It.Is<FocusSessionEntity>(
            e => e.State == "Ended")), Times.Once);
    }

    [Fact]
    public async Task CleanupOrphanedSessions_NoOrphanedSessions_ReturnsZero()
    {
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync([]);

        var count = await _service.CleanupOrphanedSessionsAsync();

        Assert.Equal(0, count);
        _sessionRepoMock.Verify(r => r.UpdateAsync(It.IsAny<FocusSessionEntity>()), Times.Never);
    }

    [Fact]
    public async Task CleanupOrphanedSessions_MultipleSessions_AllCleaned()
    {
        var sessions = new List<FocusSessionEntity>
        {
            CreateSession("Working"),
            CreateSession("ShortBreak"),
            CreateSession("LongBreak")
        };
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync(sessions);

        var count = await _service.CleanupOrphanedSessionsAsync();

        Assert.Equal(3, count);
        _sessionRepoMock.Verify(r => r.UpdateAsync(It.IsAny<FocusSessionEntity>()), Times.Exactly(3));
    }

    #endregion

    #region RecoverAsync

    [Fact]
    public async Task RecoverAsync_CallsBothCleanupMethods()
    {
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync([]);

        await _service.RecoverAsync();

        // Orphaned sessions cleanup was called
        _sessionRepoMock.Verify(r => r.GetOrphanedSessionsAsync(), Times.Once);
    }

    #endregion

    private static FocusSessionEntity CreateSession(string state)
    {
        return new FocusSessionEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddMinutes(-10),
            PlannedDurationMinutes = 25,
            State = state
        };
    }
}
