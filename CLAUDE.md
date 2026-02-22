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
- Tables: `Profiles`, `FocusSessions` (session log with state tracking), `Settings` (key-value store), `ScheduledSessions` (calendar scheduling with recurrence), `BlockedAttempts` (blocked attempt log with timestamps)

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

### Session Interaction UI
- **Start Session Dialog** — modal (440x480, borderless dark) shown on profile card click. Duration presets (25/45/60/90/120m) + custom TextBox, Pomodoro toggle, password difficulty ComboBox. `StartSessionDialogViewModel` + `StartSessionDialogResult`
- **Unlock Dialog** — modal (520x560, borderless dark) for ending sessions early. Password display (hidden by default, reveal button), paste-disabled TextBox (`DataObject.AddPastingHandler` + `PreviewKeyDown`), character counter, error message. Emergency unlock via master key in an Expander. `UnlockDialogViewModel` + `UnlockDialogResult`
- **Master Key Setup Dialog** — shown on first launch before MainWindow. Consolas-font key display, copy button with "Copied!" state, "I have saved this key" checkbox gate. `MasterKeySetupViewModel`
- **Dashboard Integration** — "Start Focus Session" buttons on profile cards (disabled when session active), "End Session Early" DangerButton. Real-time status updates via `StateChanged` event marshalled to UI thread via `Dispatcher.InvokeAsync`

### Pomodoro Timer Visualization
- `PomodoroTimer` — Singleton, wraps 1-second `System.Timers.Timer`. Subscribes to `IFocusSessionManager.StateChanged` and `PomodoroIntervalChanged`. Fires `TimerTick` (UI refresh), `IntervalStarted`, `IntervalCompleted` events. Loads config from `ISettingsRepository`. Calls `AdvancePomodoroInterval()` on interval completion
- `PomodoroConfiguration` — WorkMinutes(25), ShortBreakMinutes(5), LongBreakMinutes(15), LongBreakInterval(4), AutoStartNext
- `PomodoroInterval` — Type (FocusSessionState), DurationMinutes, SequenceNumber
- `PomodoroIntervalCalculator` — Pure logic: `CalculateIntervals(config, totalMinutes)` generates full interval sequence, `GetNextInterval(config, currentState, completedCount)` returns next interval
- `CircularProgressRing` — Custom WPF `FrameworkElement` in `App/Controls/`. DependencyProperties: Progress(0–1), StrokeThickness, ProgressColor, TrackColor. `OnRender` draws track circle + arc via `StreamGeometry`
- `SoundAlertService` — System sounds via `System.Media.SystemSounds` (Exclamation=work, Asterisk=break, Hand=session end). Checks `SettingsKeys.SoundEnabled`. Wrapped in try-catch for headless
- **Dashboard Timer Card** — Visible when session active: CircularProgressRing (140x140) with centered countdown (28px Consolas), interval label, Pomodoro dot indicators (filled via `IndexLessThanConverter`), session remaining text
- **Converters** — `IntToRangeConverter` (int → `[0..N-1]` for ItemsControl), `IndexLessThanConverter` (MultiValueConverter: index < threshold → filled dot), `InverseBoolToVisibilityConverter`, `StringToVisibilityConverter`

