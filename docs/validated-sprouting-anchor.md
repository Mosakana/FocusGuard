# FocusGuard Phase 1: Core Foundation — Implementation Plan

## Context

This is a greenfield C# WPF project for a Windows focus/productivity app. Phase 1 builds the project foundation: scaffolding, UI shell, profile CRUD, and both blocking engines (website + application).

---

## Step 1: Solution Scaffolding

Create .NET 8 solution with three projects:

| Project | Type | Purpose |
|---------|------|---------|
| `src/FocusGuard.App` | WPF WinExe | UI application |
| `src/FocusGuard.Core` | Class Library | Core logic, no UI dependency |
| `tests/FocusGuard.Core.Tests` | xUnit Test | Unit tests |

**Files:**
- `.gitignore` — Standard .NET gitignore
- `FocusGuard.sln`
- `src\FocusGuard.Core\FocusGuard.Core.csproj`
- `src\FocusGuard.App\FocusGuard.App.csproj`
- `src\FocusGuard.App\app.manifest` — Requests admin privileges (`requireAdministrator`)
- `tests\FocusGuard.Core.Tests\FocusGuard.Core.Tests.csproj`

**NuGet Packages:**

| Package | Project | Purpose |
|---------|---------|---------|
| CommunityToolkit.Mvvm 8.2.x | App | MVVM source generators |
| Microsoft.Extensions.DependencyInjection 8.0.x | App | DI container |
| Microsoft.Extensions.Hosting 8.0.x | App | Generic host lifecycle |
| Microsoft.EntityFrameworkCore.Sqlite 8.0.x | Core | SQLite provider |
| Microsoft.EntityFrameworkCore.Design 8.0.x | Core | EF tooling |
| Microsoft.Extensions.DependencyInjection.Abstractions 8.0.x | Core | DI abstractions |
| System.Management 8.0.x | Core | WMI for process monitoring |
| Serilog + Serilog.Extensions.Logging + Serilog.Sinks.File | Both | Logging |
| xunit + xunit.runner.visualstudio + Moq + EF InMemory | Tests | Testing |

**Verify:** `dotnet restore && dotnet build` succeeds.

---

## Step 2: Core Data Layer

