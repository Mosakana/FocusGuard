# FocusGuard Phase 2: Focus Session & Timer — Implementation Plan

## Context

Phase 1 (Core Foundation) is complete: solution scaffolding, EF Core data layer with `ProfileEntity`, hosts-file website blocker, WMI/polling application blocker, WPF shell with sidebar navigation, dashboard, profiles CRUD, and `BlockingOrchestrator`. All unit tests pass.

Phase 2 adds focus session lifecycle, random-text password protection, Pomodoro timer, system tray integration, and a floating timer overlay — per `requirements.md` §7 Phase 2.

**Prerequisite:** Phase 1 code builds and tests pass (`dotnet build && dotnet test`).

---

## Step 1: Database Schema Evolution

Phase 1 used `EnsureCreated()` which does not apply migrations to an existing database. We add new tables using raw SQL `CREATE TABLE IF NOT EXISTS` executed at startup, avoiding the need for a full EF Migrations setup yet.

### New Entities

**`src/FocusGuard.Core/Data/Entities/FocusSessionEntity.cs`**
```csharp
namespace FocusGuard.Core.Data.Entities;

public class FocusSessionEntity
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int PlannedDurationMinutes { get; set; }
    public int ActualDurationMinutes { get; set; }
    public int PomodoroCompletedCount { get; set; }
    public bool WasUnlockedEarly { get; set; }
    public string State { get; set; } = "Ended"; // Idle, Working, ShortBreak, LongBreak, Ended
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**`src/FocusGuard.Core/Data/Entities/SettingEntity.cs`**
```csharp
namespace FocusGuard.Core.Data.Entities;

public class SettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
```

### New Repositories

**`src/FocusGuard.Core/Data/Repositories/IFocusSessionRepository.cs`**
```csharp
namespace FocusGuard.Core.Data.Repositories;

public interface IFocusSessionRepository
{
    Task<FocusSessionEntity> CreateAsync(FocusSessionEntity session);
    Task UpdateAsync(FocusSessionEntity session);
    Task<FocusSessionEntity?> GetByIdAsync(Guid id);
    Task<FocusSessionEntity?> GetActiveSessionAsync();
    Task<List<FocusSessionEntity>> GetRecentAsync(int count = 10);
}
```

**`src/FocusGuard.Core/Data/Repositories/FocusSessionRepository.cs`** — Implementation using `IDbContextFactory<FocusGuardDbContext>` (same pattern as `ProfileRepository`).

**`src/FocusGuard.Core/Data/Repositories/ISettingsRepository.cs`**
```csharp
namespace FocusGuard.Core.Data.Repositories;

public interface ISettingsRepository
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task<bool> ExistsAsync(string key);
}
```

**`src/FocusGuard.Core/Data/Repositories/SettingsRepository.cs`** — Implementation using `IDbContextFactory<FocusGuardDbContext>`.

### DbContext Changes

**Modify `src/FocusGuard.Core/Data/FocusGuardDbContext.cs`:**
- Add `DbSet<FocusSessionEntity> FocusSessions`
- Add `DbSet<SettingEntity> Settings`
- Configure `SettingEntity` with `HasKey(e => e.Key)` and `HasMaxLength(100)` for Key, `HasMaxLength(2000)` for Value
- Configure `FocusSessionEntity` with `HasKey(e => e.Id)` and an index on `State`

### Migration Strategy

**`src/FocusGuard.Core/Data/DatabaseMigrator.cs`**
```csharp
namespace FocusGuard.Core.Data;

public class DatabaseMigrator
{
    private readonly IDbContextFactory<FocusGuardDbContext> _contextFactory;
    private readonly ILogger<DatabaseMigrator> _logger;

    public async Task MigrateAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        // Phase 1 tables already exist via EnsureCreated()
        // Phase 2: Add new tables if they don't exist
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS FocusSessions (
                Id TEXT PRIMARY KEY,
                ProfileId TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT,
                PlannedDurationMinutes INTEGER NOT NULL DEFAULT 0,
                ActualDurationMinutes INTEGER NOT NULL DEFAULT 0,
                PomodoroCompletedCount INTEGER NOT NULL DEFAULT 0,
                WasUnlockedEarly INTEGER NOT NULL DEFAULT 0,
                State TEXT NOT NULL DEFAULT 'Ended',
                CreatedAt TEXT NOT NULL
            );
        ");

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL DEFAULT ''
            );
        ");

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE INDEX IF NOT EXISTS IX_FocusSessions_State
            ON FocusSessions (State);
        ");
    }
}
```

### DI Registration

**Modify `src/FocusGuard.Core/ServiceCollectionExtensions.cs`:**
- Add `services.AddScoped<IFocusSessionRepository, FocusSessionRepository>();`
- Add `services.AddScoped<ISettingsRepository, SettingsRepository>();`
- Add `services.AddSingleton<DatabaseMigrator>();`

### Startup Change

**Modify `src/FocusGuard.App/App.xaml.cs`:**
After `EnsureCreatedAsync()`, call:
```csharp
var migrator = scope.ServiceProvider.GetRequiredService<DatabaseMigrator>();
await migrator.MigrateAsync();
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Data/Entities/FocusSessionEntity.cs` | Create |
| `src/FocusGuard.Core/Data/Entities/SettingEntity.cs` | Create |
| `src/FocusGuard.Core/Data/Repositories/IFocusSessionRepository.cs` | Create |
| `src/FocusGuard.Core/Data/Repositories/FocusSessionRepository.cs` | Create |
| `src/FocusGuard.Core/Data/Repositories/ISettingsRepository.cs` | Create |
| `src/FocusGuard.Core/Data/Repositories/SettingsRepository.cs` | Create |
| `src/FocusGuard.Core/Data/DatabaseMigrator.cs` | Create |
| `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` | Modify |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify |
| `src/FocusGuard.App/App.xaml.cs` | Modify |

**Tests:** `tests/FocusGuard.Core.Tests/Data/FocusSessionRepositoryTests.cs` — CRUD, active session retrieval, recent sessions query (using InMemory provider).

**Verify:** `dotnet build && dotnet test` passes. App launches, new tables exist in `focusguard.db`.

---

## Step 2: Security Layer — Password Generation & Master Key

### Password Generator

**`src/FocusGuard.Core/Security/PasswordDifficulty.cs`**
```csharp
namespace FocusGuard.Core.Security;

public enum PasswordDifficulty
{
    Easy,   // Lowercase letters only: abcdefghij
    Medium, // Mixed case + digits: aB3kF9mQ2x
    Hard    // Mixed case + digits + specials: aB3$kF9@mQ
}
```

**`src/FocusGuard.Core/Security/PasswordGenerator.cs`**
```csharp
namespace FocusGuard.Core.Security;

