using System.Text.Json;
using FocusGuard.Core.Blocking;
using FocusGuard.Core.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace FocusGuard.App.Services;

public class BlockingOrchestrator
{
    private readonly IWebsiteBlocker _websiteBlocker;
    private readonly IApplicationBlocker _applicationBlocker;
    private readonly IProfileRepository _profileRepository;
    private readonly ILogger<BlockingOrchestrator> _logger;

    public bool IsActive { get; private set; }
    public string? ActiveProfileName { get; private set; }
    public Guid? ActiveProfileId { get; private set; }

    public BlockingOrchestrator(
        IWebsiteBlocker websiteBlocker,
        IApplicationBlocker applicationBlocker,
        IProfileRepository profileRepository,
        ILogger<BlockingOrchestrator> logger)
    {
        _websiteBlocker = websiteBlocker;
        _applicationBlocker = applicationBlocker;
        _profileRepository = profileRepository;
        _logger = logger;
    }

    public async Task ActivateProfileAsync(Guid profileId)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId);
        if (profile is null)
        {
            _logger.LogError("Profile {Id} not found", profileId);
            return;
        }

        // Stop any current blocking
        await DeactivateAsync();

        var websites = JsonSerializer.Deserialize<List<string>>(profile.BlockedWebsites) ?? [];
        var applications = JsonSerializer.Deserialize<List<string>>(profile.BlockedApplications) ?? [];

        if (websites.Count > 0)
        {
            await _websiteBlocker.ApplyBlocklistAsync(websites);
        }

        if (applications.Count > 0)
        {
            _applicationBlocker.StartBlocking(applications);
        }

        ActiveProfileId = profileId;
        ActiveProfileName = profile.Name;
        IsActive = true;

        _logger.LogInformation("Activated blocking for profile: {Name}", profile.Name);
    }

    public async Task DeactivateAsync()
    {
        if (!IsActive) return;

        if (_websiteBlocker.IsActive)
        {
            await _websiteBlocker.RemoveBlocklistAsync();
        }

        if (_applicationBlocker.IsActive)
        {
            _applicationBlocker.StopBlocking();
        }

        var previousName = ActiveProfileName;
        ActiveProfileId = null;
        ActiveProfileName = null;
        IsActive = false;

        _logger.LogInformation("Deactivated blocking for profile: {Name}", previousName);
    }
}