### Calendar & Scheduling
- `ScheduledSessionEntity` — DB entity: Id, ProfileId, StartTime, EndTime, IsRecurring, RecurrenceRule (JSON), PomodoroEnabled, IsEnabled, CreatedAt
- `IScheduledSessionRepository` — CRUD + `GetEnabledAsync()`, `GetByDateRangeAsync(start, end)`
- `RecurrenceRule` — Type (Daily/Weekdays/Weekly/Custom), DaysOfWeek list, IntervalWeeks, EndDate
- `OccurrenceExpander` — Expands `ScheduledSessionEntity` into concrete `ScheduledOccurrence` instances for a date range. Handles all 4 recurrence types day-by-day
- `ISchedulingEngine` / `SchedulingEngine` — 15-second polling timer. Loads enabled sessions, expands for next 24h, fires `SessionStarting`/`SessionEnding` events. `HashSet<string>` tracks fired keys to prevent duplicates
- `BlockingOrchestrator` subscribes to `ISchedulingEngine.SessionStarting` — auto-starts focus sessions when scheduled time arrives (if no session active)
- **Calendar View** — Month grid (UniformGrid 6x7) with day-of-week headers, prev/next month arrows, Today button. Right side panel shows selected day's sessions with create/delete
- **Schedule Session Dialog** — Borderless dark modal (480x560): profile ComboBox, DatePicker, hour:minute ComboBoxes, Pomodoro checkbox, recurring toggle with recurrence type + day checkboxes, optional end date. `ScheduleSessionDialogViewModel` converts local times to UTC
- **Dashboard Integration** — "Today's Schedule" section shows upcoming sessions with profile color dot, time range, Pomodoro/recurring indicators. `LoadTodaySessionsAsync()` expands enabled sessions for current UTC day
- `CalendarDay` — Date, IsCurrentMonth, IsToday, TimeBlocks collection
- `CalendarTimeBlock` — ScheduledSessionId, ProfileId, ProfileName, ProfileColor, StartTime, EndTime, IsRecurring, PomodoroEnabled, computed TimeRangeDisplay and DurationMinutes

### Statistics & Analytics
- `BlockedAttemptEntity` — Id, SessionId (nullable), Timestamp, Type ("Application"/"Website"), Target
- `IBlockedAttemptRepository` — CRUD + GetBySessionId, GetByDateRange, GetCountByDateRange
- `BlockedAttemptLogger` — Singleton, subscribes to `IApplicationBlocker.ProcessBlocked`, logs to DB. Exposes `AttemptLogged` event
- `DailyFocusSummary` — record: Date, TotalFocusMinutes, SessionCount, PomodoroCount, BlockedAttemptCount
- `ProfileFocusSummary` — record: ProfileId, ProfileName, ProfileColor, TotalFocusMinutes, SessionCount
- `StreakInfo` — record: CurrentStreak, LongestStreak, StreakStartDate
- `PeriodStatistics` — record: PeriodStart/End, totals, DailyBreakdown, ProfileBreakdown, Streak
- `IStatisticsService` / `StatisticsService` — Queries completed sessions + blocked attempts, groups by day/profile, calculates streaks (consecutive days with >=1 min focus). Zero-fills missing days for heatmap
- `FocusGoal` — GoalPeriod (Daily/Weekly), TargetMinutes, ProfileId (nullable = global)
- `GoalProgress` — record: Goal, CurrentMinutes, CompletionPercent, IsCompleted, RemainingMinutes, DisplayLabel
- `IGoalService` / `GoalService` — Stores goals as JSON in Settings table. Key scheme: `"goal.daily"`, `"goal.weekly"`, `"goal.daily.{profileId}"`. Meta key `"goal.index"` tracks active goal keys
- `CsvExporter` — ExportSessionsAsync, ExportDailySummaryAsync. CSV-safe: escape commas/quotes, prefix injection chars
- **Statistics View** — Period selector (Day/Week/Month), 4 summary cards, bar chart (LiveCharts2 ColumnSeries), pie chart (profile breakdown), 90-day heatmap (pure WPF UniformGrid), streak info, goal progress bars with Set Goal dialog
- **Dashboard Enhancements** — `WeeklyMiniBarChart` custom control (7 bars), streak badge, goal progress section replacing "Coming Soon" placeholder
- LiveCharts2 package: `LiveChartsCore.SkiaSharpView.WPF` v2.0.0-rc* (requires `net8.0-windows10.0.19041` TFM)

### Settings View
- `SettingsViewModel` — Transient, extends `ViewModelBase`. Injects `ISettingsRepository`, `IStrictModeService`, `IAutoStartService`, `MasterKeyService`, `IFocusSessionManager`, `IDialogService`, `ILogger`
- 18 `[ObservableProperty]` fields across 5 groups: General, Session Defaults, Pomodoro, Notifications, Security
- `OnNavigatedTo()` → `LoadSettingsAsync()` reads all keys from repo + services with `_isLoading` guard to prevent saves during load
- Each property change uses `partial void On{Name}Changed(T value)` to persist immediately (skipped when `_isLoading`)
  - Bool settings → `SetAsync(key, value.ToString())`; Int settings → `SetAsync(key, value.ToString())` with min 1 guard
  - `AutoStartEnabled` → `IAutoStartService.Enable()` / `Disable()` (no-op in portable mode)
  - `StrictModeEnabled` → `IStrictModeService.SetEnabledAsync(value)`, reverts UI on failure
  - `PasswordDifficulty` → `SetAsync(key, value.ToString())`
