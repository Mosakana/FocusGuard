using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FocusGuard.App.Models;
using FocusGuard.App.Services;
using FocusGuard.Core.Blocking;
using FocusGuard.Core.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace FocusGuard.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly BlockingOrchestrator _orchestrator;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty]
    private string _statusText = "Idle — No active focus session";

    [ObservableProperty]
    private bool _isBlocking;

    [ObservableProperty]
    private string _activeProfileName = string.Empty;

    public ObservableCollection<ProfileSummary> Profiles { get; } = [];

    public DashboardViewModel(
        IProfileRepository profileRepository,
        BlockingOrchestrator orchestrator,
        ILogger<DashboardViewModel> logger)
    {
        _profileRepository = profileRepository;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public override async void OnNavigatedTo()
    {
        await LoadProfilesAsync();
        UpdateBlockingStatus();
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            var profiles = await _profileRepository.GetAllAsync();
            Profiles.Clear();

            foreach (var p in profiles)
            {
                var websites = JsonSerializer.Deserialize<List<string>>(p.BlockedWebsites) ?? [];
                var apps = JsonSerializer.Deserialize<List<string>>(p.BlockedApplications) ?? [];

                Profiles.Add(new ProfileSummary
                {
                    Id = p.Id,
                    Name = p.Name,
                    Color = p.Color,
                    WebsiteCount = websites.Count,
                    AppCount = apps.Count,
                    IsPreset = p.IsPreset
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load profiles");
        }
    }

    private void UpdateBlockingStatus()
    {
        IsBlocking = _orchestrator.IsActive;
        if (IsBlocking)
        {
            ActiveProfileName = _orchestrator.ActiveProfileName ?? "Unknown";
            StatusText = $"Active — Blocking with \"{ActiveProfileName}\"";
        }
        else
        {
            ActiveProfileName = string.Empty;
            StatusText = "Idle — No active focus session";
        }
    }
}
