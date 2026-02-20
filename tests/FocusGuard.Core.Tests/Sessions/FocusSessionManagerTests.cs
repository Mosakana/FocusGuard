using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Security;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FocusGuard.Core.Tests.Sessions;

public class FocusSessionManagerTests : IDisposable
{
    private readonly Mock<IFocusSessionRepository> _sessionRepoMock;
    private readonly Mock<IProfileRepository> _profileRepoMock;
    private readonly PasswordGenerator _passwordGenerator;
    private readonly PasswordValidator _passwordValidator;
    private readonly Mock<ISettingsRepository> _settingsMock;
    private readonly FocusSessionManager _manager;

    private readonly Dictionary<string, string> _settingsStore = new();
    private readonly ProfileEntity _testProfile;
    private FocusSessionEntity? _createdSession;
    private FocusSessionEntity? _updatedSession;

    public FocusSessionManagerTests()
    {
        _sessionRepoMock = new Mock<IFocusSessionRepository>();
        _profileRepoMock = new Mock<IProfileRepository>();
        _passwordGenerator = new PasswordGenerator();
        _passwordValidator = new PasswordValidator();

        // MasterKeyService needs ISettingsRepository and ILogger — mock it
        _settingsMock = new Mock<ISettingsRepository>();
        _settingsMock.Setup(s => s.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => _settingsStore.GetValueOrDefault(key));
        _settingsMock.Setup(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => _settingsStore[key] = value)
            .Returns(Task.CompletedTask);
        _settingsMock.Setup(s => s.ExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => _settingsStore.ContainsKey(key));

        var masterKeyLogger = new Mock<ILogger<MasterKeyService>>();
        var masterKeyService = new MasterKeyService(_settingsMock.Object, masterKeyLogger.Object);

        // Test profile
        _testProfile = new ProfileEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test Profile",
            Color = "#4A90D9",
            BlockedWebsites = "[]",
            BlockedApplications = "[]"
        };
        _profileRepoMock.Setup(r => r.GetByIdAsync(_testProfile.Id))
            .ReturnsAsync(_testProfile);

        // Session repo: capture created/updated entities
        _sessionRepoMock.Setup(r => r.CreateAsync(It.IsAny<FocusSessionEntity>()))
            .Callback<FocusSessionEntity>(e =>
            {
                e.Id = Guid.NewGuid();
                _createdSession = e;
            })
            .ReturnsAsync((FocusSessionEntity e) => e);

        _sessionRepoMock.Setup(r => r.UpdateAsync(It.IsAny<FocusSessionEntity>()))
            .Callback<FocusSessionEntity>(e => _updatedSession = e)
            .Returns(Task.CompletedTask);

        var logger = new Mock<ILogger<FocusSessionManager>>();

        _manager = new FocusSessionManager(
            _sessionRepoMock.Object,
            _profileRepoMock.Object,
            _passwordGenerator,
            _passwordValidator,
            masterKeyService,
            _settingsMock.Object,
            logger.Object);
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    #region State Transitions

    [Fact]
    public void InitialState_IsIdle()
    {
        Assert.Equal(FocusSessionState.Idle, _manager.CurrentState);
    }