- `CanToggleStrictMode` — computed from `IStrictModeService.CanToggleAsync()` (false when session active)
- `IsPortableMode` — readonly from `AppPaths.IsPortableMode`, disables auto-start checkbox
- `RegenerateMasterKeyCommand` — confirmation dialog → reuses `MasterKeySetupDialog` + `MasterKeySetupViewModel`
- **SettingsView** — ScrollViewer → StackPanel with 5 CardStyle sections. CheckBoxes for toggles, TextBoxes (LostFocus trigger) for numeric values, ComboBox for password difficulty, status indicator + DangerButton for master key

### UI Theme
- Dark theme defined in `Resources/Theme.xaml`
- Key colors: Background `#1E1E2E`, Surface `#2A2A3E`, Primary `#4A90D9`, Text `#ECEFF4`
- Named styles: `NavButtonStyle`, `PrimaryButtonStyle`, `DangerButtonStyle`, `SecondaryButtonStyle`, `CardStyle`, `HeadingStyle`, `SubheadingStyle`, `BodyTextStyle`, `DarkTextBoxStyle`

## Key Namespaces

| Namespace | Purpose |
|-----------|---------|
| `FocusGuard.Core.Data.Entities` | EF Core entities (ProfileEntity, FocusSessionEntity, SettingEntity, ScheduledSessionEntity) |
| `FocusGuard.Core.Data.Repositories` | Repository interfaces and implementations |
| `FocusGuard.Core.Data` | DbContext, DatabaseMigrator |
| `FocusGuard.Core.Blocking` | Website & application blocking engines |
| `FocusGuard.Core.Configuration` | AppPaths (data dir, DB path, log dir) |
| `FocusGuard.Core.Security` | PasswordGenerator, PasswordValidator, MasterKeyService, SettingsKeys |
| `FocusGuard.Core.Sessions` | FocusSessionManager, FocusSessionState, FocusSessionInfo, PomodoroTimer, PomodoroIntervalCalculator, SoundAlertService |
| `FocusGuard.App.ViewModels` | All ViewModels |
| `FocusGuard.App.Views` | All XAML Views |
| `FocusGuard.App.Services` | NavigationService, DialogService, BlockingOrchestrator |
| `FocusGuard.Core.Scheduling` | OccurrenceExpander, SchedulingEngine, RecurrenceRule, ScheduledOccurrence |
| `FocusGuard.App.Models` | UI display models (ProfileSummary, ProfileListItem, StartSessionDialogResult, UnlockDialogResult, CalendarDay, CalendarTimeBlock, ScheduleSessionDialogResult) |
| `FocusGuard.App.Controls` | Custom WPF controls (CircularProgressRing) |
| `FocusGuard.Core.Hardening` | Strict mode, heartbeat, watchdog launcher, auto-start |
| `FocusGuard.Core.Recovery` | Crash recovery, session recovery |
| `FocusGuard.Core.Statistics` | BlockedAttemptLogger, StatisticsService, GoalService, CsvExporter, models |
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
│   │   ├── ScheduledSessionEntity.cs  # Id, ProfileId, StartTime, EndTime, IsRecurring, RecurrenceRule(JSON), PomodoroEnabled
│   │   ├── BlockedAttemptEntity.cs    # Id, SessionId (nullable), Timestamp, Type, Target
│   │   └── SettingEntity.cs           # Key (PK), Value — generic key-value settings store
│   ├── Repositories/
│   │   ├── IProfileRepository.cs      # GetAll, GetById, Create, Update, Delete, Exists
│   │   ├── ProfileRepository.cs       # IDbContextFactory-based implementation
│   │   ├── ISettingsRepository.cs     # Get, Set, Exists — key-value settings access
│   │   ├── SettingsRepository.cs      # IDbContextFactory-based implementation
│   │   ├── IFocusSessionRepository.cs # Create, Update, GetById, GetActiveSession, GetRecent
│   │   ├── FocusSessionRepository.cs  # IDbContextFactory-based implementation (Singleton-safe)
│   │   ├── IScheduledSessionRepository.cs # CRUD + GetEnabled, GetByDateRange
│   │   ├── ScheduledSessionRepository.cs  # IDbContextFactory-based implementation
│   │   ├── IBlockedAttemptRepository.cs   # Create, GetBySessionId, GetByDateRange, GetCountByDateRange
│   │   └── BlockedAttemptRepository.cs    # IDbContextFactory-based implementation
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
├── Scheduling/
│   ├── RecurrenceRule.cs              # RecurrenceType enum (Daily/Weekdays/Weekly/Custom) + rule model
│   ├── ScheduledOccurrence.cs         # Concrete occurrence: SessionId, ProfileId, Start/EndTime, Pomodoro
│   ├── OccurrenceExpander.cs          # Expands ScheduledSessionEntity into occurrences for date range
│   ├── ISchedulingEngine.cs           # Interface: Start, Stop, Refresh, SessionStarting/SessionEnding events
│   └── SchedulingEngine.cs            # 15s polling timer, fires events when scheduled times arrive
├── Sessions/
│   ├── FocusSessionState.cs           # Enum: Idle, Working, ShortBreak, LongBreak, Ended
│   ├── FocusSessionInfo.cs            # Immutable snapshot record for current session
│   ├── IFocusSessionManager.cs        # Interface: Start, TryUnlock, EmergencyUnlock, EndNaturally, Pomodoro
│   ├── FocusSessionManager.cs         # Singleton state machine: timer, password, pomodoro, thread-safe
│   ├── PomodoroConfiguration.cs       # Config model: work/break durations, long break interval, auto-start
│   ├── PomodoroInterval.cs            # Interval model: Type (FocusSessionState), DurationMinutes, SequenceNumber
│   ├── PomodoroIntervalCalculator.cs  # Pure logic: CalculateIntervals, GetNextInterval
│   ├── PomodoroTimer.cs               # 1-second tick timer: drives UI refresh, manages interval transitions
│   └── SoundAlertService.cs           # System sounds on interval/session transitions (SystemSounds)
├── Statistics/
│   ├── BlockedAttemptLogger.cs        # Subscribes to ProcessBlocked, logs to DB, fires AttemptLogged
│   ├── CsvExporter.cs                 # Export sessions/daily summary to CSV with injection prevention
│   ├── DailyFocusSummary.cs           # Record: Date, TotalFocusMinutes, SessionCount, PomodoroCount, BlockedAttemptCount
│   ├── FocusGoal.cs                   # GoalPeriod enum + FocusGoal model
│   ├── GoalProgress.cs                # Record: Goal, CurrentMinutes, CompletionPercent, IsCompleted
│   ├── GoalService.cs                 # JSON-based goal storage in Settings table
│   ├── IGoalService.cs                # Get/Set/Remove goal, GetAllProgressAsync
│   ├── IStatisticsService.cs          # Period stats, streaks, daily focus, profile breakdown, heatmap
│   ├── PeriodStatistics.cs            # Record: period totals + daily/profile breakdowns
│   ├── ProfileFocusSummary.cs         # Record: ProfileId, ProfileName, TotalFocusMinutes
│   ├── StatisticsService.cs           # Queries sessions + blocked attempts, groups, calculates streaks
│   └── StreakInfo.cs                  # Record: CurrentStreak, LongestStreak, StreakStartDate
└── ServiceCollectionExtensions.cs      # AddFocusGuardCore() extension method

