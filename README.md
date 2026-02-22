# FocusGuard

A Windows desktop application that helps you stay focused by blocking distracting websites and applications during focus sessions.

## Features

- **Multi-Profile Blacklists** — Create profiles like "Deep Work", "Study", "Gaming" with different blocked websites and apps
- **Website Blocking** — Blocks websites via system hosts file modification (takes effect across all browsers)
- **Application Blocking** — Detects and terminates blocked applications in real-time
- **Built-in Presets** — Pre-configured profiles for Social Media, Gaming, and Entertainment
- **Import/Export** — Share profiles via JSON files
- **Random Password Unlock** — Focus sessions are protected by a random password (Easy/Medium/Hard difficulty) that must be typed manually (no copy-paste) to end early
- **Master Recovery Key** — SHA-256 hashed emergency key generated on first launch for genuine emergencies
- **Focus Session State Machine** — Full session lifecycle (Idle → Working → Ended) with timer-based natural expiration, password unlock, and emergency master key unlock
- **Pomodoro Mode** — Optional Pomodoro cycling (Working → ShortBreak → Working → ... → LongBreak) with configurable intervals
- **Pomodoro Timer Visualization** — Circular progress ring with large countdown display, interval labels (Focus Time / Short Break / Long Break), dot indicators showing completed intervals, and system sound alerts on transitions
- **Calendar Scheduling** — Monthly calendar view with day selection, schedule focus sessions with date/time picker, supports recurring sessions (Daily, Weekdays, Weekly, Custom days)
- **Scheduling Engine** — Background engine auto-starts focus sessions when scheduled time arrives; 15-second polling with duplicate prevention
- **Today's Schedule** — Dashboard shows today's upcoming sessions with profile colors and time ranges
- **Automatic Blocking** — Blocking engines activate automatically when a focus session starts (manual or scheduled) and deactivate when it ends; blocking stays active during Pomodoro breaks
- **Start Session Dialog** — Duration presets (25/45/60/90/120 min), Pomodoro toggle, password difficulty selector
- **Unlock Dialog** — Hidden password with reveal, paste-disabled input, emergency master key unlock
- **Master Key Setup** — First-launch setup with copyable recovery key
- **Crash Recovery** — Global exception handlers, orphaned session cleanup, watchdog process restart
- **Strict Mode** — Prevents app closure during active sessions, watchdog heartbeat monitoring
- **Auto-Start** — Optional Windows startup via registry, with `--minimized` flag
- **Portable Mode** — Drop a `portable.marker` file next to the exe to store all data locally
- **Statistics & Analytics** — Period-based statistics (day/week/month), bar and pie charts (LiveCharts2), 90-day focus heatmap, streak tracking, focus goals with progress bars, CSV export
- **System Tray & Notifications** — System tray icon with context menu, Windows toast notifications for session and Pomodoro events, floating timer overlay
- **Settings Page** — Centralized settings for general preferences (auto-start, minimize to tray, strict mode), session defaults (duration, password difficulty/length), Pomodoro configuration (work/break durations, auto-start), notification toggles, and master key regeneration

## Requirements

- **OS:** Windows 10 or Windows 11
- **Runtime:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Privileges:** Administrator (required for hosts file modification and process termination)

## Getting Started

```bash
# Clone the repository
git clone https://github.com/your-username/FocusGuard.git
cd FocusGuard

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Launch the app (requires admin / elevated terminal)
dotnet run --project src/FocusGuard.App
```

The app must run as Administrator. If launched from a non-elevated terminal, Windows will show a UAC prompt.

## Project Structure

