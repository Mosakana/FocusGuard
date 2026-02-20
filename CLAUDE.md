# CLAUDE.md — FocusGuard Technical Reference

## Project Overview

FocusGuard is a Windows desktop focus/productivity app that blocks distracting websites and applications during focus sessions. Built with C# / .NET 8 / WPF, requires administrator privileges.

## Build & Test Commands

```bash
dotnet restore              # Restore all NuGet packages
dotnet build                # Build entire solution
dotnet test                 # Run all unit tests
dotnet run --project src/FocusGuard.App  # Launch app (needs admin)
```

Publish single-file:
```bash
dotnet publish src/FocusGuard.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Solution Structure

```
FocusGuard.sln
├── src/FocusGuard.Core        (net8.0, class library — no UI dependency)
├── src/FocusGuard.App         (net8.0-windows, WPF WinExe — references Core)
├── src/FocusGuard.Watchdog    (net8.0-windows, WinExe — standalone watchdog process)
└── tests/FocusGuard.Core.Tests (net8.0, xUnit test project — references Core)
```

## Architecture & Patterns

### MVVM (ViewModel-First Navigation)
- `INavigationService.NavigateTo<T>()` resolves ViewModel from DI
- WPF implicit `DataTemplate` in `App.xaml` maps ViewModel → View
- Views have parameterless constructors (no DI in Views)
- ViewModels inherit `ViewModelBase` (extends `ObservableObject` from CommunityToolkit.Mvvm)
- Use `[ObservableProperty]` and `[RelayCommand]` source generators

### Dependency Injection
- Core services registered via `ServiceCollectionExtensions.AddFocusGuardCore()`
- App services registered in `App.xaml.cs` `ConfigureServices`
- `IDbContextFactory<FocusGuardDbContext>` pattern (not direct DbContext injection) — each operation gets a fresh context to avoid staleness in long-running WPF
- Repositories: Scoped (except `IFocusSessionRepository`: Singleton — consumed by singleton manager, safe via `IDbContextFactory`). Blocking engines: Singleton. Security services: Singleton. Session manager: Singleton. ViewModels: Transient (except MainWindowViewModel: Singleton)

### Database (SQLite + EF Core)
- Database path: `%APPDATA%\FocusGuard\focusguard.db`
- Phase 1 tables created via `EnsureCreated()`; Phase 2+ tables added via `DatabaseMigrator` (`CREATE TABLE IF NOT EXISTS`)
- `DatabaseMigrator.MigrateAsync()` runs at startup after `EnsureCreated()` — idempotent
- Blocked websites/applications stored as JSON string columns: `["youtube.com","reddit.com"]`
- 3 seeded preset profiles: "Social Media", "Gaming", "Entertainment" (IsPreset=true, cannot be deleted)
- Tables: `Profiles`, `FocusSessions` (session log with state tracking), `Settings` (key-value store)

### Security Layer
- `PasswordGenerator` — Cryptographically random passwords via `System.Security.Cryptography.RandomNumberGenerator`
  - Easy: lowercase only. Medium: mixed case + digits. Hard: + special chars (`!@#$%&*?`)
  - Guarantees at least one character from each required group
- `PasswordValidator` — Exact ordinal string comparison (case-sensitive)
- `MasterKeyService` — SHA-256 + salt hashing for master recovery key
  - `GenerateMasterKeyAsync()`: generates 32-hex-char key, stores hash+salt in Settings, returns plaintext (shown once)
  - `ValidateMasterKeyAsync()`: case-insensitive hex comparison, trims whitespace
  - `IsSetupCompleteAsync()`: checks if `SettingsKeys.MasterKeyHash` exists
- `SettingsKeys` — Constants for all settings keys (security, session, pomodoro, notifications, app)
- **Important .NET 8 note:** Use `Convert.ToHexString().ToLowerInvariant()` (not `ToHexStringLower` which is .NET 9+)