src/FocusGuard.App/
├── App.xaml / App.xaml.cs              # Entry point: Serilog, IHost, DI, EnsureCreated, DatabaseMigrator
├── app.manifest                        # requireAdministrator
├── Resources/
│   └── Theme.xaml                      # Dark theme colors, button/text/card styles
├── Controls/
│   ├── CircularProgressRing.cs         # Custom FrameworkElement: progress arc via StreamGeometry
│   └── WeeklyMiniBarChart.cs           # Custom FrameworkElement: 7 vertical bars + day labels
├── Services/
│   ├── INavigationService.cs           # NavigateTo<T>(), CurrentView, CurrentViewChanged event
│   ├── NavigationService.cs
│   ├── IDialogService.cs               # ConfirmAsync, OpenFileAsync, SaveFileAsync, ShowStartSessionDialogAsync, ShowUnlockDialogAsync, ShowScheduleSessionDialogAsync
│   ├── DialogService.cs                # IFocusSessionManager dependency for unlock dialog
│   └── BlockingOrchestrator.cs         # Session + scheduling driven blocking: subscribes to StateChanged + SessionStarting events
├── ViewModels/
│   ├── ViewModelBase.cs                # Abstract, inherits ObservableObject, virtual OnNavigatedTo()
│   ├── MainWindowViewModel.cs          # CurrentView, nav commands (incl. Statistics, Settings)
│   ├── DashboardViewModel.cs           # Session status, timer, Pomodoro, profiles, schedule, weekly chart, goals
│   ├── ProfilesViewModel.cs            # Profile list CRUD, import/export
│   ├── ProfileEditorViewModel.cs       # Edit profile: name, color, websites, apps, save/discard
│   ├── CalendarViewModel.cs            # Month grid, day selection, scheduled session CRUD
│   ├── StatisticsViewModel.cs          # Period selector, summary cards, charts, heatmap, goals, export
│   ├── SetGoalDialogViewModel.cs       # Goal period, target duration, confirm
│   ├── ScheduleSessionDialogViewModel.cs # Profile, date/time, Pomodoro, recurrence config
│   ├── StartSessionDialogViewModel.cs  # Duration presets, Pomodoro toggle, difficulty selector
│   ├── SettingsViewModel.cs            # All settings: general, session, pomodoro, notifications, security
│   ├── UnlockDialogViewModel.cs        # Password validation, emergency master key unlock
│   └── MasterKeySetupViewModel.cs      # First-launch master key display and copy
├── Views/
│   ├── MainWindow.xaml / .xaml.cs      # Two-column: sidebar (220px) + content area
│   ├── DashboardView.xaml / .xaml.cs   # Timer card, profile cards, schedule, weekly chart, goals
│   ├── ProfilesView.xaml / .xaml.cs    # Master-detail: profile list + editor
│   ├── CalendarView.xaml / .xaml.cs    # Month grid + day detail panel with session list
│   ├── StatisticsView.xaml / .xaml.cs  # Period selector, bar/pie charts, heatmap, streaks, goals
│   ├── SettingsView.xaml / .xaml.cs    # 5-section settings page: general, session, pomodoro, notifications, security
│   ├── SetGoalDialog.xaml / .xaml.cs   # Borderless dark modal: set focus goal
│   ├── ScheduleSessionDialog.xaml / .cs # Borderless dark modal: schedule with recurrence
│   ├── StartSessionDialog.xaml / .cs   # Borderless dark modal: duration, Pomodoro, difficulty
│   ├── UnlockDialog.xaml / .cs         # Borderless dark modal: password entry, emergency unlock
│   └── MasterKeySetupDialog.xaml / .cs # First-launch master key dialog
├── Models/
│   ├── ProfileSummary.cs               # Id, Name, Color, WebsiteCount, AppCount, IsPreset
│   ├── ProfileListItem.cs              # Same shape as ProfileSummary
│   ├── CalendarDay.cs                  # Date, IsCurrentMonth, IsToday, TimeBlocks
│   ├── CalendarTimeBlock.cs            # ScheduledSessionId, ProfileId/Name/Color, Start/EndTime, TimeRangeDisplay
│   ├── StatsSummaryCard.cs             # Title, Value, Subtitle, IconGlyph
│   ├── HeatmapDay.cs                   # Date, FocusMinutes, IntensityLevel, Color, ToolTipText
│   ├── ScheduleSessionDialogResult.cs  # ProfileId, Start/EndTime, Pomodoro, IsRecurring, RecurrenceRule
│   ├── StartSessionDialogResult.cs     # DurationMinutes, EnablePomodoro, Difficulty
│   └── UnlockDialogResult.cs           # Unlocked, EmergencyUsed
└── Converters/
    ├── HexToColorConverter.cs          # Hex string → SolidColorBrush
    ├── BoolToVisibilityConverter.cs    # bool → Visibility
    ├── InverseBoolConverter.cs         # bool → !bool
    ├── InverseBoolToVisibilityConverter.cs  # true → Collapsed, false → Visible
    ├── StringToVisibilityConverter.cs  # non-empty string → Visible
    ├── IntToRangeConverter.cs          # int N → [0..N-1] list for ItemsControl
    ├── IndexLessThanConverter.cs       # MultiValueConverter: index < threshold → true (Pomodoro dots)
    └── ProgressWidthConverter.cs       # MultiValueConverter: percent × width → pixel width (goal bars)

