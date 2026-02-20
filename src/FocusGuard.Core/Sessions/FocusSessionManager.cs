using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Security;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Sessions;

public class FocusSessionManager : IFocusSessionManager, IDisposable
{
    private readonly IFocusSessionRepository _sessionRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly PasswordGenerator _passwordGenerator;
    private readonly PasswordValidator _passwordValidator;
    private readonly MasterKeyService _masterKeyService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<FocusSessionManager> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private System.Timers.Timer? _sessionTimer;
    private FocusSessionEntity? _currentEntity;
    private string? _unlockPassword;
    private string _profileName = string.Empty;
    private bool _pomodoroEnabled;
    private int _pomodoroCompletedCount;

    // Pomodoro settings (loaded from ISettingsRepository on start)
    private int _pomodoroWorkMinutes = 25;
    private int _pomodoroShortBreakMinutes = 5;
    private int _pomodoroLongBreakMinutes = 15;
    private int _pomodoroLongBreakInterval = 4;

    public FocusSessionState CurrentState { get; private set; } = FocusSessionState.Idle;

    public FocusSessionInfo? CurrentSession => BuildCurrentSessionInfo();

    public event EventHandler<FocusSessionState>? StateChanged;
    public event EventHandler? SessionEnded;
    public event EventHandler<FocusSessionState>? PomodoroIntervalChanged;

