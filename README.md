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

> **Planned:** Focus session state machine, Pomodoro timer, system tray integration, floating timer overlay, calendar scheduling, statistics dashboard.

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
│   │   └── Security/               # Password generation, validation, master key service
│   └── FocusGuard.App/             # WPF desktop application
│       ├── Converters/             # WPF value converters (hex→color, bool→visibility)
│       ├── Models/                 # UI display models
│       ├── Resources/              # Theme and style definitions
│       ├── Services/               # Navigation, dialogs, blocking orchestration
│       ├── ViewModels/             # MVVM ViewModels
│       └── Views/                  # XAML views (MainWindow, Dashboard, Profiles)
└── tests/
    └── FocusGuard.Core.Tests/      # Unit tests for core logic
        ├── Blocking/               # Domain helper and process helper tests
        ├── Data/                   # Repository tests
        └── Security/               # Password generator, validator, master key tests
```

### `docs/`

Design documents and step-by-step implementation plans for each development phase.

### `src/FocusGuard.Core/`

Platform-independent core logic with no WPF dependency. Contains:

- **Blocking/** — `HostsFileWebsiteBlocker` (modifies system hosts file to redirect blocked domains to 127.0.0.1) and `ProcessApplicationBlocker` (monitors and kills blocked processes via WMI + polling). Helper classes for domain validation and process name normalization.
- **Configuration/** — `AppPaths` defines file paths (`%APPDATA%\FocusGuard\` for database and logs).
- **Data/** — SQLite database layer using Entity Framework Core. `ProfileEntity` stores blacklist profiles, `FocusSessionEntity` tracks session history, `SettingEntity` provides key-value settings. `DatabaseMigrator` handles schema evolution for Phase 2+ tables via `CREATE TABLE IF NOT EXISTS`. Repository pattern with `IDbContextFactory` for thread-safe database access.
- **Security/** — `PasswordGenerator` creates cryptographically random passwords using `RandomNumberGenerator` with three difficulty levels (Easy: lowercase, Medium: mixed case + digits, Hard: + specials). `PasswordValidator` performs exact ordinal comparison. `MasterKeyService` manages master recovery key lifecycle (generate, hash with SHA-256 + salt, validate). `SettingsKeys` defines constants for all application settings.

### `src/FocusGuard.App/`

WPF desktop application using MVVM pattern:

- **Views/** — XAML UI: main window with sidebar navigation, dashboard with profile cards, profiles editor with website/app list management.
- **ViewModels/** — Application logic: dashboard status display, profile CRUD operations, editor with save/discard tracking.
- **Services/** — `NavigationService` (ViewModel-first page switching), `DialogService` (confirmation and file dialogs), `BlockingOrchestrator` (coordinates website and app blockers for a given profile).
- **Resources/** — Dark theme definition with color palette, button styles, and typography.
- **Converters/** — Hex color string to brush, boolean to visibility, inverse boolean.
- **Models/** — Lightweight display models for UI data binding.

### `tests/FocusGuard.Core.Tests/`

xUnit tests using EF Core InMemory provider and Moq. Covers profile repository CRUD operations, domain validation/normalization, process name handling, password generation (length, character sets, group guarantees, uniqueness), password validation (exact match, case sensitivity), and master key service (generate, validate, hash storage).

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