### Focus Session State Machine
- `FocusSessionState` enum: `Idle`, `Working`, `ShortBreak`, `LongBreak`, `Ended`
- `FocusSessionManager` (Singleton, implements `IFocusSessionManager`) — core session lifecycle
  - `StartSessionAsync(profileId, durationMinutes, pomodoroEnabled)`: loads password settings from `ISettingsRepository` (defaults: Medium/30), generates password, creates `FocusSessionEntity`, starts `System.Timers.Timer`, transitions Idle → Working
  - `TryUnlockAsync(password)`: validates via `PasswordValidator`, ends session early if correct
  - `EmergencyUnlockAsync(masterKey)`: validates via `MasterKeyService`, ends session early if correct
  - `EndSessionNaturallyAsync()`: called when timer expires, ends session normally
  - `AdvancePomodoroInterval()`: Working → ShortBreak/LongBreak → Working cycle, long break every N intervals (default 4)
  - `GetUnlockPassword()`: returns the in-memory password (never persisted, lost on crash)
  - Events: `StateChanged`, `SessionEnded`, `PomodoroIntervalChanged`
- Thread safety via `SemaphoreSlim(1,1)` — timer callbacks arrive on thread pool threads
- `FocusSessionInfo` — immutable snapshot record: SessionId, ProfileId, ProfileName, State, Elapsed, TotalPlanned, CurrentIntervalRemaining, PomodoroCompletedCount, UnlockPassword
- Session end (common path): updates entity with EndTime, ActualDurationMinutes, WasUnlockedEarly, State="Ended", saves to DB, clears in-memory state

### BlockingOrchestrator (Session ↔ Blocking Bridge)
- `BlockingOrchestrator` subscribes to `IFocusSessionManager.StateChanged` in constructor
- `Working` (when `!IsActive`) → `ActivateProfileAsync(session.ProfileId)` — auto-activates blocking
- `Idle` (when `IsActive`) → `DeactivateAsync()` — auto-deactivates blocking
- `ShortBreak` / `LongBreak` → no action, blocking remains active during breaks
- Guards prevent double-activation on Pomodoro Working→Break→Working cycles
- `async void` event handler with try-catch to prevent unobserved exceptions
- Exposes `SessionManager` property for UI access to `IFocusSessionManager`

### Blocking Engines
- **Website:** `HostsFileWebsiteBlocker` — modifies `C:\Windows\System32\drivers\etc\hosts` with marker comments (`# >>> FocusGuard START/END <<<`). Atomic writes via temp file. Thread-safe via `SemaphoreSlim`. DNS flush via `ipconfig /flushdns`.
- **Application:** `ProcessApplicationBlocker` — dual detection: WMI EventWatcher (real-time ~1s) + polling timer (2s fallback). Kills blocked processes via `Process.Kill(entireProcessTree: true)`.
- `DomainHelper`: normalize (strip protocol/path, lowercase), expand (add www.), validate (regex)
- `ProcessHelper`: normalize (remove .exe, lowercase), list running processes

### Global Exception Handling & Crash Recovery
- `App.xaml.cs` installs 3 global handlers: `DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`
- `EmergencyCleanup()` removes hosts file entries and stops application blocker on crash
- `ICrashRecoveryService.RecoverAsync()` runs at startup: cleans stale hosts entries + marks orphaned sessions as Ended
- `IFocusSessionRepository.GetOrphanedSessionsAsync()` finds sessions in Working/ShortBreak/LongBreak state
- Named `Mutex("FocusGuard_SingleInstance")` enforces single instance

### Strict Mode & Hardening
- `IStrictModeService` — persists `app.strict_mode_enabled` setting. Can only toggle when `FocusSessionState.Idle`
- `MainWindow.OnClosing` cancels close when strict mode enabled + session active
- `IHeartbeatService` — writes `heartbeat.json` (atomic via temp file) every 5s with PID, session ID, timestamp
- `IWatchdogLauncher` — starts/stops `FocusGuard.Watchdog.exe` from `AppContext.BaseDirectory`
- `FocusGuard.Watchdog` (separate process) — monitors heartbeat, restarts main app with `--recovered` if stale (>15s) + active session, max 3 attempts
- `ISessionRecoveryService.TryRecoverSessionAsync()` — on `--recovered` startup: resumes session with remaining time + new password, or marks expired sessions as ended
- `IFocusSessionManager.ResumeSessionAsync(sessionId, remainingMinutes)` — reloads entity, generates new password, starts timer for remaining time

