using System.Media;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Security;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Sessions;

public class SoundAlertService
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<SoundAlertService> _logger;

    public SoundAlertService(
        ISettingsRepository settingsRepository,
        ILogger<SoundAlertService> logger)
    {
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public async Task PlayWorkStartAsync()
    {
        if (!await IsSoundEnabledAsync()) return;
        PlaySound(SystemSounds.Exclamation);
    }

    public async Task PlayBreakStartAsync()
    {
        if (!await IsSoundEnabledAsync()) return;
        PlaySound(SystemSounds.Asterisk);
    }

    public async Task PlaySessionEndAsync()
    {
        if (!await IsSoundEnabledAsync()) return;
        PlaySound(SystemSounds.Hand);
    }

    private async Task<bool> IsSoundEnabledAsync()
    {
        var value = await _settingsRepository.GetAsync(SettingsKeys.SoundEnabled);
        // Default to enabled if not set
        if (value is null) return true;
        return bool.TryParse(value, out var enabled) && enabled;
    }

    private void PlaySound(SystemSound sound)
    {
        try
        {
            sound.Play();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to play system sound (headless or audio unavailable)");
        }
    }
}