src/FocusGuard.Watchdog/
├── FocusGuard.Watchdog.csproj          # net8.0-windows WinExe, no Core dependency
├── app.manifest                        # requireAdministrator
└── Program.cs                          # Monitor loop: read heartbeat, restart on stale + active session

installer/
├── FocusGuard.iss                      # Inno Setup 6 script
└── build.ps1                           # PowerShell: build, test, publish, package

tests/FocusGuard.Core.Tests/
├── Data/
│   ├── ProfileRepositoryTests.cs       # CRUD, preset protection, duplicate name (InMemory provider)
│   └── BlockedAttemptRepositoryTests.cs # CRUD, date range, count
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
├── Scheduling/
│   └── OccurrenceExpanderTests.cs      # Non-recurring, Daily, Weekdays, Weekly, Bi-weekly, Custom, edge cases
├── Sessions/
│   ├── FocusSessionManagerTests.cs      # State transitions, unlock, emergency unlock, pomodoro, persistence, events, ResumeSessionAsync
│   └── PomodoroIntervalCalculatorTests.cs # Interval sequence, long break placement, duration fitting, custom config
└── Statistics/
    ├── StatisticsServiceTests.cs        # Empty range, single/multiple sessions, daily grouping, profile breakdown, streak, heatmap
    ├── GoalServiceTests.cs              # Set/get/remove, progress calculation, profile-specific goals
    └── CsvExporterTests.cs              # Correct escaping, injection prevention, empty dataset