### Auto-Start & Portable Mode
- `IAutoStartService` — reads/writes `HKCU\...\Run\FocusGuard` registry value with `--minimized` flag
- `AppPaths.IsPortableMode` — checks for `portable.marker` file in `AppContext.BaseDirectory`
- Portable mode: DB, logs, heartbeat go to `{appDir}/data/` instead of `%APPDATA%\FocusGuard\`
- Auto-start is a no-op in portable mode

### UI Theme
- Dark theme defined in `Resources/Theme.xaml`
- Key colors: Background `#1E1E2E`, Surface `#2A2A3E`, Primary `#4A90D9`, Text `#ECEFF4`
- Named styles: `NavButtonStyle`, `PrimaryButtonStyle`, `DangerButtonStyle`, `SecondaryButtonStyle`, `CardStyle`, `HeadingStyle`, `SubheadingStyle`, `BodyTextStyle`, `DarkTextBoxStyle`

## Key Namespaces

| Namespace | Purpose |
|-----------|---------|
| `FocusGuard.Core.Data.Entities` | EF Core entities (ProfileEntity, FocusSessionEntity, SettingEntity) |
| `FocusGuard.Core.Data.Repositories` | Repository interfaces and implementations |
| `FocusGuard.Core.Data` | DbContext, DatabaseMigrator |
| `FocusGuard.Core.Blocking` | Website & application blocking engines |
| `FocusGuard.Core.Configuration` | AppPaths (data dir, DB path, log dir) |
| `FocusGuard.Core.Security` | PasswordGenerator, PasswordValidator, MasterKeyService, SettingsKeys |
| `FocusGuard.Core.Sessions` | FocusSessionManager, FocusSessionState, FocusSessionInfo |
| `FocusGuard.App.ViewModels` | All ViewModels |
| `FocusGuard.App.Views` | All XAML Views |
| `FocusGuard.App.Services` | NavigationService, DialogService, BlockingOrchestrator |
| `FocusGuard.App.Models` | UI display models (ProfileSummary, ProfileListItem) |
| `FocusGuard.Core.Hardening` | Strict mode, heartbeat, watchdog launcher, auto-start |
| `FocusGuard.Core.Recovery` | Crash recovery, session recovery |
| `FocusGuard.App.Converters` | WPF value converters |

## File Layout