public class PasswordGenerator
{
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitChars = "0123456789";
    private const string SpecialChars = "!@#$%&*?";

    /// <summary>
    /// Generates a cryptographically random password string.
    /// Uses System.Security.Cryptography.RandomNumberGenerator.
    /// </summary>
    public string Generate(int length, PasswordDifficulty difficulty)
    {
        // Easy: LowercaseChars
        // Medium: LowercaseChars + UppercaseChars + DigitChars
        // Hard: LowercaseChars + UppercaseChars + DigitChars + SpecialChars
        // Ensure at least one character from each required group
    }
}
```

### Password Validator

**`src/FocusGuard.Core/Security/PasswordValidator.cs`**
```csharp
namespace FocusGuard.Core.Security;

public class PasswordValidator
{
    /// <summary>
    /// Validates that the user-typed input exactly matches the generated password.
    /// </summary>
    public bool Validate(string expected, string actual)
    {
        return string.Equals(expected, actual, StringComparison.Ordinal);
    }
}
```

### Master Key Service

**`src/FocusGuard.Core/Security/MasterKeyService.cs`**
```csharp
namespace FocusGuard.Core.Security;

public class MasterKeyService
{
    private readonly ISettingsRepository _settings;

    /// <summary>
    /// Generates a new master recovery key (32 hex chars), hashes it (SHA-256 + salt),
    /// stores the hash in Settings. Returns the plaintext key (shown once to user).
    /// </summary>
    public async Task<string> GenerateMasterKeyAsync()

    /// <summary>
    /// Validates a user-provided master key against the stored hash.
    /// </summary>
    public async Task<bool> ValidateMasterKeyAsync(string key)

    /// <summary>
    /// Returns true if a master key has been set up.
    /// </summary>
    public async Task<bool> IsSetupCompleteAsync()
}
```

Storage uses `SettingsKeys.MasterKeyHash` and `SettingsKeys.MasterKeySalt`.

### Settings Keys Constants

**`src/FocusGuard.Core/Security/SettingsKeys.cs`**
```csharp
namespace FocusGuard.Core.Security;

public static class SettingsKeys
{
    public const string MasterKeyHash = "security.master_key_hash";
    public const string MasterKeySalt = "security.master_key_salt";
    public const string PasswordDifficulty = "session.password_difficulty";
    public const string PasswordLength = "session.password_length";
    public const string DefaultSessionDuration = "session.default_duration_minutes";
    public const string PomodoroWorkMinutes = "pomodoro.work_minutes";
    public const string PomodoroShortBreakMinutes = "pomodoro.short_break_minutes";
    public const string PomodoroLongBreakMinutes = "pomodoro.long_break_minutes";
    public const string PomodoroLongBreakInterval = "pomodoro.long_break_interval";
    public const string PomodoroAutoStart = "pomodoro.auto_start";
    public const string SoundEnabled = "notifications.sound_enabled";
    public const string MinimizeToTray = "app.minimize_to_tray";
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Security/PasswordDifficulty.cs` | Create |
| `src/FocusGuard.Core/Security/PasswordGenerator.cs` | Create |
| `src/FocusGuard.Core/Security/PasswordValidator.cs` | Create |
| `src/FocusGuard.Core/Security/MasterKeyService.cs` | Create |
| `src/FocusGuard.Core/Security/SettingsKeys.cs` | Create |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register `PasswordGenerator`, `PasswordValidator`, `MasterKeyService` |

**Tests:**
- `tests/FocusGuard.Core.Tests/Security/PasswordGeneratorTests.cs` — Correct length, character sets per difficulty, cryptographic randomness (no two identical outputs in 100 generations)
- `tests/FocusGuard.Core.Tests/Security/PasswordValidatorTests.cs` — Exact match required, case sensitivity, empty strings
- `tests/FocusGuard.Core.Tests/Security/MasterKeyServiceTests.cs` — Generate stores hash+salt, validate succeeds with correct key, validate fails with wrong key (using mocked ISettingsRepository)

**Verify:** `dotnet test` passes. Password generation produces correct character sets.

---

## Step 3: Focus Session State Machine

The session manager tracks the lifecycle: Idle → Working → ShortBreak/LongBreak → Working → ... → Ended.

### State Enum

**`src/FocusGuard.Core/Sessions/FocusSessionState.cs`**
```csharp
namespace FocusGuard.Core.Sessions;

public enum FocusSessionState
{
    Idle,
    Working,
    ShortBreak,
    LongBreak,
    Ended
}
```

### Session Info Snapshot

**`src/FocusGuard.Core/Sessions/FocusSessionInfo.cs`**
```csharp
namespace FocusGuard.Core.Sessions;

public class FocusSessionInfo
{
    public Guid SessionId { get; init; }
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public FocusSessionState State { get; init; }
    public DateTime StartTime { get; init; }
    public TimeSpan Elapsed { get; init; }
    public TimeSpan TotalPlanned { get; init; }
    public TimeSpan CurrentIntervalRemaining { get; init; }
    public int PomodoroCompletedCount { get; init; }
    public bool IsPomodoroEnabled { get; init; }
    public string? UnlockPassword { get; init; } // Only populated when unlock is requested
}
```

### Interface

**`src/FocusGuard.Core/Sessions/IFocusSessionManager.cs`**
```csharp
namespace FocusGuard.Core.Sessions;

public interface IFocusSessionManager
{
    FocusSessionState CurrentState { get; }
    FocusSessionInfo? CurrentSession { get; }

    /// <summary>
    /// Start a new focus session with the given profile and duration.
    /// Optionally enable Pomodoro mode.
    /// </summary>
    Task StartSessionAsync(Guid profileId, int durationMinutes, bool enablePomodoro);

    /// <summary>
    /// Attempt to end the session early by validating the unlock password.
    /// Returns true if unlocked successfully.
    /// </summary>
    Task<bool> TryUnlockAsync(string typedPassword);

    /// <summary>
    /// Emergency unlock using the master recovery key.
    /// </summary>
    Task<bool> EmergencyUnlockAsync(string masterKey);

    /// <summary>
    /// Called when the session timer naturally expires.
    /// </summary>
    Task EndSessionNaturallyAsync();

    /// <summary>
    /// Request the unlock password to be revealed (for the unlock dialog).
    /// </summary>
    string? GetUnlockPassword();

    /// <summary>
    /// Advance to the next Pomodoro interval (Work → Break → Work → ...).
    /// Called by the Pomodoro timer.
    /// </summary>
    void AdvancePomodoroInterval();

