using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.App.Services;
using FocusGuard.Core.Configuration;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Hardening;
using FocusGuard.Core.Security;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FocusGuard.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly IStrictModeService _strictModeService;
    private readonly IAutoStartService _autoStartService;
    private readonly MasterKeyService _masterKeyService;
    private readonly IFocusSessionManager _sessionManager;
    private readonly IDialogService _dialogService;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isLoading;

    // General
    [ObservableProperty] private bool _autoStartEnabled;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _strictModeEnabled;
    [ObservableProperty] private bool _canToggleStrictMode = true;
    [ObservableProperty] private bool _isPortableMode;

    // Session Defaults
    [ObservableProperty] private int _defaultDurationMinutes = 25;
    [ObservableProperty] private PasswordDifficulty _passwordDifficulty = PasswordDifficulty.Medium;
    [ObservableProperty] private int _passwordLength = 30;

    // Pomodoro
    [ObservableProperty] private int _pomodoroWorkMinutes = 25;
    [ObservableProperty] private int _pomodoroShortBreakMinutes = 5;
    [ObservableProperty] private int _pomodoroLongBreakMinutes = 15;
    [ObservableProperty] private int _pomodoroLongBreakInterval = 4;
    [ObservableProperty] private bool _pomodoroAutoStart = true;

    // Notifications
    [ObservableProperty] private bool _sessionNotificationsEnabled = true;
    [ObservableProperty] private bool _pomodoroNotificationsEnabled = true;
    [ObservableProperty] private bool _blockedNotificationsEnabled = true;
    [ObservableProperty] private bool _goalNotificationsEnabled = true;
    [ObservableProperty] private bool _soundEnabled = true;

    // Security
    [ObservableProperty] private bool _isMasterKeySetup;

    public static PasswordDifficulty[] PasswordDifficultyOptions { get; } =
        [PasswordDifficulty.Easy, PasswordDifficulty.Medium, PasswordDifficulty.Hard];

    public SettingsViewModel(
        ISettingsRepository settingsRepository,
        IStrictModeService strictModeService,
        IAutoStartService autoStartService,
        MasterKeyService masterKeyService,
        IFocusSessionManager sessionManager,
        IDialogService dialogService,
        ILogger<SettingsViewModel> logger)
    {
        _settingsRepository = settingsRepository;
        _strictModeService = strictModeService;
        _autoStartService = autoStartService;
        _masterKeyService = masterKeyService;
        _sessionManager = sessionManager;
        _dialogService = dialogService;
        _logger = logger;
    }

    public override async void OnNavigatedTo()
    {
        await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        _isLoading = true;
        try
        {
            // General
            IsPortableMode = AppPaths.IsPortableMode;
            AutoStartEnabled = _autoStartService.IsEnabled();
            MinimizeToTray = await GetBoolAsync(SettingsKeys.MinimizeToTray, false);
            StrictModeEnabled = await _strictModeService.IsEnabledAsync();
            CanToggleStrictMode = await _strictModeService.CanToggleAsync();

            // Session Defaults
            DefaultDurationMinutes = await GetIntAsync(SettingsKeys.DefaultSessionDuration, 25);
            var difficultyStr = await _settingsRepository.GetAsync(SettingsKeys.PasswordDifficulty);
            PasswordDifficulty = Enum.TryParse<PasswordDifficulty>(difficultyStr, out var diff) ? diff : PasswordDifficulty.Medium;
            PasswordLength = await GetIntAsync(SettingsKeys.PasswordLength, 30);

            // Pomodoro
            PomodoroWorkMinutes = await GetIntAsync(SettingsKeys.PomodoroWorkMinutes, 25);
            PomodoroShortBreakMinutes = await GetIntAsync(SettingsKeys.PomodoroShortBreakMinutes, 5);
            PomodoroLongBreakMinutes = await GetIntAsync(SettingsKeys.PomodoroLongBreakMinutes, 15);
            PomodoroLongBreakInterval = await GetIntAsync(SettingsKeys.PomodoroLongBreakInterval, 4);
            PomodoroAutoStart = await GetBoolAsync(SettingsKeys.PomodoroAutoStart, true);

            // Notifications
            SessionNotificationsEnabled = await GetBoolAsync(SettingsKeys.NotifySessionEnabled, true);
            PomodoroNotificationsEnabled = await GetBoolAsync(SettingsKeys.NotifyPomodoroEnabled, true);
            BlockedNotificationsEnabled = await GetBoolAsync(SettingsKeys.NotifyBlockedEnabled, true);
            GoalNotificationsEnabled = await GetBoolAsync(SettingsKeys.NotifyGoalEnabled, true);
            SoundEnabled = await GetBoolAsync(SettingsKeys.SoundEnabled, true);

            // Security
            IsMasterKeySetup = await _masterKeyService.IsSetupCompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
        }
        finally
        {
            _isLoading = false;
        }
    }

    // --- General property change handlers ---

    partial void OnAutoStartEnabledChanged(bool value)
    {
        if (_isLoading) return;
        try
        {
            if (value) _autoStartService.Enable();
            else _autoStartService.Disable();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle auto-start");
        }
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveBoolAsync(SettingsKeys.MinimizeToTray, value);
    }

    partial void OnStrictModeEnabledChanged(bool value)
    {
        if (_isLoading) return;
        _ = SetStrictModeAsync(value);
    }

    private async Task SetStrictModeAsync(bool value)
    {
        try
        {
            await _strictModeService.SetEnabledAsync(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle strict mode");
            // Revert the UI
            _isLoading = true;
            StrictModeEnabled = !value;
            _isLoading = false;
        }
    }

    // --- Session property change handlers ---

    partial void OnDefaultDurationMinutesChanged(int value)
    {
        if (_isLoading || value < 1) return;
        _ = SaveIntAsync(SettingsKeys.DefaultSessionDuration, value);
    }

    partial void OnPasswordDifficultyChanged(PasswordDifficulty value)
    {
        if (_isLoading) return;
        _ = _settingsRepository.SetAsync(SettingsKeys.PasswordDifficulty, value.ToString());
    }

    partial void OnPasswordLengthChanged(int value)
    {
        if (_isLoading || value < 1) return;
        _ = SaveIntAsync(SettingsKeys.PasswordLength, value);
    }

    // --- Pomodoro property change handlers ---

    partial void OnPomodoroWorkMinutesChanged(int value)
    {
        if (_isLoading || value < 1) return;
        _ = SaveIntAsync(SettingsKeys.PomodoroWorkMinutes, value);
    }

    partial void OnPomodoroShortBreakMinutesChanged(int value)
    {
        if (_isLoading || value < 1) return;
        _ = SaveIntAsync(SettingsKeys.PomodoroShortBreakMinutes, value);
    }

    partial void OnPomodoroLongBreakMinutesChanged(int value)
    {
        if (_isLoading || value < 1) return;
        _ = SaveIntAsync(SettingsKeys.PomodoroLongBreakMinutes, value);
    }

    partial void OnPomodoroLongBreakIntervalChanged(int value)
    {
        if (_isLoading || value < 1) return;
        _ = SaveIntAsync(SettingsKeys.PomodoroLongBreakInterval, value);
    }

    partial void OnPomodoroAutoStartChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveBoolAsync(SettingsKeys.PomodoroAutoStart, value);
    }

    // --- Notification property change handlers ---

    partial void OnSessionNotificationsEnabledChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveBoolAsync(SettingsKeys.NotifySessionEnabled, value);
    }

    partial void OnPomodoroNotificationsEnabledChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveBoolAsync(SettingsKeys.NotifyPomodoroEnabled, value);
    }

    partial void OnBlockedNotificationsEnabledChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveBoolAsync(SettingsKeys.NotifyBlockedEnabled, value);
    }

    partial void OnGoalNotificationsEnabledChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveBoolAsync(SettingsKeys.NotifyGoalEnabled, value);
    }

    partial void OnSoundEnabledChanged(bool value)
    {
        if (_isLoading) return;
        _ = SaveBoolAsync(SettingsKeys.SoundEnabled, value);
    }

    // --- Commands ---

    [RelayCommand]
    private async Task RegenerateMasterKeyAsync()
    {
        var confirmed = await _dialogService.ConfirmAsync(
            "Regenerate Master Key",
            "This will generate a new master recovery key. The old key will no longer work. Are you sure?");

        if (!confirmed) return;

        try
        {
            var setupVm = new MasterKeySetupViewModel(_masterKeyService);
            await setupVm.GenerateKeyAsync();
            var dialog = new Views.MasterKeySetupDialog { DataContext = setupVm };
            dialog.ShowDialog();
            IsMasterKeySetup = await _masterKeyService.IsSetupCompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to regenerate master key");
        }
    }

    // --- Helpers ---

    private async Task<bool> GetBoolAsync(string key, bool defaultValue)
    {
        var value = await _settingsRepository.GetAsync(key);
        return value is not null ? bool.TryParse(value, out var b) && b : defaultValue;
    }

    private async Task<int> GetIntAsync(string key, int defaultValue)
    {
        var value = await _settingsRepository.GetAsync(key);
        return value is not null && int.TryParse(value, out var i) ? i : defaultValue;
    }

    private async Task SaveBoolAsync(string key, bool value)
    {
        try
        {
            await _settingsRepository.SetAsync(key, value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save setting {Key}", key);
        }
    }

    private async Task SaveIntAsync(string key, int value)
    {
        try
        {
            await _settingsRepository.SetAsync(key, value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save setting {Key}", key);
        }
    }
}