```
src/FocusGuard.Core/
├── Configuration/
│   └── AppPaths.cs                    # %APPDATA%\FocusGuard paths
├── Data/
│   ├── Entities/
│   │   ├── ProfileEntity.cs           # Id, Name, Color, BlockedWebsites(JSON), BlockedApplications(JSON), IsPreset
│   │   ├── FocusSessionEntity.cs      # Id, ProfileId, StartTime, EndTime, State, PomodoroCompletedCount, WasUnlockedEarly
│   │   └── SettingEntity.cs           # Key (PK), Value — generic key-value settings store
│   ├── Repositories/
│   │   ├── IProfileRepository.cs      # GetAll, GetById, Create, Update, Delete, Exists
│   │   ├── ProfileRepository.cs       # IDbContextFactory-based implementation
│   │   ├── ISettingsRepository.cs     # Get, Set, Exists — key-value settings access
│   │   ├── SettingsRepository.cs      # IDbContextFactory-based implementation
│   │   ├── IFocusSessionRepository.cs # Create, Update, GetById, GetActiveSession, GetRecent
│   │   └── FocusSessionRepository.cs  # IDbContextFactory-based implementation (Singleton-safe)
│   ├── DatabaseMigrator.cs            # CREATE TABLE IF NOT EXISTS for Phase 2+ tables
│   └── FocusGuardDbContext.cs          # DbSet<ProfileEntity/FocusSessionEntity/SettingEntity>, seeds 3 presets
├── Blocking/
│   ├── IWebsiteBlocker.cs             # ApplyBlocklist, RemoveBlocklist, GetCurrentlyBlocked, IsActive
│   ├── HostsFileWebsiteBlocker.cs     # Hosts file modification with markers
│   ├── IApplicationBlocker.cs         # StartBlocking, StopBlocking, IsActive, ProcessBlocked event
│   ├── ProcessApplicationBlocker.cs   # WMI + polling dual detection
│   ├── BlockedProcessEventArgs.cs     # ProcessName, Timestamp
│   ├── DomainHelper.cs                # Normalize, Expand, IsValid (GeneratedRegex)
│   └── ProcessHelper.cs               # NormalizeProcessName, GetRunningProcessNames
├── Security/
│   ├── PasswordDifficulty.cs          # Enum: Easy, Medium, Hard
│   ├── PasswordGenerator.cs           # Cryptographic random password generation (RandomNumberGenerator)
│   ├── PasswordValidator.cs           # Exact ordinal string comparison
│   ├── MasterKeyService.cs            # Master recovery key: generate (SHA-256+salt), validate, setup check
│   └── SettingsKeys.cs                # Constants for all settings keys (incl. Phase 5: strict_mode, auto_start)
├── Hardening/
│   ├── IStrictModeService.cs          # Toggle strict mode (prevents close during session)
│   ├── StrictModeService.cs           # Persists to settings, guards against toggle during session
│   ├── HeartbeatData.cs               # Heartbeat JSON contract: PID, timestamp, session info
│   ├── HeartbeatHelper.cs             # Static read/write/delete for heartbeat.json (atomic writes)
│   ├── IHeartbeatService.cs           # Start/stop/update heartbeat timer
│   ├── HeartbeatService.cs            # Writes heartbeat every 5s via System.Threading.Timer
│   ├── IWatchdogLauncher.cs           # Launch/stop watchdog process
│   ├── WatchdogLauncher.cs            # Starts FocusGuard.Watchdog.exe with runas verb
│   ├── IAutoStartService.cs           # Enable/disable auto-start on boot
│   └── AutoStartService.cs            # HKCU registry Run key, portable mode guard
├── Recovery/
│   ├── ICrashRecoveryService.cs       # Cleanup hosts + orphaned sessions
│   ├── CrashRecoveryService.cs        # Runs at startup, marks orphaned sessions as Ended
│   ├── ISessionRecoveryService.cs     # Resume session after crash
│   └── SessionRecoveryService.cs      # Checks remaining time, calls ResumeSessionAsync or marks ended
├── Sessions/
│   ├── FocusSessionState.cs           # Enum: Idle, Working, ShortBreak, LongBreak, Ended
│   ├── FocusSessionInfo.cs            # Immutable snapshot record for current session
│   ├── IFocusSessionManager.cs        # Interface: Start, TryUnlock, EmergencyUnlock, EndNaturally, Pomodoro
│   └── FocusSessionManager.cs         # Singleton state machine: timer, password, pomodoro, thread-safe
└── ServiceCollectionExtensions.cs      # AddFocusGuardCore() extension method

src/FocusGuard.App/
├── App.xaml / App.xaml.cs              # Entry point: Serilog, IHost, DI, EnsureCreated, DatabaseMigrator
├── app.manifest                        # requireAdministrator
├── Resources/
│   └── Theme.xaml                      # Dark theme colors, button/text/card styles
├── Services/
│   ├── INavigationService.cs           # NavigateTo<T>(), CurrentView, CurrentViewChanged event
│   ├── NavigationService.cs
│   ├── IDialogService.cs               # ConfirmAsync, OpenFileAsync, SaveFileAsync
│   ├── DialogService.cs
│   └── BlockingOrchestrator.cs         # Session-driven blocking: subscribes to IFocusSessionManager.StateChanged, auto-activates on Working, auto-deactivates on Idle
├── ViewModels/
│   ├── ViewModelBase.cs                # Abstract, inherits ObservableObject, virtual OnNavigatedTo()
│   ├── MainWindowViewModel.cs          # CurrentView, nav commands
│   ├── DashboardViewModel.cs           # StatusText, IsBlocking, Profiles collection
│   ├── ProfilesViewModel.cs            # Profile list CRUD, import/export
│   └── ProfileEditorViewModel.cs       # Edit profile: name, color, websites, apps, save/discard
├── Views/
│   ├── MainWindow.xaml / .xaml.cs      # Two-column: sidebar (220px) + content area
│   ├── DashboardView.xaml / .xaml.cs   # Status card + profile cards + coming-soon placeholders
│   └── ProfilesView.xaml / .xaml.cs    # Master-detail: profile list + editor
├── Models/
│   ├── ProfileSummary.cs               # Id, Name, Color, WebsiteCount, AppCount, IsPreset
│   └── ProfileListItem.cs              # Same shape as ProfileSummary
└── Converters/
    ├── HexToColorConverter.cs          # Hex string → SolidColorBrush
    ├── BoolToVisibilityConverter.cs    # bool → Visibility
    └── InverseBoolConverter.cs         # bool → !bool

src/FocusGuard.Watchdog/
├── FocusGuard.Watchdog.csproj          # net8.0-windows WinExe, no Core dependency
├── app.manifest                        # requireAdministrator
└── Program.cs                          # Monitor loop: read heartbeat, restart on stale + active session

installer/
├── FocusGuard.iss                      # Inno Setup 6 script
└── build.ps1                           # PowerShell: build, test, publish, package

tests/FocusGuard.Core.Tests/
├── Data/
│   └── ProfileRepositoryTests.cs       # CRUD, preset protection, duplicate name (InMemory provider)
├── Blocking/
│   ├── DomainHelperTests.cs             # Normalize, Expand, IsValid
│   └── ProcessHelperTests.cs            # NormalizeProcessName, GetRunningProcessNames
├── Security/
│   ├── PasswordGeneratorTests.cs        # Length, char sets, group guarantees, uniqueness, edge cases
│   ├── PasswordValidatorTests.cs        # Exact match, case sensitivity, nulls, whitespace
│   └── MasterKeyServiceTests.cs         # Generate/validate, hash storage, case-insensitive hex, setup check
├── Hardening/
│   ├── StrictModeServiceTests.cs        # Toggle, persistence, session guard, throws during session
│   └── HeartbeatHelperTests.cs          # Write/read round-trip, delete, null on missing
├── Recovery/
│   ├── CrashRecoveryServiceTests.cs     # Orphaned session cleanup (Working/ShortBreak/LongBreak), hosts cleanup
│   └── SessionRecoveryServiceTests.cs   # No active → false, expired → ended, active → resumes
└── Sessions/
    └── FocusSessionManagerTests.cs      # State transitions, unlock, emergency unlock, pomodoro, persistence, events, ResumeSessionAsync
```