    event EventHandler<FocusSessionState>? StateChanged;
    event EventHandler? SessionEnded;
    event EventHandler<string>? PomodoroIntervalChanged; // "Work", "Short Break", "Long Break"
}
```

### Implementation

**`src/FocusGuard.Core/Sessions/FocusSessionManager.cs`**
```csharp
namespace FocusGuard.Core.Sessions;

public class FocusSessionManager : IFocusSessionManager
{
    private readonly IFocusSessionRepository _sessionRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly PasswordGenerator _passwordGenerator;
    private readonly PasswordValidator _passwordValidator;
    private readonly MasterKeyService _masterKeyService;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<FocusSessionManager> _logger;

    private FocusSessionEntity? _activeEntity;
    private string? _unlockPassword;
    private string _activeProfileName = string.Empty;
    private PasswordDifficulty _difficulty = PasswordDifficulty.Medium;
    private int _passwordLength = 30;
    private int _pomodoroCompletedCount;
    private bool _pomodoroEnabled;
    private System.Timers.Timer? _sessionTimer;

    // State machine transitions:
    // StartSessionAsync: Idle → Working (generates password, persists entity)
    // AdvancePomodoroInterval: Working → ShortBreak/LongBreak → Working (cycles)
    // TryUnlockAsync: Working/ShortBreak/LongBreak → Ended (if password matches)
    // EmergencyUnlockAsync: Any → Ended (if master key valid)
    // EndSessionNaturallyAsync: Any → Ended (timer expired)
    // SessionTimer_Elapsed: auto-calls EndSessionNaturallyAsync when total duration reached
}
```

Key behaviors:
- On `StartSessionAsync`: loads password settings from `ISettingsRepository` (defaults: Medium, 30 chars), generates password, creates `FocusSessionEntity` with State="Working", starts session timer
- Session timer: `System.Timers.Timer` set to total duration. On elapsed → `EndSessionNaturallyAsync()`
- On end (any path): updates entity with `EndTime`, `ActualDurationMinutes`, `WasUnlockedEarly`, `State="Ended"`, saves to DB, raises events

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Sessions/FocusSessionState.cs` | Create |
| `src/FocusGuard.Core/Sessions/FocusSessionInfo.cs` | Create |
| `src/FocusGuard.Core/Sessions/IFocusSessionManager.cs` | Create |
| `src/FocusGuard.Core/Sessions/FocusSessionManager.cs` | Create |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register `IFocusSessionManager` as Singleton |

**Tests:** `tests/FocusGuard.Core.Tests/Sessions/FocusSessionManagerTests.cs` — State transitions, password generation on start, unlock with correct/incorrect password, emergency unlock, session persistence, event firing (using mocked dependencies).

**Verify:** `dotnet test` passes. State machine transitions are correct.

---

## Step 4: BlockingOrchestrator Integration

Extend `BlockingOrchestrator` so that `IFocusSessionManager` drives activation/deactivation.

**Modify `src/FocusGuard.App/Services/BlockingOrchestrator.cs`:**
```csharp
public class BlockingOrchestrator
{
    // Existing fields...
    private readonly IFocusSessionManager _sessionManager;

    // Constructor: add IFocusSessionManager parameter
    // Subscribe to _sessionManager.StateChanged:
    //   Working → ActivateProfileAsync(session.ProfileId)
    //   Ended → DeactivateAsync()
    // Subscribe to _sessionManager.SessionEnded:
    //   DeactivateAsync()

    // New property:
    public IFocusSessionManager SessionManager => _sessionManager;
}
```

When a focus session starts (state → Working), the orchestrator automatically activates blocking for the session's profile. When the session ends (state → Ended), blocking is deactivated.

During break intervals (ShortBreak/LongBreak), blocking remains active — users should still be blocked during breaks.

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/BlockingOrchestrator.cs` | Modify |

**Verify:** Start a focus session → blocking activates. End session → blocking deactivates.

---

## Step 5: Pomodoro Timer Engine

### Configuration

**`src/FocusGuard.Core/Sessions/PomodoroConfiguration.cs`**
```csharp
namespace FocusGuard.Core.Sessions;

public class PomodoroConfiguration
{
    public int WorkMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int LongBreakInterval { get; set; } = 4; // Long break every N work sessions
    public bool AutoStartNext { get; set; } = false;
}
```

### Interval Types

**`src/FocusGuard.Core/Sessions/PomodoroInterval.cs`**
```csharp
namespace FocusGuard.Core.Sessions;

public class PomodoroInterval
{
    public FocusSessionState Type { get; init; } // Working, ShortBreak, or LongBreak
    public int DurationMinutes { get; init; }
    public int SequenceNumber { get; init; } // 1-based position in the cycle
}
```

### Interval Calculator

**`src/FocusGuard.Core/Sessions/PomodoroIntervalCalculator.cs`**
```csharp
namespace FocusGuard.Core.Sessions;

public class PomodoroIntervalCalculator
{
    /// <summary>
    /// Calculates the sequence of Pomodoro intervals for a given total duration.
    /// Pattern: Work → Short Break → Work → Short Break → ... → Work → Long Break → repeat
    /// Long break occurs after every LongBreakInterval work sessions.
    /// </summary>
    public List<PomodoroInterval> CalculateIntervals(
        PomodoroConfiguration config, int totalDurationMinutes)

    /// <summary>
    /// Given the number of completed work sessions, returns the next interval type.
    /// </summary>
    public PomodoroInterval GetNextInterval(
        PomodoroConfiguration config, int completedWorkSessions)
}
```

### Pomodoro Timer

**`src/FocusGuard.Core/Sessions/PomodoroTimer.cs`**
```csharp
namespace FocusGuard.Core.Sessions;

public class PomodoroTimer : IDisposable
{
    private readonly IFocusSessionManager _sessionManager;
    private readonly PomodoroIntervalCalculator _calculator;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<PomodoroTimer> _logger;

    private PomodoroConfiguration _config;
    private System.Timers.Timer? _intervalTimer;
    private PomodoroInterval? _currentInterval;
    private DateTime _intervalStartTime;
    private int _completedWorkSessions;

    public PomodoroInterval? CurrentInterval => _currentInterval;
    public TimeSpan IntervalRemaining { get; private set; }
    public int CompletedWorkSessions => _completedWorkSessions;

    public event EventHandler<PomodoroInterval>? IntervalStarted;
    public event EventHandler<PomodoroInterval>? IntervalCompleted;
    public event EventHandler? TimerTick; // Fires every second for UI update

    /// <summary>Load config from settings repository, start first Work interval.</summary>
    public async Task StartAsync()

    /// <summary>Stop timer, reset state.</summary>
    public void Stop()

    /// <summary>Called every 1 second to update IntervalRemaining and fire TimerTick.</summary>
    private void OnTick(object? sender, System.Timers.ElapsedEventArgs e)

