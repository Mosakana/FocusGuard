using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Recovery;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FocusGuard.Core.Tests.Recovery;

public class SessionRecoveryServiceTests
{
    private readonly Mock<IFocusSessionRepository> _sessionRepoMock;
    private readonly Mock<IFocusSessionManager> _sessionManagerMock;
    private readonly SessionRecoveryService _service;

    public SessionRecoveryServiceTests()
    {
        _sessionRepoMock = new Mock<IFocusSessionRepository>();
        _sessionManagerMock = new Mock<IFocusSessionManager>();
        var logger = new Mock<ILogger<SessionRecoveryService>>();

        _service = new SessionRecoveryService(
            _sessionRepoMock.Object,
            _sessionManagerMock.Object,
            logger.Object);
    }

    [Fact]
    public async Task TryRecover_NoActiveSessions_ReturnsFalse()
    {
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync([]);

        var result = await _service.TryRecoverSessionAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task TryRecover_ExpiredSession_MarkedAsEnded_ReturnsFalse()
    {
        var session = new FocusSessionEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddMinutes(-60),
            PlannedDurationMinutes = 25,
            State = "Working"
        };
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync([session]);

        var result = await _service.TryRecoverSessionAsync();

        Assert.False(result);
        _sessionRepoMock.Verify(r => r.UpdateAsync(It.Is<FocusSessionEntity>(
            e => e.State == "Ended" && !e.WasUnlockedEarly)), Times.Once);
    }

    [Fact]
    public async Task TryRecover_ActiveSessionWithRemainingTime_ResumesSession()
    {
        var session = new FocusSessionEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddMinutes(-10),
            PlannedDurationMinutes = 25,
            State = "Working"
        };
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync([session]);

        var result = await _service.TryRecoverSessionAsync();

        Assert.True(result);
        _sessionManagerMock.Verify(m => m.ResumeSessionAsync(
            session.Id,
            It.Is<int>(min => min > 0 && min <= 15)),
            Times.Once);
    }

    [Fact]
    public async Task TryRecover_MultipleOrphaned_RecoversMostRecent()
    {
        var oldSession = new FocusSessionEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddMinutes(-30),
            PlannedDurationMinutes = 60,
            State = "Working"
        };
        var recentSession = new FocusSessionEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            PlannedDurationMinutes = 25,
            State = "Working"
        };
        _sessionRepoMock.Setup(r => r.GetOrphanedSessionsAsync())
            .ReturnsAsync([oldSession, recentSession]);

        var result = await _service.TryRecoverSessionAsync();

        Assert.True(result);
        _sessionManagerMock.Verify(m => m.ResumeSessionAsync(
            recentSession.Id,
            It.IsAny<int>()),
            Times.Once);
    }
}