**Files:**
- `src/FocusGuard.Core/Configuration/AppPaths.cs` — Static paths: `%APPDATA%\FocusGuard\` for DB, logs
- `src/FocusGuard.Core/Data/Entities/ProfileEntity.cs` — Entity: Id (Guid), Name, Color (hex), BlockedWebsites (JSON string), BlockedApplications (JSON string), IsPreset, CreatedAt
- `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` — DbContext with `DbSet<ProfileEntity>`, seeds 3 presets: "Social Media", "Gaming", "Entertainment"
- `src/FocusGuard.Core/Data/Repositories/IProfileRepository.cs` — Interface: GetAll, GetById, Create, Update, Delete, Exists
- `src/FocusGuard.Core/Data/Repositories/ProfileRepository.cs` — Implementation using `IDbContextFactory<>` (avoids stale context in long-running WPF app). Preset profiles cannot be deleted.
- `src/FocusGuard.Core/ServiceCollectionExtensions.cs` — `AddFocusGuardCore()` extension: registers DbContextFactory, repositories, blockers

**Key decisions:**
- Use `IDbContextFactory` (not direct DbContext injection) — each operation gets its own context
- BlockedWebsites/BlockedApplications stored as JSON string columns for simplicity
- Presets are editable but not deletable

**Tests:** `tests/FocusGuard.Core.Tests/Data/ProfileRepositoryTests.cs` — CRUD, preset protection, duplicate name check (using InMemory provider)

**Verify:** `dotnet test` passes.

---

## Step 3: Blocking Engines

### Website Blocker
- `src/FocusGuard.Core/Blocking/IWebsiteBlocker.cs` — Interface: ApplyBlocklist, RemoveBlocklist, GetCurrentlyBlocked, IsActive
- `src/FocusGuard.Core/Blocking/HostsFileWebsiteBlocker.cs` — Modifies `C:\Windows\System32\drivers\etc\hosts` with marker comments:
  ```
  # >>> FocusGuard START - DO NOT EDIT <<<
  127.0.0.1 youtube.com
  127.0.0.1 www.youtube.com
  # >>> FocusGuard END <<<
  ```
  Atomic writes (temp file → replace). Flushes DNS via `ipconfig /flushdns`. Thread-safe via `SemaphoreSlim`.
- `src/FocusGuard.Core/Blocking/DomainHelper.cs` — Normalize domains (strip protocol/path, lowercase), expand (add www. prefix), validate

### Application Blocker
- `src/FocusGuard.Core/Blocking/IApplicationBlocker.cs` — Interface: StartBlocking, StopBlocking, IsActive, ProcessBlocked event
- `src/FocusGuard.Core/Blocking/BlockedProcessEventArgs.cs` — Event args: ProcessName, Timestamp
- `src/FocusGuard.Core/Blocking/ProcessApplicationBlocker.cs` — Dual detection:
  1. **WMI EventWatcher** (real-time, ~1s delay): `__InstanceCreationEvent` for `Win32_Process`
  2. **Polling timer** (fallback, every 2s): `Process.GetProcessesByName()`
  Kills matched processes via `Process.Kill()`. Raises `ProcessBlocked` event.
- `src/FocusGuard.Core/Blocking/ProcessHelper.cs` — Normalize process names, list running processes

**Tests:**
- `tests/FocusGuard.Core.Tests/Blocking/DomainHelperTests.cs`
- `tests/FocusGuard.Core.Tests/Blocking/ProcessHelperTests.cs`
- `tests/FocusGuard.Core.Tests/Blocking/HostsFileWebsiteBlockerTests.cs` (integration, needs admin)

**Verify:** Unit tests pass. Integration test verifies hosts file modification.

---

## Step 4: WPF Application Shell

**Files:**
- `src/FocusGuard.App/App.xaml` + `App.xaml.cs` — Entry point: configure Serilog, build `IHost` with DI, ensure DB created, show MainWindow
- `src/FocusGuard.App/Resources/Theme.xaml` — Dark theme (Background #1E1E2E, Surface #2A2A3E, Primary #4A90D9, Text #ECEFF4)
- `src/FocusGuard.App/Services/INavigationService.cs` + `NavigationService.cs` — ViewModel-first navigation
- `src/FocusGuard.App/ViewModels/ViewModelBase.cs` — Abstract base, inherits `ObservableObject`
- `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs` — Holds CurrentView, navigation commands
- `src/FocusGuard.App/Views/MainWindow.xaml` + `.xaml.cs` — Two-column layout:
  - Left sidebar (~220px): App title + nav buttons (Dashboard, Profiles, Calendar*, Statistics*, Settings*) — * = disabled/grayed
  - Right content: `ContentControl` bound to `CurrentView`, auto-resolved via implicit `DataTemplate`

**Navigation pattern:** ViewModel-first — `NavigationService` resolves ViewModel from DI → WPF implicit DataTemplate maps ViewModel type to View → Views have no constructor DI.

**Verify:** App launches with sidebar. Clicking Dashboard/Profiles switches content area. Logs appear in `%APPDATA%\FocusGuard\logs\`. SQLite DB created.

---

## Step 5: Dashboard View (Minimal)

**Files:**
- `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` — Status text, loads profile summaries from repository, blocking status display
- `src/FocusGuard.App/Models/ProfileSummary.cs` — Display model: Id, Name, Color, WebsiteCount, AppCount
- `src/FocusGuard.App/Views/DashboardView.xaml` — Status card + profile quick-start cards + placeholders for calendar/stats (grayed "Coming Soon")

**Verify:** Dashboard shows 3 preset profiles on launch.

---

## Step 6: Profiles View with CRUD

**Files:**
- `src/FocusGuard.App/ViewModels/ProfilesViewModel.cs` — Profile list management: Load, Create, Delete (with confirmation, blocked for presets), Duplicate, Export (JSON), Import (JSON)
- `src/FocusGuard.App/ViewModels/ProfileEditorViewModel.cs` — Edit single profile: Name, Color (swatch selector), BlockedWebsites list (add/remove), BlockedApplications list (add/remove/browse .exe), Save, Discard. Tracks HasUnsavedChanges.
- `src/FocusGuard.App/Models/ProfileListItem.cs` — List display model
- `src/FocusGuard.App/Views/ProfilesView.xaml` — Master-detail layout: profile list (left) + editor (right)
- `src/FocusGuard.App/Converters/HexToColorConverter.cs` — Hex string → SolidColorBrush
- `src/FocusGuard.App/Converters/BoolToVisibilityConverter.cs`
- `src/FocusGuard.App/Converters/InverseBoolConverter.cs`
- `src/FocusGuard.App/Services/IDialogService.cs` + `DialogService.cs` — Confirm dialogs, file open/save dialogs (abstracted for testability)

**Profile JSON import/export format:**
```json
{
  "name": "Deep Work",
  "color": "#4A90D9",
  "blockedWebsites": ["youtube.com", "reddit.com"],
  "blockedApplications": ["steam.exe", "discord.exe"]
}
```

**Verify:** Full CRUD cycle works. Profiles persist across restart. Export/Import works. Presets can't be deleted.

---

## Step 7: Wire Blocking Engines to UI

**Files:**
- `src/FocusGuard.App/Services/BlockingOrchestrator.cs` — Coordinates both blockers: `ActivateProfileAsync(Guid)` loads profile and starts both engines, `DeactivateAsync()` stops both. Prepares for Phase 2 focus session lifecycle.
- **Modify** `ProfileEditorViewModel` — Add "Test Website Blocking" / "Test App Blocking" buttons with corresponding "Stop Test" buttons
- **Modify** `DashboardViewModel` — Show blocking status (active/inactive)

**Verify (End-to-End):**
1. `dotnet build` — no errors
2. `dotnet test` — all pass
3. Run app as admin → sidebar navigation works
4. Create profile with `youtube.com` + `notepad.exe` → Save → Persists after restart
5. Test Website Blocking → hosts file updated → youtube.com inaccessible → Stop → restored
6. Test App Blocking → Notepad killed on launch → Stop → Notepad works again
7. Export profile → Delete → Import → Profile restored

---

## Total: ~38 new files across 7 sequential steps

## Risk Mitigations
- **Admin UAC prompt on every launch** — Acceptable for Phase 1; background service in Phase 5
- **Hosts file locked by antivirus** — Catch IOException, show user-friendly error
- **WMI failure** — Fallback to polling-only with logged warning
- **Process.Kill() on protected processes** — Catch Win32Exception, log and notify