    /// <summary>Current interval completed — advance to next and notify session manager.</summary>
    private void OnIntervalCompleted()
}
```

The `PomodoroTimer` runs a 1-second tick timer. Each tick updates `IntervalRemaining` and fires `TimerTick`. When an interval completes, it fires `IntervalCompleted`, calls `_sessionManager.AdvancePomodoroInterval()`, and starts the next interval.

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Sessions/PomodoroConfiguration.cs` | Create |
| `src/FocusGuard.Core/Sessions/PomodoroInterval.cs` | Create |
| `src/FocusGuard.Core/Sessions/PomodoroIntervalCalculator.cs` | Create |
| `src/FocusGuard.Core/Sessions/PomodoroTimer.cs` | Create |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register `PomodoroIntervalCalculator`, `PomodoroTimer` as Singleton |

**Tests:** `tests/FocusGuard.Core.Tests/Sessions/PomodoroIntervalCalculatorTests.cs` — Correct interval sequence, long break placement, total duration fits within session time.

**Verify:** `dotnet test` passes.

---

## Step 6: Dashboard UI Rework

Replace the minimal Phase 1 dashboard with a fully functional focus session control center.

### Start Session Dialog

**`src/FocusGuard.App/Views/StartSessionDialog.xaml` + `.xaml.cs`**

A modal dialog shown when user clicks "Start Focus Session" on a profile card:
- Profile name + color indicator (read-only)
- Duration picker: preset buttons (25m, 45m, 60m, 90m, 120m) + custom TextBox
- Pomodoro toggle: CheckBox "Enable Pomodoro Mode"
- Password difficulty: ComboBox (Easy / Medium / Hard)
- "Start Session" primary button, "Cancel" secondary button

**`src/FocusGuard.App/ViewModels/StartSessionDialogViewModel.cs`**
```csharp
namespace FocusGuard.App.ViewModels;

public partial class StartSessionDialogViewModel : ObservableObject
{
    [ObservableProperty] private string _profileName = string.Empty;
    [ObservableProperty] private string _profileColor = "#4A90D9";
    [ObservableProperty] private int _durationMinutes = 25;
    [ObservableProperty] private bool _enablePomodoro = true;
    [ObservableProperty] private PasswordDifficulty _selectedDifficulty = PasswordDifficulty.Medium;

    public Guid ProfileId { get; set; }
    public bool Confirmed { get; set; }

    [RelayCommand] private void SetDuration(int minutes) => DurationMinutes = minutes;
    [RelayCommand] private void Confirm() { Confirmed = true; /* Close dialog */ }
    [RelayCommand] private void Cancel() { Confirmed = false; /* Close dialog */ }
}
```

### Unlock Dialog

**`src/FocusGuard.App/Views/UnlockDialog.xaml` + `.xaml.cs`**

A modal dialog for ending a session early:
- Shows the generated password string (revealed on button click)
- TextBox for typing the password (**paste disabled** via `CommandManager.AddPreviewCanExecuteHandler` on `ApplicationCommands.Paste`)
- Live character counter: "12 / 30 characters"
- "Unlock" button (enabled only when character count matches)
- "Emergency Unlock" expander with master key TextBox
- Visual feedback: green border when correct, red when wrong

Paste prevention in code-behind:
```csharp
private void PasswordInput_Loaded(object sender, RoutedEventArgs e)
{
    DataObject.AddPastingHandler(PasswordInput, OnPaste);
}

private void OnPaste(object sender, DataObjectPastingEventArgs e)
{
    e.CancelCommand(); // Block all paste attempts
}
```

**`src/FocusGuard.App/ViewModels/UnlockDialogViewModel.cs`**
```csharp
namespace FocusGuard.App.ViewModels;

public partial class UnlockDialogViewModel : ObservableObject
{
    [ObservableProperty] private string _generatedPassword = string.Empty;
    [ObservableProperty] private string _typedPassword = string.Empty;
    [ObservableProperty] private bool _isPasswordRevealed;
    [ObservableProperty] private bool _isCorrect;
    [ObservableProperty] private string _masterKeyInput = string.Empty;

    public bool UnlockSucceeded { get; set; }
    public bool EmergencyUnlockUsed { get; set; }

    public int PasswordLength => GeneratedPassword.Length;
    public int TypedLength => TypedPassword?.Length ?? 0;

    [RelayCommand] private void RevealPassword() => IsPasswordRevealed = true;
    [RelayCommand] private async Task TryUnlock() { /* Validate via IFocusSessionManager */ }
    [RelayCommand] private async Task TryEmergencyUnlock() { /* Validate via MasterKeyService */ }

    partial void OnTypedPasswordChanged(string value)
    {
        IsCorrect = string.Equals(value, GeneratedPassword, StringComparison.Ordinal);
        OnPropertyChanged(nameof(TypedLength));
    }
}
```

### Dashboard View Rework

**Modify `src/FocusGuard.App/ViewModels/DashboardViewModel.cs`:**
```csharp
public partial class DashboardViewModel : ViewModelBase
{
    // Existing fields...
    private readonly IFocusSessionManager _sessionManager;
    private readonly PomodoroTimer _pomodoroTimer;

    // New observable properties:
    [ObservableProperty] private string _timerDisplay = "00:00"; // MM:SS or HH:MM:SS
    [ObservableProperty] private double _timerProgress; // 0.0 to 1.0 for progress ring
    [ObservableProperty] private string _currentIntervalLabel = string.Empty; // "Work", "Short Break", etc.
    [ObservableProperty] private bool _isSessionActive;
    [ObservableProperty] private int _pomodoroCount;

    // New commands:
    [RelayCommand] private async Task StartSession(Guid profileId) { /* Show StartSessionDialog */ }
    [RelayCommand] private async Task EndSessionEarly() { /* Show UnlockDialog */ }

    // Subscribe to PomodoroTimer.TimerTick to update TimerDisplay every second
    // Subscribe to IFocusSessionManager.StateChanged to update UI state
}
```

**Modify `src/FocusGuard.App/Views/DashboardView.xaml`:**
- Replace status card with timer card:
  - Large timer display (48px font, monospace)
  - Circular progress ring (using `Arc` geometry or `Ellipse` with `StrokeDashArray`)
  - Current interval label ("Work Session", "Short Break", "Long Break")
  - Pomodoro count indicators (filled/unfilled dots)
  - "End Session Early" button (danger style, visible only when session active)
- Profile cards gain a "Start Focus Session" button (each card is clickable)
- Remove "Coming Soon" placeholders for Pomodoro Timer (now implemented)

### Progress Ring Control

**`src/FocusGuard.App/Controls/CircularProgressRing.cs`**
```csharp
namespace FocusGuard.App.Controls;

public class CircularProgressRing : Control
{
    public static readonly DependencyProperty ProgressProperty; // 0.0 to 1.0
    public static readonly DependencyProperty StrokeThicknessProperty; // default 6
    public static readonly DependencyProperty ProgressColorProperty; // default PrimaryBrush
    public static readonly DependencyProperty TrackColorProperty; // default SurfaceLightBrush

    protected override void OnRender(DrawingContext dc)
    {
        // Draw background circle (track)
        // Draw arc from 12 o'clock position, Progress * 360 degrees
    }
}
```

### Dialog Service Extension

**Modify `src/FocusGuard.App/Services/IDialogService.cs`:**
```csharp
// Add new methods:
Task<StartSessionDialogResult?> ShowStartSessionDialogAsync(Guid profileId, string profileName, string profileColor);
Task<UnlockDialogResult?> ShowUnlockDialogAsync(string generatedPassword);
```

**Modify `src/FocusGuard.App/Services/DialogService.cs`:** — Implement new dialog methods.

**`src/FocusGuard.App/Models/StartSessionDialogResult.cs`**
```csharp
namespace FocusGuard.App.Models;

public class StartSessionDialogResult
{
    public int DurationMinutes { get; init; }
    public bool EnablePomodoro { get; init; }
    public PasswordDifficulty Difficulty { get; init; }
}
```

**`src/FocusGuard.App/Models/UnlockDialogResult.cs`**
```csharp
namespace FocusGuard.App.Models;

public class UnlockDialogResult
{
    public bool Unlocked { get; init; }
    public bool EmergencyUsed { get; init; }
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/StartSessionDialog.xaml` | Create |
| `src/FocusGuard.App/Views/StartSessionDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/StartSessionDialogViewModel.cs` | Create |
| `src/FocusGuard.App/Views/UnlockDialog.xaml` | Create |
| `src/FocusGuard.App/Views/UnlockDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/UnlockDialogViewModel.cs` | Create |
| `src/FocusGuard.App/Controls/CircularProgressRing.cs` | Create |
| `src/FocusGuard.App/Models/StartSessionDialogResult.cs` | Create |
| `src/FocusGuard.App/Models/UnlockDialogResult.cs` | Create |
| `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | Modify |
| `src/FocusGuard.App/Views/DashboardView.xaml` | Modify |
| `src/FocusGuard.App/Services/IDialogService.cs` | Modify |
| `src/FocusGuard.App/Services/DialogService.cs` | Modify |

**Verify:** Dashboard shows timer card. Click profile → start dialog opens. Start session → timer counts down. End early → unlock dialog with paste-disabled TextBox.

---

## Step 7: System Tray Integration

### NuGet / Project Changes

**Modify `src/FocusGuard.App/FocusGuard.App.csproj`:**
```xml
<UseWindowsForms>true</UseWindowsForms>
```

This enables `System.Windows.Forms.NotifyIcon` in the WPF project.

### Tray Icon Service

**`src/FocusGuard.App/Services/TrayIconService.cs`**
```csharp
namespace FocusGuard.App.Services;

public class TrayIconService : IDisposable
{
    private readonly IFocusSessionManager _sessionManager;
    private readonly PomodoroTimer _pomodoroTimer;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Windows.Forms.ContextMenuStrip? _contextMenu;