    [Fact]
    public async Task StartSession_TransitionsToWorking()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        Assert.Equal(FocusSessionState.Working, _manager.CurrentState);
    }

    [Fact]
    public async Task StartSession_ThenUnlock_TransitionsToIdle()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);
        var password = _manager.GetUnlockPassword()!;

        await _manager.TryUnlockAsync(password);

        Assert.Equal(FocusSessionState.Idle, _manager.CurrentState);
    }

    [Fact]
    public async Task StartSession_ThenNaturalEnd_TransitionsToIdle()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        await _manager.EndSessionNaturallyAsync();

        Assert.Equal(FocusSessionState.Idle, _manager.CurrentState);
    }

    [Fact]
    public async Task CannotStartSession_WhenAlreadyActive()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.StartSessionAsync(_testProfile.Id, 25));
    }

    [Fact]
    public async Task CannotStartSession_WithInvalidProfile()
    {
        var badId = Guid.NewGuid();
        _profileRepoMock.Setup(r => r.GetByIdAsync(badId)).ReturnsAsync((ProfileEntity?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.StartSessionAsync(badId, 25));
    }

    #endregion

    #region Password Generation & Unlock

    [Fact]
    public async Task StartSession_GeneratesPassword()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        var password = _manager.GetUnlockPassword();
        Assert.NotNull(password);
        Assert.NotEmpty(password);
    }

    [Fact]
    public async Task StartSession_UsesDefaultPasswordSettings()
    {
        // No settings stored — should default to Medium/30
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        var password = _manager.GetUnlockPassword()!;
        Assert.Equal(30, password.Length);
    }

    [Fact]
    public async Task StartSession_UsesCustomPasswordSettings()
    {
        _settingsStore[SettingsKeys.PasswordDifficulty] = "Easy";
        _settingsStore[SettingsKeys.PasswordLength] = "10";

        await _manager.StartSessionAsync(_testProfile.Id, 25);

        var password = _manager.GetUnlockPassword()!;
        Assert.Equal(10, password.Length);
        // Easy = lowercase only
        Assert.Matches("^[a-z]+$", password);
    }

    [Fact]
    public async Task TryUnlock_CorrectPassword_ReturnsTrue()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);
        var password = _manager.GetUnlockPassword()!;

        var result = await _manager.TryUnlockAsync(password);

        Assert.True(result);
    }

    [Fact]
    public async Task TryUnlock_WrongPassword_ReturnsFalse()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        var result = await _manager.TryUnlockAsync("wrong-password");

        Assert.False(result);
    }

    [Fact]
    public async Task TryUnlock_WrongPassword_SessionContinues()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        await _manager.TryUnlockAsync("wrong-password");

        Assert.Equal(FocusSessionState.Working, _manager.CurrentState);
    }

    [Fact]
    public async Task TryUnlock_WhenIdle_ReturnsFalse()
    {
        var result = await _manager.TryUnlockAsync("anything");

        Assert.False(result);
    }

    [Fact]
    public void GetUnlockPassword_WhenIdle_ReturnsNull()
    {
        var password = _manager.GetUnlockPassword();

        Assert.Null(password);
    }

    [Fact]
    public async Task GetUnlockPassword_AfterEnd_ReturnsNull()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);
        await _manager.EndSessionNaturallyAsync();

        var password = _manager.GetUnlockPassword();

        Assert.Null(password);
    }

    #endregion

    #region Emergency Unlock

    [Fact]
    public async Task EmergencyUnlock_CorrectMasterKey_ReturnsTrue()
    {
        // Set up master key
        var masterKeyService = new MasterKeyService(_settingsMock.Object, new Mock<ILogger<MasterKeyService>>().Object);
        var masterKey = await masterKeyService.GenerateMasterKeyAsync();

        // Create a new manager that uses the same settings store (with master key now set)
        var logger = new Mock<ILogger<FocusSessionManager>>();
        using var manager = new FocusSessionManager(
            _sessionRepoMock.Object,
            _profileRepoMock.Object,
            _passwordGenerator,
            _passwordValidator,
            masterKeyService,
            _settingsMock.Object,
            logger.Object);

        await manager.StartSessionAsync(_testProfile.Id, 25);

        var result = await manager.EmergencyUnlockAsync(masterKey);

        Assert.True(result);
        Assert.Equal(FocusSessionState.Idle, manager.CurrentState);
    }

    [Fact]
    public async Task EmergencyUnlock_WrongMasterKey_ReturnsFalse()
    {
        var masterKeyService = new MasterKeyService(_settingsMock.Object, new Mock<ILogger<MasterKeyService>>().Object);
        await masterKeyService.GenerateMasterKeyAsync();

        var logger = new Mock<ILogger<FocusSessionManager>>();
        using var manager = new FocusSessionManager(
            _sessionRepoMock.Object,
            _profileRepoMock.Object,
            _passwordGenerator,
            _passwordValidator,
            masterKeyService,
            _settingsMock.Object,
            logger.Object);

        await manager.StartSessionAsync(_testProfile.Id, 25);

        var result = await manager.EmergencyUnlockAsync("wrongkeywrongkeywrongkeywrongkeyw");

        Assert.False(result);
        Assert.Equal(FocusSessionState.Working, manager.CurrentState);
    }

    [Fact]
    public async Task EmergencyUnlock_WhenIdle_ReturnsFalse()
    {
        var result = await _manager.EmergencyUnlockAsync("anything");

        Assert.False(result);
    }

    #endregion

    #region Persistence

    [Fact]
    public async Task StartSession_CreatesEntityInRepository()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        _sessionRepoMock.Verify(r => r.CreateAsync(It.IsAny<FocusSessionEntity>()), Times.Once);
        Assert.NotNull(_createdSession);
        Assert.Equal(_testProfile.Id, _createdSession.ProfileId);
        Assert.Equal("Working", _createdSession.State);
        Assert.Equal(25, _createdSession.PlannedDurationMinutes);
    }

    [Fact]
    public async Task EndSession_UpdatesEntityInRepository()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);
        var password = _manager.GetUnlockPassword()!;

        await _manager.TryUnlockAsync(password);

        _sessionRepoMock.Verify(r => r.UpdateAsync(It.IsAny<FocusSessionEntity>()), Times.Once);
        Assert.NotNull(_updatedSession);
        Assert.Equal("Ended", _updatedSession.State);
        Assert.NotNull(_updatedSession.EndTime);
    }

    [Fact]
    public async Task EarlyUnlock_SetsWasUnlockedEarlyTrue()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);
        var password = _manager.GetUnlockPassword()!;

        await _manager.TryUnlockAsync(password);

        Assert.True(_updatedSession!.WasUnlockedEarly);
    }

    [Fact]
    public async Task NaturalEnd_SetsWasUnlockedEarlyFalse()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        await _manager.EndSessionNaturallyAsync();

        Assert.False(_updatedSession!.WasUnlockedEarly);
    }

    #endregion

    #region Events

    [Fact]
    public async Task StartSession_FiresStateChanged()
    {
        FocusSessionState? firedState = null;
        _manager.StateChanged += (_, state) => firedState = state;

        await _manager.StartSessionAsync(_testProfile.Id, 25);

        Assert.Equal(FocusSessionState.Working, firedState);
    }

    [Fact]
    public async Task EndSession_FiresSessionEnded()
    {
        var sessionEndedFired = false;
        _manager.SessionEnded += (_, _) => sessionEndedFired = true;

        await _manager.StartSessionAsync(_testProfile.Id, 25);
        await _manager.EndSessionNaturallyAsync();

        Assert.True(sessionEndedFired);
    }

    [Fact]
    public async Task EndSession_FiresStateChangedToIdle()
    {
        var states = new List<FocusSessionState>();
        _manager.StateChanged += (_, state) => states.Add(state);

        await _manager.StartSessionAsync(_testProfile.Id, 25);
        await _manager.EndSessionNaturallyAsync();

        Assert.Equal(2, states.Count);
        Assert.Equal(FocusSessionState.Working, states[0]);
        Assert.Equal(FocusSessionState.Idle, states[1]);
    }

    [Fact]
    public async Task EndSessionNaturally_WhenIdle_DoesNotFireEvents()
    {
        var eventFired = false;
        _manager.SessionEnded += (_, _) => eventFired = true;
        _manager.StateChanged += (_, _) => eventFired = true;

        await _manager.EndSessionNaturallyAsync();

        Assert.False(eventFired);
    }

    #endregion

    #region Pomodoro

    [Fact]
    public async Task AdvancePomodoroInterval_Working_TransitionsToShortBreak()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 120, pomodoroEnabled: true);

        _manager.AdvancePomodoroInterval();

        Assert.Equal(FocusSessionState.ShortBreak, _manager.CurrentState);
    }

    [Fact]
    public async Task AdvancePomodoroInterval_ShortBreak_TransitionsToWorking()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 120, pomodoroEnabled: true);

        _manager.AdvancePomodoroInterval(); // Working → ShortBreak
        _manager.AdvancePomodoroInterval(); // ShortBreak → Working

        Assert.Equal(FocusSessionState.Working, _manager.CurrentState);
    }

    [Fact]
    public async Task AdvancePomodoroInterval_LongBreakAfterFourWorkIntervals()
    {
        // Default long break interval is 4
        await _manager.StartSessionAsync(_testProfile.Id, 120, pomodoroEnabled: true);

        // Complete 4 work intervals
        for (int i = 0; i < 3; i++)
        {
            _manager.AdvancePomodoroInterval(); // Working → ShortBreak
            _manager.AdvancePomodoroInterval(); // ShortBreak → Working
        }
        // 4th work interval complete
        _manager.AdvancePomodoroInterval(); // Working → LongBreak (4th)

        Assert.Equal(FocusSessionState.LongBreak, _manager.CurrentState);
    }

    [Fact]
    public async Task AdvancePomodoroInterval_LongBreak_TransitionsToWorking()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 120, pomodoroEnabled: true);

        // Complete 4 work intervals to get to LongBreak
        for (int i = 0; i < 3; i++)
        {
            _manager.AdvancePomodoroInterval(); // Working → ShortBreak
            _manager.AdvancePomodoroInterval(); // ShortBreak → Working
        }
        _manager.AdvancePomodoroInterval(); // Working → LongBreak

        _manager.AdvancePomodoroInterval(); // LongBreak → Working

        Assert.Equal(FocusSessionState.Working, _manager.CurrentState);
    }

    [Fact]
    public async Task AdvancePomodoroInterval_FiresPomodoroIntervalChanged()
    {
        FocusSessionState? firedState = null;
        _manager.PomodoroIntervalChanged += (_, state) => firedState = state;

        await _manager.StartSessionAsync(_testProfile.Id, 120, pomodoroEnabled: true);
        _manager.AdvancePomodoroInterval();

        Assert.Equal(FocusSessionState.ShortBreak, firedState);
    }

    [Fact]
    public async Task AdvancePomodoroInterval_WhenNotPomodoroEnabled_DoesNothing()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25, pomodoroEnabled: false);

        _manager.AdvancePomodoroInterval();

        Assert.Equal(FocusSessionState.Working, _manager.CurrentState);
    }

    [Fact]
    public void AdvancePomodoroInterval_WhenIdle_DoesNothing()
    {
        _manager.AdvancePomodoroInterval();

        Assert.Equal(FocusSessionState.Idle, _manager.CurrentState);
    }

    [Fact]
    public async Task AdvancePomodoroInterval_UseCustomLongBreakInterval()
    {
        _settingsStore[SettingsKeys.PomodoroLongBreakInterval] = "2";

        await _manager.StartSessionAsync(_testProfile.Id, 120, pomodoroEnabled: true);

        // 1st work interval
        _manager.AdvancePomodoroInterval(); // Working → ShortBreak
        _manager.AdvancePomodoroInterval(); // ShortBreak → Working
        // 2nd work interval — should trigger long break with interval=2
        _manager.AdvancePomodoroInterval(); // Working → LongBreak

        Assert.Equal(FocusSessionState.LongBreak, _manager.CurrentState);
    }

    #endregion

    #region ResumeSession

    [Fact]
    public async Task ResumeSession_TransitionsToWorking()
    {
        var entity = CreateSessionEntity("Working");
        _sessionRepoMock.Setup(r => r.GetByIdAsync(entity.Id))
            .ReturnsAsync(entity);

        await _manager.ResumeSessionAsync(entity.Id, 15);

        Assert.Equal(FocusSessionState.Working, _manager.CurrentState);
    }

    [Fact]
    public async Task ResumeSession_GeneratesNewPassword()
    {
        var entity = CreateSessionEntity("Working");
        _sessionRepoMock.Setup(r => r.GetByIdAsync(entity.Id))
            .ReturnsAsync(entity);

        await _manager.ResumeSessionAsync(entity.Id, 15);

        var password = _manager.GetUnlockPassword();
        Assert.NotNull(password);
        Assert.NotEmpty(password);
    }

    [Fact]
    public async Task ResumeSession_FiresStateChanged()
    {
        var entity = CreateSessionEntity("Working");
        _sessionRepoMock.Setup(r => r.GetByIdAsync(entity.Id))
            .ReturnsAsync(entity);

        FocusSessionState? firedState = null;
        _manager.StateChanged += (_, state) => firedState = state;

        await _manager.ResumeSessionAsync(entity.Id, 15);

        Assert.Equal(FocusSessionState.Working, firedState);
    }

    [Fact]
    public async Task ResumeSession_ThrowsWhenAlreadyActive()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        var entity = CreateSessionEntity("Working");
        _sessionRepoMock.Setup(r => r.GetByIdAsync(entity.Id))
            .ReturnsAsync(entity);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.ResumeSessionAsync(entity.Id, 15));
    }

    [Fact]
    public async Task ResumeSession_ThrowsWhenSessionNotFound()
    {
        var badId = Guid.NewGuid();
        _sessionRepoMock.Setup(r => r.GetByIdAsync(badId))
            .ReturnsAsync((FocusSessionEntity?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.ResumeSessionAsync(badId, 15));
    }

    [Fact]
    public async Task ResumeSession_CanUnlockWithNewPassword()
    {
        var entity = CreateSessionEntity("Working");
        _sessionRepoMock.Setup(r => r.GetByIdAsync(entity.Id))
            .ReturnsAsync(entity);

        await _manager.ResumeSessionAsync(entity.Id, 15);
        var password = _manager.GetUnlockPassword()!;
        var result = await _manager.TryUnlockAsync(password);

        Assert.True(result);
        Assert.Equal(FocusSessionState.Idle, _manager.CurrentState);
    }

    private FocusSessionEntity CreateSessionEntity(string state)
    {
        return new FocusSessionEntity
        {
            Id = Guid.NewGuid(),
            ProfileId = _testProfile.Id,
            StartTime = DateTime.UtcNow.AddMinutes(-10),
            PlannedDurationMinutes = 25,
            State = state,
            PomodoroCompletedCount = 0
        };
    }

    #endregion

    #region CurrentSession Info

    [Fact]
    public void CurrentSession_WhenIdle_ReturnsNull()
    {
        Assert.Null(_manager.CurrentSession);
    }

    [Fact]
    public async Task CurrentSession_WhenActive_ReturnsInfo()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        var info = _manager.CurrentSession;

        Assert.NotNull(info);
        Assert.Equal(_testProfile.Id, info.ProfileId);
        Assert.Equal("Test Profile", info.ProfileName);
        Assert.Equal(FocusSessionState.Working, info.State);
        Assert.Equal(TimeSpan.FromMinutes(25), info.TotalPlanned);
    }

    [Fact]
    public async Task CurrentSession_AfterEnd_ReturnsNull()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);
        await _manager.EndSessionNaturallyAsync();

        Assert.Null(_manager.CurrentSession);
    }

    [Fact]
    public async Task CurrentSession_IncludesUnlockPassword()
    {
        await _manager.StartSessionAsync(_testProfile.Id, 25);

        var info = _manager.CurrentSession!;
        var password = _manager.GetUnlockPassword()!;

        Assert.Equal(password, info.UnlockPassword);
    }

    #endregion
}
