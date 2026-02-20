using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Security;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Hardening;

public class StrictModeService : IStrictModeService
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly IFocusSessionManager _sessionManager;
    private readonly ILogger<StrictModeService> _logger;

    public StrictModeService(
        ISettingsRepository settingsRepository,
        IFocusSessionManager sessionManager,
        ILogger<StrictModeService> logger)
    {
        _settingsRepository = settingsRepository;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<bool> IsEnabledAsync()
    {
        var value = await _settingsRepository.GetAsync(SettingsKeys.StrictModeEnabled);
        return value is not null && bool.TryParse(value, out var enabled) && enabled;
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (!await CanToggleAsync())
            throw new InvalidOperationException("Cannot toggle strict mode while a focus session is active.");

        await _settingsRepository.SetAsync(SettingsKeys.StrictModeEnabled, enabled.ToString());
        _logger.LogInformation("Strict mode {Action}", enabled ? "enabled" : "disabled");
    }

    public Task<bool> CanToggleAsync()
    {
        return Task.FromResult(_sessionManager.CurrentState == FocusSessionState.Idle);
    }
}