    public void Initialize()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadAppIcon(), // Embedded resource or generated
            Text = "FocusGuard — Idle",
            Visible = true
        };

        _contextMenu = new System.Windows.Forms.ContextMenuStrip();
        _contextMenu.Items.Add("Open FocusGuard", null, OnOpenClick);
        _contextMenu.Items.Add("Quick Start Session...", null, OnQuickStartClick);
        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, OnExitClick);
        _notifyIcon.ContextMenuStrip = _contextMenu;

        _notifyIcon.DoubleClick += OnOpenClick;

        // Subscribe to session state changes to update icon tooltip
        _sessionManager.StateChanged += OnStateChanged;
        _pomodoroTimer.TimerTick += OnTimerTick;
    }

    private void OnStateChanged(object? sender, FocusSessionState state)
    {
        // Update tooltip: "FocusGuard — Working (23:45 remaining)"
        // Update context menu: show "End Session" when active, hide when idle
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        // Update tooltip with remaining time
        if (_notifyIcon != null)
            _notifyIcon.Text = $"FocusGuard — {FormatRemaining()}";
    }

    /// <summary>Show balloon notification for Pomodoro transitions.</summary>
    public void ShowBalloonTip(string title, string message, System.Windows.Forms.ToolTipIcon icon)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, message, icon);
    }

    public void Dispose() { _notifyIcon?.Dispose(); _contextMenu?.Dispose(); }
}
```

### Minimize to Tray Behavior

**Modify `src/FocusGuard.App/Views/MainWindow.xaml.cs`:**
```csharp
protected override void OnClosing(CancelEventArgs e)
{
    // If session is active or minimize-to-tray is enabled:
    // Cancel the close, hide the window instead
    if (_sessionManager.CurrentState != FocusSessionState.Idle)
    {
        e.Cancel = true;
        this.Hide();
        return;
    }
    base.OnClosing(e);
}

protected override void OnStateChanged(EventArgs e)
{
    // When minimized and minimize-to-tray enabled: hide window
    if (WindowState == WindowState.Minimized)
    {
        this.Hide();
    }
    base.OnStateChanged(e);
}

public void RestoreFromTray()
{
    this.Show();
    this.WindowState = WindowState.Normal;
    this.Activate();
}
```

### App Icon

**`src/FocusGuard.App/Resources/AppIcon.ico`** — Simple shield/focus icon (can be generated as a 16x16/32x32/48x48 multi-size ICO). For initial implementation, use a placeholder icon from `SystemIcons.Shield` or embed a simple `.ico` file.

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/TrayIconService.cs` | Create |
| `src/FocusGuard.App/Resources/AppIcon.ico` | Create (placeholder) |
| `src/FocusGuard.App/FocusGuard.App.csproj` | Modify — add `<UseWindowsForms>true</UseWindowsForms>` |
| `src/FocusGuard.App/Views/MainWindow.xaml.cs` | Modify — minimize-to-tray |
| `src/FocusGuard.App/App.xaml.cs` | Modify — initialize `TrayIconService`, register in DI |

**Verify:** Tray icon appears on launch. Tooltip shows status. Right-click opens context menu. Minimize hides to tray. Double-click restores. Closing during active session minimizes instead of exiting.

---

## Step 8: Timer Overlay Window

A small, always-on-top, draggable window showing the countdown timer.

