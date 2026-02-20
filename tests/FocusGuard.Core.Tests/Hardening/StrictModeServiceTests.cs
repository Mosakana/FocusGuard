using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Hardening;
using FocusGuard.Core.Security;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FocusGuard.Core.Tests.Hardening;

public class StrictModeServiceTests
{
    private readonly Mock<ISettingsRepository> _settingsMock;
    private readonly Mock<IFocusSessionManager> _sessionManagerMock;
    private readonly StrictModeService _service;
    private readonly Dictionary<string, string> _settingsStore = new();

    public StrictModeServiceTests()
    {
        _settingsMock = new Mock<ISettingsRepository>();
        _settingsMock.Setup(s => s.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => _settingsStore.GetValueOrDefault(key));
        _settingsMock.Setup(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => _settingsStore[key] = value)
            .Returns(Task.CompletedTask);

        _sessionManagerMock = new Mock<IFocusSessionManager>();
        _sessionManagerMock.Setup(m => m.CurrentState).Returns(FocusSessionState.Idle);

        var logger = new Mock<ILogger<StrictModeService>>();

        _service = new StrictModeService(
            _settingsMock.Object,
            _sessionManagerMock.Object,
            logger.Object);
    }

    [Fact]
    public async Task IsEnabled_DefaultsFalse()
    {
        var result = await _service.IsEnabledAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task SetEnabled_PersistsTrue()
    {
        await _service.SetEnabledAsync(true);

        var result = await _service.IsEnabledAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task SetEnabled_PersistsFalse()
    {
        await _service.SetEnabledAsync(true);
        await _service.SetEnabledAsync(false);

        var result = await _service.IsEnabledAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task CanToggle_WhenIdle_ReturnsTrue()
    {
        _sessionManagerMock.Setup(m => m.CurrentState).Returns(FocusSessionState.Idle);

        var result = await _service.CanToggleAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task CanToggle_WhenWorking_ReturnsFalse()
    {
        _sessionManagerMock.Setup(m => m.CurrentState).Returns(FocusSessionState.Working);

        var result = await _service.CanToggleAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task CanToggle_WhenShortBreak_ReturnsFalse()
    {
        _sessionManagerMock.Setup(m => m.CurrentState).Returns(FocusSessionState.ShortBreak);

        var result = await _service.CanToggleAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task SetEnabled_ThrowsWhenSessionActive()
    {
        _sessionManagerMock.Setup(m => m.CurrentState).Returns(FocusSessionState.Working);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SetEnabledAsync(true));
    }

    [Fact]
    public async Task SetEnabled_WritesToSettingsRepository()
    {
        await _service.SetEnabledAsync(true);

        _settingsMock.Verify(s => s.SetAsync(SettingsKeys.StrictModeEnabled, "True"), Times.Once);
    }
}