```
FocusGuard/
├── FocusGuard.sln                  # Visual Studio solution file
├── docs/                           # Design documents and implementation plans
│   ├── requirements.md             # Full requirements & technical design spec
│   ├── validated-sprouting-anchor.md  # Phase 1 implementation plan
│   └── phase2-focus-session-timer.md  # Phase 2 implementation plan
├── src/
│   ├── FocusGuard.Core/            # Core logic library (no UI dependency)
│   │   ├── Blocking/               # Website & application blocking engines
│   │   ├── Configuration/          # Application paths and settings
│   │   ├── Data/                   # Database context, entities, repositories, migrator
│   │   ├── Hardening/              # Strict mode, heartbeat, watchdog, auto-start
│   │   ├── Recovery/               # Crash recovery, session recovery
│   │   ├── Security/               # Password generation, validation, master key service
│   │   ├── Scheduling/              # Calendar scheduling, recurrence rules, occurrence expander
│   │   └── Sessions/               # Focus session state machine, Pomodoro timer, sound alerts
│   ├── FocusGuard.Watchdog/        # Standalone watchdog process (heartbeat monitoring)
│   └── FocusGuard.App/             # WPF desktop application
│       ├── Controls/               # Custom WPF controls (CircularProgressRing)
│       ├── Converters/             # WPF value converters (hex→color, bool→visibility, etc.)
│       ├── Models/                 # UI display models and dialog results
│       ├── Resources/              # Theme and style definitions
│       ├── Services/               # Navigation, dialogs, blocking orchestration
│       ├── ViewModels/             # MVVM ViewModels (incl. dialog ViewModels)
│       └── Views/                  # XAML views and modal dialogs
├── installer/                      # Inno Setup installer script and build script
└── tests/
    └── FocusGuard.Core.Tests/      # Unit tests for core logic (209 tests)
        ├── Blocking/               # Domain helper and process helper tests
        ├── Data/                   # Repository tests
        ├── Hardening/              # Strict mode, heartbeat tests
        ├── Recovery/               # Crash recovery, session recovery tests
        ├── Scheduling/             # Occurrence expander tests (recurrence types, edge cases)
        ├── Security/               # Password generator, validator, master key tests
        └── Sessions/               # Session manager, Pomodoro interval calculator tests
```

### `docs/`

Design documents and step-by-step implementation plans for each development phase.

### `src/FocusGuard.Core/`

Platform-independent core logic with no WPF dependency. Contains:

- **Blocking/** — `HostsFileWebsiteBlocker` (modifies system hosts file to redirect blocked domains to 127.0.0.1) and `ProcessApplicationBlocker` (monitors and kills blocked processes via WMI + polling). Helper classes for domain validation and process name normalization.
- **Configuration/** — `AppPaths` defines file paths (`%APPDATA%\FocusGuard\` for database and logs).
- **Data/** — SQLite database layer using Entity Framework Core. `ProfileEntity` stores blacklist profiles, `FocusSessionEntity` tracks session history, `SettingEntity` provides key-value settings, `ScheduledSessionEntity` stores calendar-scheduled sessions with recurrence rules. `DatabaseMigrator` handles schema evolution for Phase 2+ tables via `CREATE TABLE IF NOT EXISTS`. Repository pattern with `IDbContextFactory` for thread-safe database access.
- **Security/** — `PasswordGenerator` creates cryptographically random passwords using `RandomNumberGenerator` with three difficulty levels (Easy: lowercase, Medium: mixed case + digits, Hard: + specials). `PasswordValidator` performs exact ordinal comparison. `MasterKeyService` manages master recovery key lifecycle (generate, hash with SHA-256 + salt, validate). `SettingsKeys` defines constants for all application settings.
- **Scheduling/** — `ScheduledSessionEntity` stores scheduled focus sessions with recurrence support. `RecurrenceRule` defines recurrence type (Daily/Weekdays/Weekly/Custom) with day-of-week list and interval configuration. `OccurrenceExpander` expands scheduled sessions into concrete occurrences for a date range. `SchedulingEngine` polls every 15 seconds, fires `SessionStarting`/`SessionEnding` events when scheduled times arrive.
- **Sessions/** — `FocusSessionManager` implements the core session state machine (Idle → Working → ShortBreak/LongBreak → Ended). Manages session timer (`System.Timers.Timer`), generates unlock passwords on session start, validates password and master key unlock attempts, supports Pomodoro interval cycling with configurable work/break durations. Thread-safe via `SemaphoreSlim`. Fires `StateChanged`, `SessionEnded`, and `PomodoroIntervalChanged` events. `PomodoroTimer` provides 1-second tick events for UI countdown refresh and auto-advances intervals. `PomodoroIntervalCalculator` computes interval sequences. `SoundAlertService` plays system sounds on state transitions. `FocusSessionInfo` provides immutable session snapshots.

### `src/FocusGuard.App/`

WPF desktop application using MVVM pattern:

- **Views/** — XAML UI: main window with sidebar navigation, dashboard with timer visualization and profile cards, profiles editor, calendar month grid with day detail panel, statistics with charts/heatmap/goals, settings page with 5 grouped sections, schedule session dialog, start session dialog, unlock dialog, master key setup dialog.
- **ViewModels/** — Application logic: dashboard with real-time timer display and Pomodoro progress and today's schedule, profile CRUD operations, calendar month navigation with session scheduling, statistics with period selection and chart data, settings with auto-persist on change, dialog ViewModels for session start/unlock/master key/schedule/goal flows.
- **Controls/** — Custom WPF controls: `CircularProgressRing` renders a progress arc via `StreamGeometry`.
- **Services/** — `NavigationService` (ViewModel-first page switching), `DialogService` (confirmation, file, and session dialogs), `BlockingOrchestrator` (bridges focus sessions to blocking engines — automatically activates blocking when a session starts and deactivates when it ends, keeps blocking active during Pomodoro breaks).
- **Resources/** — Dark theme definition with color palette, button styles, and typography.
- **Converters/** — Hex color to brush, boolean to visibility, inverse boolean, string to visibility, int-to-range for Pomodoro dots, index-less-than for filled/unfilled indicators.
- **Models/** — Lightweight display models and dialog result types.

### `tests/FocusGuard.Core.Tests/`

209 xUnit tests using EF Core InMemory provider and Moq. Covers profile repository CRUD, domain validation/normalization, process name handling, password generation/validation, master key service, focus session manager (state transitions, unlock, Pomodoro cycling, events), Pomodoro interval calculator (interval sequences, long break placement, custom config), occurrence expander (all recurrence types, edge cases), strict mode service, heartbeat helpers, crash recovery, and session recovery.

## Dependencies

### FocusGuard.Core

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.EntityFrameworkCore.Sqlite | 8.0.x | SQLite database provider |
| Microsoft.EntityFrameworkCore.Design | 8.0.x | EF Core tooling (design-time) |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.x | DI service registration |
| Serilog | 4.0.x | Structured logging |
| Serilog.Extensions.Logging | 8.0.x | Microsoft.Extensions.Logging integration |
| Serilog.Sinks.File | 6.0.x | Rolling file log output |
| System.Management | 8.0.x | WMI for real-time process monitoring |
| System.Windows.Extensions | 8.0.x | System sounds (SystemSounds) for alerts |

### FocusGuard.App

| Package | Version | Purpose |
|---------|---------|---------|
| CommunityToolkit.Mvvm | 8.2.x | MVVM source generators (ObservableProperty, RelayCommand) |
| Microsoft.Extensions.DependencyInjection | 8.0.x | DI container |
| Microsoft.Extensions.Hosting | 8.0.x | Generic host lifecycle management |
| Serilog | 4.0.x | Structured logging |
| Serilog.Extensions.Hosting | 8.0.x | Host builder integration |
| Serilog.Extensions.Logging | 8.0.x | Microsoft.Extensions.Logging integration |
| Serilog.Sinks.File | 6.0.x | Rolling file log output |

### FocusGuard.Core.Tests

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.NET.Test.Sdk | 17.10.x | Test platform |
| xunit | 2.9.x | Test framework |
| xunit.runner.visualstudio | 2.8.x | Visual Studio / CLI test runner |
| Moq | 4.20.x | Mocking framework |
| Microsoft.EntityFrameworkCore.InMemory | 8.0.x | In-memory database for testing |

## Data Storage

All application data is stored locally in `%APPDATA%\FocusGuard\`:

| Path | Content |
|------|---------|
| `focusguard.db` | SQLite database (profiles, sessions, settings) |
| `logs/focusguard-YYYYMMDD.log` | Daily rolling log files (7-day retention) |

## License

See [LICENSE](LICENSE) for details.