**`src/FocusGuard.App/Views/TimerOverlayWindow.xaml`**
```xml
<Window x:Class="FocusGuard.App.Views.TimerOverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        Title="FocusGuard Timer"
        Width="180" Height="180"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False"
        ResizeMode="NoResize"
        MouseLeftButtonDown="Window_MouseLeftButtonDown">
    <Border CornerRadius="90" Background="#CC1E1E2E" BorderBrush="#4A90D9" BorderThickness="2">
        <Grid>
            <!-- CircularProgressRing (fills the border area) -->
            <local:CircularProgressRing Progress="{Binding TimerProgress}"
                                        StrokeThickness="6" Width="160" Height="160" />
            <!-- Center content -->
            <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
                <TextBlock Text="{Binding TimerDisplay}"
                           FontSize="28" FontWeight="Bold" FontFamily="Consolas"
                           Foreground="White" HorizontalAlignment="Center" />
                <TextBlock Text="{Binding IntervalLabel}"
                           FontSize="11" Foreground="#8890A0"
                           HorizontalAlignment="Center" Margin="0,4,0,0" />
                <TextBlock Text="{Binding PomodoroCountDisplay}"
                           FontSize="10" Foreground="#4A90D9"
                           HorizontalAlignment="Center" Margin="0,2,0,0" />
            </StackPanel>
        </Grid>
    </Border>
</Window>
```

**`src/FocusGuard.App/Views/TimerOverlayWindow.xaml.cs`**
```csharp
public partial class TimerOverlayWindow : Window
{
    public TimerOverlayWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove(); // Makes the window draggable
    }
}
```

**`src/FocusGuard.App/ViewModels/TimerOverlayViewModel.cs`**
```csharp
namespace FocusGuard.App.ViewModels;

public partial class TimerOverlayViewModel : ObservableObject
{
    private readonly IFocusSessionManager _sessionManager;
    private readonly PomodoroTimer _pomodoroTimer;

    [ObservableProperty] private string _timerDisplay = "00:00";
    [ObservableProperty] private double _timerProgress;
    [ObservableProperty] private string _intervalLabel = string.Empty;
    [ObservableProperty] private string _pomodoroCountDisplay = string.Empty;

    // Subscribe to PomodoroTimer.TimerTick, update properties
    // Visible only when a session is active
}
```

### Overlay Lifecycle

The overlay is shown/hidden by `BlockingOrchestrator` or `DashboardViewModel`:
- Session starts → create and show `TimerOverlayWindow`
- Session ends → close the overlay
- The overlay window positions itself at the bottom-right of the primary screen by default, remembering position if dragged

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/TimerOverlayWindow.xaml` | Create |
| `src/FocusGuard.App/Views/TimerOverlayWindow.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/TimerOverlayViewModel.cs` | Create |
| `src/FocusGuard.App/App.xaml.cs` | Modify — register `TimerOverlayViewModel` as Transient |

**Verify:** Start session → overlay appears, always-on-top. Timer counts down. Can drag overlay. Session ends → overlay disappears.

---

## Step 9: Master Key Setup Flow

On first launch (no master key in Settings), show a setup dialog before the main window.

**`src/FocusGuard.App/Views/MasterKeySetupDialog.xaml` + `.xaml.cs`**

A modal dialog:
- Explanation text: "FocusGuard uses a master recovery key for emergency unlocks. Save this key somewhere safe — it will only be shown once."
- Large monospace TextBlock displaying the generated key (copyable via button)
- Checkbox: "I have saved this key"
- "Continue" button (enabled only when checkbox is checked)

**`src/FocusGuard.App/ViewModels/MasterKeySetupViewModel.cs`**
```csharp
namespace FocusGuard.App.ViewModels;

public partial class MasterKeySetupViewModel : ObservableObject
{
    private readonly MasterKeyService _masterKeyService;

    [ObservableProperty] private string _masterKey = string.Empty;
    [ObservableProperty] private bool _hasSavedKey;

    public async Task GenerateKeyAsync()
    {
        MasterKey = await _masterKeyService.GenerateMasterKeyAsync();
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        Clipboard.SetText(MasterKey);
    }