## Development Phases

| Phase | Status | Description |
|-------|--------|-------------|
| 1 — Core Foundation | Done | Scaffolding, profile CRUD, blocking engines, WPF shell |
| 2 — Focus Session & Timer | Done | Session lifecycle, password unlock, Pomodoro, tray, overlay |
| 3 — Calendar & Scheduling | Done | Calendar UI, drag-and-drop scheduling, recurring sessions |
| 4 — Statistics & Polish | Done | Charts, goals, streaks, notifications, auto-start |
| 5 — Hardening | Done | Global exception handling, crash recovery, strict mode, watchdog, auto-start, portable mode, Inno Setup installer |

Phase implementation plans: `docs/validated-sprouting-anchor.md` (Phase 1), `docs/focus-session-timer.md` (Phase 2).

## Conventions

- Admin privileges required at runtime (hosts file + process kill)
- Logging: Serilog, rolling daily files in `%APPDATA%\FocusGuard\logs/`, 7-day retention
- Async throughout: all repository methods return `Task<T>`
- Profile JSON import/export format: `{ "name", "color", "blockedWebsites": [], "blockedApplications": [] }`
- Color values stored as hex strings: `#RRGGBB`
- Preset profiles use fixed GUIDs: `00000000-0000-0000-0000-00000000000{1,2,3}`