```

## Development Phases

| Phase | Status | Description |
|-------|--------|-------------|
| 1 — Core Foundation | Done | Scaffolding, profile CRUD, blocking engines, WPF shell |
| 2 — Focus Session & Timer | Done | Session lifecycle, password unlock, Pomodoro, tray, overlay |
| 3 — Calendar & Scheduling | Done | Calendar UI, drag-and-drop scheduling, recurring sessions |
| 4 — Statistics & Polish | Done | Charts, goals, streaks, notifications, auto-start |
| 5 — Hardening | Done | Global exception handling, crash recovery, strict mode, watchdog, auto-start, portable mode, Inno Setup installer |

### UI Feature Areas (post-Phase 5)

| Feature Area | Status | Description |
|--------------|--------|-------------|
| 1 — Session Interaction UI | Done | Start session dialog, unlock dialog, master key setup, dashboard integration |
| 2 — Pomodoro Timer Visualization | Done | CircularProgressRing, 1s tick timer, interval indicators, sound alerts |
| 3 — Calendar & Scheduling | Done | Calendar month grid, schedule session dialog, recurring sessions, scheduling engine auto-activation, dashboard today's schedule |
| 4 — Statistics & Analytics | Done | Charts, goals, streaks, session history, CSV export, heatmap |
| 5 — System Tray & Notifications | Done | Tray icon, toast notifications, floating overlay |
| 6 — Settings View | Done | Settings page with general, session defaults, pomodoro, notifications, and security sections |

Phase implementation plans: `docs/validated-sprouting-anchor.md` (Phase 1), `docs/focus-session-timer.md` (Phase 2). UI feature plan: `docs/remaining-features.md`.

## Conventions

- Admin privileges required at runtime (hosts file + process kill)
- Logging: Serilog, rolling daily files in `%APPDATA%\FocusGuard\logs/`, 7-day retention
- Async throughout: all repository methods return `Task<T>`
- Profile JSON import/export format: `{ "name", "color", "blockedWebsites": [], "blockedApplications": [] }`
- Color values stored as hex strings: `#RRGGBB`
- Preset profiles use fixed GUIDs: `00000000-0000-0000-0000-00000000000{1,2,3}`
