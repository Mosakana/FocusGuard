using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.App.Models;
using FocusGuard.App.Services;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FocusGuard.App.ViewModels;

public partial class ProfilesViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProfilesViewModel> _logger;

    [ObservableProperty]
    private ProfileListItem? _selectedProfile;

    [ObservableProperty]
    private ProfileEditorViewModel? _editor;

    [ObservableProperty]
    private bool _isEditorVisible;

    public ObservableCollection<ProfileListItem> Profiles { get; } = [];

    public ProfilesViewModel(
        IProfileRepository profileRepository,
        IDialogService dialogService,
        IServiceProvider serviceProvider,
        ILogger<ProfilesViewModel> logger)
    {
        _profileRepository = profileRepository;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public override async void OnNavigatedTo()
    {
        await LoadProfilesAsync();
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

                Profiles.Add(new ProfileListItem
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

    partial void OnSelectedProfileChanged(ProfileListItem? value)
    {
        if (value is not null)
        {
            OpenEditor(value.Id);
        }
        else
        {
            IsEditorVisible = false;
            Editor = null;
        }
    }

    private async void OpenEditor(Guid profileId)
    {
        var editor = _serviceProvider.GetRequiredService<ProfileEditorViewModel>();
        await editor.LoadAsync(profileId);
        editor.ProfileSaved += OnProfileSaved;
        Editor = editor;
        IsEditorVisible = true;
    }

    private async void OnProfileSaved(object? sender, EventArgs e)
    {
        await LoadProfilesAsync();
    }

    [RelayCommand]
    private async Task CreateProfileAsync()
    {
        try
        {
            var profile = new ProfileEntity
            {
                Name = "New Profile",
                Color = "#4A90D9",
                BlockedWebsites = "[]",
                BlockedApplications = "[]"
            };

            // Ensure unique name
            var baseName = profile.Name;
            var counter = 1;
            while (await _profileRepository.ExistsAsync(profile.Name))
            {
                profile.Name = $"{baseName} ({counter++})";
            }

            var created = await _profileRepository.CreateAsync(profile);
            await LoadProfilesAsync();

            // Select the new profile
            var item = Profiles.FirstOrDefault(p => p.Id == created.Id);
            SelectedProfile = item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create profile");
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is null) return;

        if (SelectedProfile.IsPreset)
        {
            await _dialogService.ConfirmAsync("Cannot Delete", "Preset profiles cannot be deleted.");
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Delete Profile",
            $"Are you sure you want to delete \"{SelectedProfile.Name}\"?");

        if (!confirmed) return;

        try
        {
            await _profileRepository.DeleteAsync(SelectedProfile.Id);
            IsEditorVisible = false;
            Editor = null;
            SelectedProfile = null;
            await LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete profile");
        }
    }

    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        if (SelectedProfile is null) return;

        try
        {
            var source = await _profileRepository.GetByIdAsync(SelectedProfile.Id);
            if (source is null) return;

            var newProfile = new ProfileEntity
            {
                Name = $"{source.Name} (Copy)",
                Color = source.Color,
                BlockedWebsites = source.BlockedWebsites,
                BlockedApplications = source.BlockedApplications
            };

            var counter = 1;
            while (await _profileRepository.ExistsAsync(newProfile.Name))
            {
                newProfile.Name = $"{source.Name} (Copy {counter++})";
            }

            var created = await _profileRepository.CreateAsync(newProfile);
            await LoadProfilesAsync();

            var item = Profiles.FirstOrDefault(p => p.Id == created.Id);
            SelectedProfile = item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to duplicate profile");
        }
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (SelectedProfile is null) return;

        try
        {
            var profile = await _profileRepository.GetByIdAsync(SelectedProfile.Id);
            if (profile is null) return;

            var path = await _dialogService.SaveFileAsync(
                "JSON Files (*.json)|*.json",
                $"{profile.Name}.json",
                "Export Profile");

            if (path is null) return;

            var exportData = new
            {
                name = profile.Name,
                color = profile.Color,
                blockedWebsites = JsonSerializer.Deserialize<List<string>>(profile.BlockedWebsites) ?? [],
                blockedApplications = JsonSerializer.Deserialize<List<string>>(profile.BlockedApplications) ?? []
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);

            _logger.LogInformation("Exported profile to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export profile");
        }
    }

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        try
        {
            var path = await _dialogService.OpenFileAsync(
                "JSON Files (*.json)|*.json",
                "Import Profile");

            if (path is null) return;

            var json = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString() ?? "Imported Profile";
            var color = root.TryGetProperty("color", out var colorEl) ? colorEl.GetString() ?? "#4A90D9" : "#4A90D9";
            var websites = root.TryGetProperty("blockedWebsites", out var wEl)
                ? JsonSerializer.Serialize(JsonSerializer.Deserialize<List<string>>(wEl.GetRawText()) ?? [])
                : "[]";
            var apps = root.TryGetProperty("blockedApplications", out var aEl)
                ? JsonSerializer.Serialize(JsonSerializer.Deserialize<List<string>>(aEl.GetRawText()) ?? [])
                : "[]";

            var counter = 1;
            var baseName = name;
            while (await _profileRepository.ExistsAsync(name))
            {
                name = $"{baseName} ({counter++})";
            }

            var profile = new ProfileEntity
            {
                Name = name,
                Color = color,
                BlockedWebsites = websites,
                BlockedApplications = apps
            };

            var created = await _profileRepository.CreateAsync(profile);
            await LoadProfilesAsync();

            var item = Profiles.FirstOrDefault(p => p.Id == created.Id);
            SelectedProfile = item;

            _logger.LogInformation("Imported profile from {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import profile");
        }
    }
}