    public FocusSessionManager(
        IFocusSessionRepository sessionRepository,
        IProfileRepository profileRepository,
        PasswordGenerator passwordGenerator,
        PasswordValidator passwordValidator,
        MasterKeyService masterKeyService,
        ISettingsRepository settingsRepository,
        ILogger<FocusSessionManager> logger)
    {
        _sessionRepository = sessionRepository;
        _profileRepository = profileRepository;
        _passwordGenerator = passwordGenerator;
        _passwordValidator = passwordValidator;
        _masterKeyService = masterKeyService;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public async Task StartSessionAsync(Guid profileId, int durationMinutes, bool pomodoroEnabled = false)
    {
        await _lock.WaitAsync();
        try
        {
            if (CurrentState != FocusSessionState.Idle)
                throw new InvalidOperationException("Cannot start a new session while one is already active.");

            // Load profile name
            var profile = await _profileRepository.GetByIdAsync(profileId)
                ?? throw new InvalidOperationException($"Profile with ID '{profileId}' not found.");
            _profileName = profile.Name;

            // Load password settings (defaults: Medium, 30)
            var difficulty = await LoadPasswordDifficultyAsync();
            var length = await LoadPasswordLengthAsync();

            // Generate unlock password
            _unlockPassword = _passwordGenerator.Generate(length, difficulty);

            // Load pomodoro settings
            _pomodoroEnabled = pomodoroEnabled;
            _pomodoroCompletedCount = 0;
            if (pomodoroEnabled)
            {
                await LoadPomodoroSettingsAsync();
            }

            // Create session entity
            _currentEntity = new FocusSessionEntity
            {
                ProfileId = profileId,
                StartTime = DateTime.UtcNow,
                PlannedDurationMinutes = durationMinutes,
                State = FocusSessionState.Working.ToString()
            };
            _currentEntity = await _sessionRepository.CreateAsync(_currentEntity);

            // Start session timer
            _sessionTimer = new System.Timers.Timer(durationMinutes * 60_000.0);
            _sessionTimer.AutoReset = false;
            _sessionTimer.Elapsed += async (_, _) => await EndSessionNaturallyAsync();
            _sessionTimer.Start();

            CurrentState = FocusSessionState.Working;
            _logger.LogInformation("Focus session started: {SessionId}, Profile={Profile}, Duration={Duration}min",
                _currentEntity.Id, _profileName, durationMinutes);

            StateChanged?.Invoke(this, CurrentState);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> TryUnlockAsync(string password)
    {
        await _lock.WaitAsync();
        try
        {
            if (CurrentState == FocusSessionState.Idle || CurrentState == FocusSessionState.Ended)
                return false;

            if (_unlockPassword is null)
                return false;

            if (!_passwordValidator.Validate(_unlockPassword, password))
            {
                _logger.LogInformation("Unlock attempt failed: incorrect password");
                return false;
            }

            _logger.LogInformation("Session unlocked with password");
            await EndSessionInternalAsync(wasUnlockedEarly: true);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> EmergencyUnlockAsync(string masterKey)
    {
        await _lock.WaitAsync();
        try
        {
            if (CurrentState == FocusSessionState.Idle || CurrentState == FocusSessionState.Ended)
                return false;

            var isValid = await _masterKeyService.ValidateMasterKeyAsync(masterKey);
            if (!isValid)
            {
                _logger.LogInformation("Emergency unlock attempt failed: invalid master key");
                return false;
            }

            _logger.LogWarning("Session ended via emergency master key unlock");
            await EndSessionInternalAsync(wasUnlockedEarly: true);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EndSessionNaturallyAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (CurrentState == FocusSessionState.Idle || CurrentState == FocusSessionState.Ended)
                return;

            _logger.LogInformation("Session ended naturally (timer expired)");
            await EndSessionInternalAsync(wasUnlockedEarly: false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public string? GetUnlockPassword()
    {
        return _unlockPassword;
    }

    public void AdvancePomodoroInterval()
    {
        _lock.Wait();
        try
        {
            if (CurrentState == FocusSessionState.Idle || CurrentState == FocusSessionState.Ended)
                return;

            if (!_pomodoroEnabled)
                return;

            FocusSessionState newState;

            if (CurrentState == FocusSessionState.Working)
            {
                _pomodoroCompletedCount++;

                // Long break after every N work intervals
                newState = (_pomodoroCompletedCount % _pomodoroLongBreakInterval == 0)
                    ? FocusSessionState.LongBreak
                    : FocusSessionState.ShortBreak;
            }
            else if (CurrentState == FocusSessionState.ShortBreak || CurrentState == FocusSessionState.LongBreak)
            {
                newState = FocusSessionState.Working;
            }
            else
            {
                return;
            }

            CurrentState = newState;

            // Update entity state
            if (_currentEntity is not null)
            {
                _currentEntity.State = CurrentState.ToString();
                _currentEntity.PomodoroCompletedCount = _pomodoroCompletedCount;
            }

            _logger.LogInformation("Pomodoro interval advanced to {State}, completed={Count}",
                CurrentState, _pomodoroCompletedCount);

            StateChanged?.Invoke(this, CurrentState);
            PomodoroIntervalChanged?.Invoke(this, CurrentState);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EndSessionInternalAsync(bool wasUnlockedEarly)
    {
        StopTimer();

        if (_currentEntity is not null)
        {
            _currentEntity.EndTime = DateTime.UtcNow;
            _currentEntity.ActualDurationMinutes = (int)(DateTime.UtcNow - _currentEntity.StartTime).TotalMinutes;
            _currentEntity.WasUnlockedEarly = wasUnlockedEarly;
            _currentEntity.PomodoroCompletedCount = _pomodoroCompletedCount;
            _currentEntity.State = FocusSessionState.Ended.ToString();

            await _sessionRepository.UpdateAsync(_currentEntity);
        }

        CurrentState = FocusSessionState.Idle;
        _unlockPassword = null;
        _currentEntity = null;
        _profileName = string.Empty;
        _pomodoroEnabled = false;
        _pomodoroCompletedCount = 0;

        SessionEnded?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, CurrentState);
    }

    private void StopTimer()
    {
        if (_sessionTimer is not null)
        {
            _sessionTimer.Stop();
            _sessionTimer.Dispose();
            _sessionTimer = null;
        }
    }

    private FocusSessionInfo? BuildCurrentSessionInfo()
    {
        if (_currentEntity is null)
            return null;

        var elapsed = DateTime.UtcNow - _currentEntity.StartTime;
        var totalPlanned = TimeSpan.FromMinutes(_currentEntity.PlannedDurationMinutes);
        var remaining = totalPlanned - elapsed;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        return new FocusSessionInfo
        {
            SessionId = _currentEntity.Id,
            ProfileId = _currentEntity.ProfileId,
            ProfileName = _profileName,
            State = CurrentState,
            StartTime = _currentEntity.StartTime,
            Elapsed = elapsed,
            TotalPlanned = totalPlanned,
            CurrentIntervalRemaining = remaining,
            PomodoroCompletedCount = _pomodoroCompletedCount,
            IsPomodoroEnabled = _pomodoroEnabled,
            UnlockPassword = _unlockPassword ?? string.Empty
        };
    }

    private async Task<PasswordDifficulty> LoadPasswordDifficultyAsync()
    {
        var value = await _settingsRepository.GetAsync(SettingsKeys.PasswordDifficulty);
        if (value is not null && Enum.TryParse<PasswordDifficulty>(value, true, out var difficulty))
            return difficulty;
        return PasswordDifficulty.Medium;
    }

    private async Task<int> LoadPasswordLengthAsync()
    {
        var value = await _settingsRepository.GetAsync(SettingsKeys.PasswordLength);
        if (value is not null && int.TryParse(value, out var length) && length > 0)
            return length;
        return 30;
    }

    private async Task LoadPomodoroSettingsAsync()
    {
        var workMin = await _settingsRepository.GetAsync(SettingsKeys.PomodoroWorkMinutes);
        if (workMin is not null && int.TryParse(workMin, out var w) && w > 0)
            _pomodoroWorkMinutes = w;

        var shortBreak = await _settingsRepository.GetAsync(SettingsKeys.PomodoroShortBreakMinutes);
        if (shortBreak is not null && int.TryParse(shortBreak, out var sb) && sb > 0)
            _pomodoroShortBreakMinutes = sb;

        var longBreak = await _settingsRepository.GetAsync(SettingsKeys.PomodoroLongBreakMinutes);
        if (longBreak is not null && int.TryParse(longBreak, out var lb) && lb > 0)
            _pomodoroLongBreakMinutes = lb;

        var interval = await _settingsRepository.GetAsync(SettingsKeys.PomodoroLongBreakInterval);
        if (interval is not null && int.TryParse(interval, out var i) && i > 0)
            _pomodoroLongBreakInterval = i;
    }

    public void Dispose()
    {
        StopTimer();
        _lock.Dispose();
    }
}