    public bool CanContinue => HasSavedKey;
}
```

### Startup Integration

**Modify `src/FocusGuard.App/App.xaml.cs`:**
After migration, before showing MainWindow:
```csharp
var masterKeyService = Services.GetRequiredService<MasterKeyService>();
if (!await masterKeyService.IsSetupCompleteAsync())
{
    var setupDialog = new MasterKeySetupDialog();
    var vm = new MasterKeySetupViewModel(masterKeyService);
    await vm.GenerateKeyAsync();
    setupDialog.DataContext = vm;
    setupDialog.ShowDialog();
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/MasterKeySetupDialog.xaml` | Create |
| `src/FocusGuard.App/Views/MasterKeySetupDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/MasterKeySetupViewModel.cs` | Create |
| `src/FocusGuard.App/App.xaml.cs` | Modify — add master key setup check |

**Verify:** Delete `focusguard.db` → launch app → master key dialog appears. Save key → continue → main window. Relaunch → dialog does not reappear.

---

## Step 10: DI Wiring (All New Services)

Final DI registration pass to ensure everything is wired.

**Modify `src/FocusGuard.Core/ServiceCollectionExtensions.cs`:**
```csharp
public static IServiceCollection AddFocusGuardCore(this IServiceCollection services)
{
    // Database
    services.AddDbContextFactory<FocusGuardDbContext>(options =>
        options.UseSqlite($"Data Source={AppPaths.DatabasePath}"));

    // Migration
    services.AddSingleton<DatabaseMigrator>();

    // Repositories
    services.AddScoped<IProfileRepository, ProfileRepository>();
    services.AddScoped<IFocusSessionRepository, FocusSessionRepository>();
    services.AddScoped<ISettingsRepository, SettingsRepository>();

    // Blocking engines
    services.AddSingleton<IWebsiteBlocker, HostsFileWebsiteBlocker>();
    services.AddSingleton<IApplicationBlocker, ProcessApplicationBlocker>();

    // Security
    services.AddSingleton<PasswordGenerator>();
    services.AddSingleton<PasswordValidator>();
    services.AddSingleton<MasterKeyService>();

    // Sessions
    services.AddSingleton<IFocusSessionManager, FocusSessionManager>();
    services.AddSingleton<PomodoroIntervalCalculator>();
    services.AddSingleton<PomodoroTimer>();

    return services;
}
```

**Modify `src/FocusGuard.App/App.xaml.cs`:**
```csharp
.ConfigureServices((_, services) =>
{
    // Core services
    services.AddFocusGuardCore();

    // App services
    services.AddSingleton<INavigationService, NavigationService>();
    services.AddSingleton<IDialogService, DialogService>();
    services.AddSingleton<BlockingOrchestrator>();
    services.AddSingleton<TrayIconService>();

    // ViewModels
    services.AddTransient<DashboardViewModel>();
    services.AddTransient<ProfilesViewModel>();
    services.AddTransient<ProfileEditorViewModel>();
    services.AddTransient<TimerOverlayViewModel>();
    services.AddSingleton<MainWindowViewModel>();

    // Windows
    services.AddSingleton<MainWindow>();
})
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify (final) |
| `src/FocusGuard.App/App.xaml.cs` | Modify (final) |

**Verify:** `dotnet build` — no missing DI registrations. App launches without `InvalidOperationException`.

---

## Step 11: Notifications — Balloon Tips

Pomodoro transitions trigger system tray balloon notifications.

**Integrate in `BlockingOrchestrator` or create a dedicated notification coordinator:**

Subscribe to `PomodoroTimer.IntervalCompleted` and `IFocusSessionManager.StateChanged`:

```csharp
// In BlockingOrchestrator or a new NotificationService:
_pomodoroTimer.IntervalCompleted += (_, interval) =>
{
    var title = interval.Type switch
    {
        FocusSessionState.Working => "Back to Work!",
        FocusSessionState.ShortBreak => "Short Break",
        FocusSessionState.LongBreak => "Long Break — You earned it!",
        _ => "FocusGuard"
    };
    var message = $"Duration: {interval.DurationMinutes} minutes";
    _trayIconService.ShowBalloonTip(title, message, ToolTipIcon.Info);
};

_sessionManager.SessionEnded += (_, _) =>
{
    _trayIconService.ShowBalloonTip("Session Complete",
        "Great work! Your focus session has ended.", ToolTipIcon.Info);
};
```

This is wired directly via `TrayIconService` (already created in Step 7) — no new files needed, just event subscriptions added to existing classes.

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/TrayIconService.cs` | Modify — add notification event subscriptions |

**Verify:** Start Pomodoro session → work interval ends → balloon notification appears → break interval ends → notification → session ends → final notification.

---

## Step 12: Sound Alerts

Simple sound alerts on Pomodoro transitions using built-in system sounds.

**`src/FocusGuard.Core/Sessions/SoundAlertService.cs`**
```csharp
namespace FocusGuard.Core.Sessions;

public class SoundAlertService
{
    private readonly ISettingsRepository _settings;

    public async Task<bool> IsSoundEnabledAsync()
    {
        var value = await _settings.GetAsync(SettingsKeys.SoundEnabled);
        return value == null || value == "true"; // Default: enabled
    }

    public void PlayWorkStart()
    {
        if (IsSoundEnabledSync())
            System.Media.SystemSounds.Exclamation.Play();
    }

    public void PlayBreakStart()
    {
        if (IsSoundEnabledSync())
            System.Media.SystemSounds.Asterisk.Play();
    }

    public void PlaySessionEnd()
    {
        if (IsSoundEnabledSync())
            System.Media.SystemSounds.Hand.Play();
    }

    private bool IsSoundEnabledSync()
    {
        // Cache the setting to avoid async in hot path
        return _soundEnabled;
    }
}
```

Subscribe to `PomodoroTimer.IntervalStarted` to play appropriate sounds.

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Sessions/SoundAlertService.cs` | Create |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register `SoundAlertService` |
| `src/FocusGuard.App/Services/TrayIconService.cs` | Modify — wire sound alerts to Pomodoro events |

**Verify:** Enable sound → Pomodoro transitions play system sounds. Disable sound → silent.

---

## Step Dependency Graph

```
Step 1  (DB Schema)
  ↓
Step 2  (Security Layer)
  ↓
Step 3  (Session Manager)  ← depends on Steps 1 + 2
  ↓
Step 4  (BlockingOrchestrator)  ← depends on Step 3
  ↓
Step 5  (Pomodoro Timer)  ← depends on Step 3
  ↓
Step 6  (Dashboard UI)  ← depends on Steps 3 + 4 + 5
  ↓
Step 7  (System Tray)  ← depends on Steps 3 + 5
  ↓
Step 8  (Timer Overlay)  ← depends on Steps 5 + 6
  ↓
Step 9  (Master Key Setup)  ← depends on Step 2
  ↓
Step 10 (DI Wiring)  ← depends on all above
  ↓
Step 11 (Notifications)  ← depends on Steps 5 + 7
  ↓
Step 12 (Sound Alerts)  ← depends on Steps 5 + 11
```

Steps 1–5 are sequential (each builds on the prior). Steps 7 and 9 could be implemented in parallel after their prerequisites. Steps 10–12 are final integration.

---

## File Summary

### New Files (~44)

| # | File | Step |
|---|------|------|
| 1 | `src/FocusGuard.Core/Data/Entities/FocusSessionEntity.cs` | 1 |
| 2 | `src/FocusGuard.Core/Data/Entities/SettingEntity.cs` | 1 |
| 3 | `src/FocusGuard.Core/Data/Repositories/IFocusSessionRepository.cs` | 1 |
| 4 | `src/FocusGuard.Core/Data/Repositories/FocusSessionRepository.cs` | 1 |
| 5 | `src/FocusGuard.Core/Data/Repositories/ISettingsRepository.cs` | 1 |
| 6 | `src/FocusGuard.Core/Data/Repositories/SettingsRepository.cs` | 1 |
| 7 | `src/FocusGuard.Core/Data/DatabaseMigrator.cs` | 1 |
| 8 | `src/FocusGuard.Core/Security/PasswordDifficulty.cs` | 2 |
| 9 | `src/FocusGuard.Core/Security/PasswordGenerator.cs` | 2 |
| 10 | `src/FocusGuard.Core/Security/PasswordValidator.cs` | 2 |
| 11 | `src/FocusGuard.Core/Security/MasterKeyService.cs` | 2 |
| 12 | `src/FocusGuard.Core/Security/SettingsKeys.cs` | 2 |
| 13 | `src/FocusGuard.Core/Sessions/FocusSessionState.cs` | 3 |
| 14 | `src/FocusGuard.Core/Sessions/FocusSessionInfo.cs` | 3 |
| 15 | `src/FocusGuard.Core/Sessions/IFocusSessionManager.cs` | 3 |
| 16 | `src/FocusGuard.Core/Sessions/FocusSessionManager.cs` | 3 |
| 17 | `src/FocusGuard.Core/Sessions/PomodoroConfiguration.cs` | 5 |
| 18 | `src/FocusGuard.Core/Sessions/PomodoroInterval.cs` | 5 |
| 19 | `src/FocusGuard.Core/Sessions/PomodoroIntervalCalculator.cs` | 5 |
| 20 | `src/FocusGuard.Core/Sessions/PomodoroTimer.cs` | 5 |
| 21 | `src/FocusGuard.Core/Sessions/SoundAlertService.cs` | 12 |
| 22 | `src/FocusGuard.App/Views/StartSessionDialog.xaml` | 6 |
| 23 | `src/FocusGuard.App/Views/StartSessionDialog.xaml.cs` | 6 |
| 24 | `src/FocusGuard.App/ViewModels/StartSessionDialogViewModel.cs` | 6 |
| 25 | `src/FocusGuard.App/Views/UnlockDialog.xaml` | 6 |
| 26 | `src/FocusGuard.App/Views/UnlockDialog.xaml.cs` | 6 |
| 27 | `src/FocusGuard.App/ViewModels/UnlockDialogViewModel.cs` | 6 |
| 28 | `src/FocusGuard.App/Controls/CircularProgressRing.cs` | 6 |
| 29 | `src/FocusGuard.App/Models/StartSessionDialogResult.cs` | 6 |
| 30 | `src/FocusGuard.App/Models/UnlockDialogResult.cs` | 6 |
| 31 | `src/FocusGuard.App/Services/TrayIconService.cs` | 7 |
| 32 | `src/FocusGuard.App/Resources/AppIcon.ico` | 7 |
| 33 | `src/FocusGuard.App/Views/TimerOverlayWindow.xaml` | 8 |
| 34 | `src/FocusGuard.App/Views/TimerOverlayWindow.xaml.cs` | 8 |
| 35 | `src/FocusGuard.App/ViewModels/TimerOverlayViewModel.cs` | 8 |
| 36 | `src/FocusGuard.App/Views/MasterKeySetupDialog.xaml` | 9 |
| 37 | `src/FocusGuard.App/Views/MasterKeySetupDialog.xaml.cs` | 9 |
| 38 | `src/FocusGuard.App/ViewModels/MasterKeySetupViewModel.cs` | 9 |

### New Test Files (7)

| # | File | Step |
|---|------|------|
| 1 | `tests/FocusGuard.Core.Tests/Data/FocusSessionRepositoryTests.cs` | 1 |
| 2 | `tests/FocusGuard.Core.Tests/Data/SettingsRepositoryTests.cs` | 1 |
| 3 | `tests/FocusGuard.Core.Tests/Security/PasswordGeneratorTests.cs` | 2 |
| 4 | `tests/FocusGuard.Core.Tests/Security/PasswordValidatorTests.cs` | 2 |
| 5 | `tests/FocusGuard.Core.Tests/Security/MasterKeyServiceTests.cs` | 2 |
| 6 | `tests/FocusGuard.Core.Tests/Sessions/FocusSessionManagerTests.cs` | 3 |
| 7 | `tests/FocusGuard.Core.Tests/Sessions/PomodoroIntervalCalculatorTests.cs` | 5 |

### Modified Files (~12)

| # | File | Steps |
|---|------|-------|
| 1 | `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` | 1 |
| 2 | `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | 1, 2, 3, 5, 10, 12 |
| 3 | `src/FocusGuard.App/App.xaml.cs` | 1, 7, 9, 10 |
| 4 | `src/FocusGuard.App/Services/BlockingOrchestrator.cs` | 4 |
| 5 | `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | 6 |
| 6 | `src/FocusGuard.App/Views/DashboardView.xaml` | 6 |
| 7 | `src/FocusGuard.App/Services/IDialogService.cs` | 6 |
| 8 | `src/FocusGuard.App/Services/DialogService.cs` | 6 |
| 9 | `src/FocusGuard.App/FocusGuard.App.csproj` | 7 |
| 10 | `src/FocusGuard.App/Views/MainWindow.xaml.cs` | 7 |
| 11 | `src/FocusGuard.App/Views/MainWindow.xaml` | 6 (version string update) |

---

## Risk Mitigations

| Risk | Mitigation |
|------|------------|
| **`EnsureCreated()` + raw SQL conflicts** | `CREATE TABLE IF NOT EXISTS` is idempotent — safe to run on both fresh and existing databases |
| **Paste bypass in unlock dialog** | `DataObject.AddPastingHandler` + `CommandManager` approach covers `Ctrl+V`, right-click paste, and drag-drop. Also disable `InputMethod` (IME) paste. |
| **System tray icon not appearing** | `NotifyIcon.Visible = true` must be called on UI thread. Use `Application.Current.Dispatcher.Invoke` if set from background. |
| **Timer accuracy drift** | Use `DateTime.UtcNow` for elapsed calculation, not accumulated tick counts. The 1-second timer is only for UI refresh — actual duration tracked by wall clock. |
| **Overlay window focus stealing** | Set `ShowActivated="False"` on the overlay window to prevent stealing focus from user's work. |
| **Session state lost on crash** | `FocusSessionEntity` with State="Working" persists in DB. On restart, detect orphaned sessions and mark as ended (with `WasUnlockedEarly = true`). |
| **`SoundAlertService` on headless/remote systems** | Wrap `SystemSounds.Play()` in try-catch — fails silently if no audio device. |
| **`UseWindowsForms` + WPF coexistence** | Well-supported in .NET 8. `Application.EnableVisualStyles()` not needed — just `UseWindowsForms` in csproj. |
| **WPF dispatcher threading** | All UI updates from timer/session events must marshal to UI thread via `Application.Current.Dispatcher.InvokeAsync`. |

---

## End-to-End Verification Checklist

1. `dotnet build` — no errors, no warnings on new code
2. `dotnet test` — all existing + new tests pass
3. **Fresh install flow:** Delete `focusguard.db` → launch → master key setup dialog → save key → main window
4. **Returning user flow:** Launch → main window directly (no setup dialog)
5. **Start session:** Click profile card → start dialog → set 25min + Pomodoro → Start → blocking activates → timer counts down → overlay appears → tray tooltip shows time
6. **Pomodoro cycle:** Work (25m) → balloon "Short Break" + sound → break (5m) → balloon "Back to Work!" + sound → repeat → long break after 4 work sessions
7. **Early unlock:** Click "End Session Early" → unlock dialog → paste disabled → type password correctly → session ends → blocking deactivated → overlay closes
8. **Emergency unlock:** Unlock dialog → expand emergency section → type master key → session ends
9. **Wrong password:** Type incorrect password → unlock denied → session continues
10. **Minimize to tray:** Click minimize → window hides → tray icon visible → double-click → window restores
11. **Close during session:** Click X → window hides to tray (doesn't exit)
12. **Tray context menu:** Right-click → Open / Quick Start / Exit (exit disabled during session)
13. **Session persistence:** Start session → kill app → relaunch → orphaned session detected and cleaned up
14. **Sound toggle:** Disable sound in settings → Pomodoro transitions are silent
