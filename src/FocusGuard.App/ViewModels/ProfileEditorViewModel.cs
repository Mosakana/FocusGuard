using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.App.Services;
using FocusGuard.Core.Blocking;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace FocusGuard.App.ViewModels;

public partial class ProfileEditorViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly IDialogService _dialogService;
    private readonly IWebsiteBlocker _websiteBlocker;
    private readonly IApplicationBlocker _applicationBlocker;
    private readonly ILogger<ProfileEditorViewModel> _logger;

    private Guid _profileId;
    private string _originalName = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _color = "#4A90D9";

    [ObservableProperty]
    private bool _isPreset;

    [ObservableProperty]
    private string _newWebsite = string.Empty;

    [ObservableProperty]
    private string _newApplication = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private bool _isWebsiteBlockingActive;

    [ObservableProperty]
    private bool _isAppBlockingActive;

    public ObservableCollection<string> BlockedWebsites { get; } = [];
    public ObservableCollection<string> BlockedApplications { get; } = [];

    public static string[] ColorSwatches => [
        "#E74C3C", "#E67E22", "#F39C12", "#2ECC71", "#1ABC9C",
        "#3498DB", "#4A90D9", "#9B59B6", "#E91E63", "#607D8B"
    ];

    public event EventHandler? ProfileSaved;

    public ProfileEditorViewModel(
        IProfileRepository profileRepository,
        IDialogService dialogService,
        IWebsiteBlocker websiteBlocker,
        IApplicationBlocker applicationBlocker,
        ILogger<ProfileEditorViewModel> logger)
    {
        _profileRepository = profileRepository;
        _dialogService = dialogService;
        _websiteBlocker = websiteBlocker;
        _applicationBlocker = applicationBlocker;
        _logger = logger;

        BlockedWebsites.CollectionChanged += (_, _) => HasUnsavedChanges = true;
        BlockedApplications.CollectionChanged += (_, _) => HasUnsavedChanges = true;
    }

    partial void OnNameChanged(string value) => HasUnsavedChanges = true;
    partial void OnColorChanged(string value) => HasUnsavedChanges = true;

    public async Task LoadAsync(Guid profileId)
    {
        _profileId = profileId;
        var profile = await _profileRepository.GetByIdAsync(profileId);
        if (profile is null) return;

        _originalName = profile.Name;
        Name = profile.Name;
        Color = profile.Color;
        IsPreset = profile.IsPreset;

        BlockedWebsites.Clear();
        foreach (var w in JsonSerializer.Deserialize<List<string>>(profile.BlockedWebsites) ?? [])
            BlockedWebsites.Add(w);

        BlockedApplications.Clear();
        foreach (var a in JsonSerializer.Deserialize<List<string>>(profile.BlockedApplications) ?? [])
            BlockedApplications.Add(a);

        HasUnsavedChanges = false;

        // Update blocking status
        IsWebsiteBlockingActive = _websiteBlocker.IsActive;
        IsAppBlockingActive = _applicationBlocker.IsActive;
    }

    [RelayCommand]
    private void AddWebsite()
    {
        var domain = DomainHelper.Normalize(NewWebsite);
        if (string.IsNullOrEmpty(domain) || BlockedWebsites.Contains(domain)) return;

        if (!DomainHelper.IsValid(domain))
        {
            _logger.LogWarning("Invalid domain: {Domain}", domain);
            return;
        }

        BlockedWebsites.Add(domain);
        NewWebsite = string.Empty;
    }

    [RelayCommand]
    private void RemoveWebsite(string website)
    {
        BlockedWebsites.Remove(website);
    }

    [RelayCommand]
    private void AddApplication()
    {
        var name = NewApplication.Trim();
        if (string.IsNullOrEmpty(name) || BlockedApplications.Contains(name)) return;

        // Ensure .exe extension
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name += ".exe";

        BlockedApplications.Add(name.ToLowerInvariant());
        NewApplication = string.Empty;
    }

    [RelayCommand]
    private async Task BrowseApplicationAsync()
    {
        var path = await _dialogService.OpenFileAsync(
            "Executables (*.exe)|*.exe",
            "Select Application");

        if (path is null) return;

        var fileName = Path.GetFileName(path).ToLowerInvariant();
        if (!BlockedApplications.Contains(fileName))
        {
            BlockedApplications.Add(fileName);
        }
    }

    [RelayCommand]
    private void RemoveApplication(string app)
    {
        BlockedApplications.Remove(app);
    }

    [RelayCommand]
    private void SelectColor(string color)
    {
        Color = color;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            _logger.LogWarning("Profile name cannot be empty");
            return;
        }

        try
        {
            var profile = new ProfileEntity
            {
                Id = _profileId,
                Name = Name.Trim(),
                Color = Color,
                BlockedWebsites = JsonSerializer.Serialize(BlockedWebsites.ToList()),
                BlockedApplications = JsonSerializer.Serialize(BlockedApplications.ToList())
            };

            await _profileRepository.UpdateAsync(profile);
            _originalName = Name;
            HasUnsavedChanges = false;
            ProfileSaved?.Invoke(this, EventArgs.Empty);

            _logger.LogInformation("Saved profile: {Name}", Name);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to save profile");
            await _dialogService.ConfirmAsync("Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DiscardAsync()
    {
        if (HasUnsavedChanges)
        {
            var confirmed = await _dialogService.ConfirmAsync(
                "Discard Changes",
                "You have unsaved changes. Discard them?");
            if (!confirmed) return;
        }

        await LoadAsync(_profileId);
    }

    [RelayCommand]
    private async Task TestWebsiteBlockingAsync()
    {
        try
        {
            var domains = BlockedWebsites.ToList();
            if (domains.Count == 0) return;

            await _websiteBlocker.ApplyBlocklistAsync(domains);
            IsWebsiteBlockingActive = true;
            _logger.LogInformation("Started test website blocking");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start website blocking");
            await _dialogService.ConfirmAsync("Error",
                $"Failed to modify hosts file: {ex.Message}\nMake sure the app is running as administrator.");
        }
    }

    [RelayCommand]
    private async Task StopWebsiteBlockingAsync()
    {
        try
        {
            await _websiteBlocker.RemoveBlocklistAsync();
            IsWebsiteBlockingActive = false;
            _logger.LogInformation("Stopped test website blocking");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop website blocking");
        }
    }

    [RelayCommand]
    private void TestAppBlocking()
    {
        var apps = BlockedApplications.ToList();
        if (apps.Count == 0) return;

        _applicationBlocker.StartBlocking(apps);
        IsAppBlockingActive = true;
        _logger.LogInformation("Started test app blocking");
    }

    [RelayCommand]
    private void StopAppBlocking()
    {
        _applicationBlocker.StopBlocking();
        IsAppBlockingActive = false;
        _logger.LogInformation("Stopped test app blocking");
    }
}
