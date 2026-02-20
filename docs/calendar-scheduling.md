# FocusGuard Phase 3: Calendar & Scheduling — Implementation Plan

## Context

Phase 1 (Core Foundation) is complete. Phase 2 (Focus Session & Timer) adds session lifecycle, password unlock, Pomodoro timer, system tray, and floating overlay.

Phase 3 adds a visual calendar system for scheduling focus sessions in advance, with drag-and-drop creation, recurring schedules, and auto-activation — per `requirements.md` §2.3, §5.3, and §7 Phase 3.

**Prerequisite:** Phase 2 code builds and tests pass (`dotnet build && dotnet test`). Phase 2's `IFocusSessionManager`, `BlockingOrchestrator`, and `PomodoroTimer` are fully functional.

---

## Step 1: Database Schema — ScheduledSession Entity

### New Entity

**`src/FocusGuard.Core/Data/Entities/ScheduledSessionEntity.cs`**
```csharp
namespace FocusGuard.Core.Data.Entities;

public class ScheduledSessionEntity
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurrenceRule { get; set; }  // JSON string, null if not recurring
    public bool PomodoroEnabled { get; set; }
    public bool IsEnabled { get; set; } = true;  // Can disable without deleting
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### Recurrence Rule Model

**`src/FocusGuard.Core/Scheduling/RecurrenceRule.cs`**
```csharp
namespace FocusGuard.Core.Scheduling;

public class RecurrenceRule
{
    public RecurrenceType Type { get; set; }           // Daily, Weekly, Weekdays, Custom
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];  // For Weekly/Custom
    public int IntervalWeeks { get; set; } = 1;        // Every N weeks (for Weekly)
    public DateTime? EndDate { get; set; }              // Null = no end date
}

public enum RecurrenceType
{
    Daily,      // Every day
    Weekdays,   // Monday–Friday
    Weekly,     // Same day(s) every N weeks
    Custom      // Specific days of week
}
```

The `RecurrenceRule` is serialized as JSON and stored in `ScheduledSessionEntity.RecurrenceRule`. The `StartTime` and `EndTime` on the entity represent the **time-of-day** template (hours and minutes) plus the **first occurrence date**. The recurrence engine expands occurrences from this template.

### Repository

**`src/FocusGuard.Core/Data/Repositories/IScheduledSessionRepository.cs`**
```csharp
namespace FocusGuard.Core.Data.Repositories;

public interface IScheduledSessionRepository
{
    Task<ScheduledSessionEntity> CreateAsync(ScheduledSessionEntity session);
    Task UpdateAsync(ScheduledSessionEntity session);
    Task<bool> DeleteAsync(Guid id);
    Task<ScheduledSessionEntity?> GetByIdAsync(Guid id);
    Task<List<ScheduledSessionEntity>> GetAllAsync();
    Task<List<ScheduledSessionEntity>> GetEnabledAsync();
    Task<List<ScheduledSessionEntity>> GetByDateRangeAsync(DateTime start, DateTime end);
}
```

**`src/FocusGuard.Core/Data/Repositories/ScheduledSessionRepository.cs`** — Implementation using `IDbContextFactory<FocusGuardDbContext>` (same pattern as other repositories).

### DbContext Changes

**Modify `src/FocusGuard.Core/Data/FocusGuardDbContext.cs`:**
- Add `DbSet<ScheduledSessionEntity> ScheduledSessions`
- Configure with `HasKey(e => e.Id)` and indexes on `ProfileId` and `StartTime`

### Migration

**Modify `src/FocusGuard.Core/Data/DatabaseMigrator.cs`:**
Append after existing Phase 2 migrations:
```csharp
// Phase 3: ScheduledSessions table
await context.Database.ExecuteSqlRawAsync(@"
    CREATE TABLE IF NOT EXISTS ScheduledSessions (
        Id TEXT PRIMARY KEY,
        ProfileId TEXT NOT NULL,
        StartTime TEXT NOT NULL,
        EndTime TEXT NOT NULL,
        IsRecurring INTEGER NOT NULL DEFAULT 0,
        RecurrenceRule TEXT,
        PomodoroEnabled INTEGER NOT NULL DEFAULT 0,
        IsEnabled INTEGER NOT NULL DEFAULT 1,
        CreatedAt TEXT NOT NULL
    );
");

await context.Database.ExecuteSqlRawAsync(@"
    CREATE INDEX IF NOT EXISTS IX_ScheduledSessions_ProfileId
    ON ScheduledSessions (ProfileId);
");

await context.Database.ExecuteSqlRawAsync(@"
    CREATE INDEX IF NOT EXISTS IX_ScheduledSessions_StartTime
    ON ScheduledSessions (StartTime);
");
```

### DI Registration

**Modify `src/FocusGuard.Core/ServiceCollectionExtensions.cs`:**
- Add `services.AddScoped<IScheduledSessionRepository, ScheduledSessionRepository>();`

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Data/Entities/ScheduledSessionEntity.cs` | Create |
| `src/FocusGuard.Core/Scheduling/RecurrenceRule.cs` | Create |
| `src/FocusGuard.Core/Data/Repositories/IScheduledSessionRepository.cs` | Create |
| `src/FocusGuard.Core/Data/Repositories/ScheduledSessionRepository.cs` | Create |
| `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` | Modify |
| `src/FocusGuard.Core/Data/DatabaseMigrator.cs` | Modify |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify |

**Tests:** `tests/FocusGuard.Core.Tests/Data/ScheduledSessionRepositoryTests.cs` — CRUD, date range query, enabled-only query (using InMemory provider).

**Verify:** `dotnet build && dotnet test` passes. New table exists in `focusguard.db`.

---

## Step 2: Recurrence Engine

A pure-logic engine that expands recurring schedules into concrete date-time occurrences.

### Occurrence Expander

**`src/FocusGuard.Core/Scheduling/OccurrenceExpander.cs`**
```csharp
namespace FocusGuard.Core.Scheduling;

public class OccurrenceExpander
{
    /// <summary>
    /// Expands a scheduled session into concrete occurrences within a date range.
    /// For non-recurring sessions, returns a single occurrence if it falls in range.
    /// For recurring sessions, generates all occurrences based on the recurrence rule.
    /// </summary>
    public List<ScheduledOccurrence> Expand(
        ScheduledSessionEntity session, DateTime rangeStart, DateTime rangeEnd)
    {
        if (!session.IsRecurring || string.IsNullOrEmpty(session.RecurrenceRule))
        {
            // Single occurrence — return if within range
            if (session.StartTime < rangeEnd && session.EndTime > rangeStart)
                return [new ScheduledOccurrence(session, session.StartTime, session.EndTime)];
            return [];
        }

        var rule = JsonSerializer.Deserialize<RecurrenceRule>(session.RecurrenceRule)!;
        var occurrences = new List<ScheduledOccurrence>();
        var duration = session.EndTime - session.StartTime;
        var templateTime = session.StartTime.TimeOfDay;
        var current = session.StartTime.Date;

        while (current <= rangeEnd)
        {
            if (rule.EndDate.HasValue && current > rule.EndDate.Value)
                break;

            if (ShouldOccurOn(rule, current))
            {
                var start = current + templateTime;
                var end = start + duration;
                if (start < rangeEnd && end > rangeStart)
                    occurrences.Add(new ScheduledOccurrence(session, start, end));
            }

            current = current.AddDays(1);
        }

        return occurrences;
    }

    private static bool ShouldOccurOn(RecurrenceRule rule, DateTime date)
    {
        return rule.Type switch
        {
            RecurrenceType.Daily => true,
            RecurrenceType.Weekdays => date.DayOfWeek is >= DayOfWeek.Monday
                                                     and <= DayOfWeek.Friday,
            RecurrenceType.Weekly => rule.DaysOfWeek.Contains(date.DayOfWeek),
            RecurrenceType.Custom => rule.DaysOfWeek.Contains(date.DayOfWeek),
            _ => false
        };
    }
}
```

### Occurrence Model

**`src/FocusGuard.Core/Scheduling/ScheduledOccurrence.cs`**
```csharp
namespace FocusGuard.Core.Scheduling;

public class ScheduledOccurrence
{
    public Guid ScheduledSessionId { get; init; }
    public Guid ProfileId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public bool PomodoroEnabled { get; init; }
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;

    public ScheduledOccurrence(ScheduledSessionEntity entity, DateTime start, DateTime end)
    {
        ScheduledSessionId = entity.Id;
        ProfileId = entity.ProfileId;
        StartTime = start;
        EndTime = end;
        PomodoroEnabled = entity.PomodoroEnabled;
    }
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Scheduling/OccurrenceExpander.cs` | Create |
| `src/FocusGuard.Core/Scheduling/ScheduledOccurrence.cs` | Create |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register `OccurrenceExpander` as Singleton |

**Tests:** `tests/FocusGuard.Core.Tests/Scheduling/OccurrenceExpanderTests.cs` — Single occurrence, daily recurrence, weekdays recurrence, weekly with specific days, end date cutoff, empty range, duration preservation.

**Verify:** `dotnet test` passes. Recurrence logic produces correct dates.

---

## Step 3: Scheduling Engine — Auto-Activation

A background service that monitors upcoming scheduled sessions and auto-activates them.

**`src/FocusGuard.Core/Scheduling/ISchedulingEngine.cs`**
```csharp
namespace FocusGuard.Core.Scheduling;

public interface ISchedulingEngine
{
    /// <summary>Start monitoring for upcoming scheduled sessions.</summary>
    Task StartAsync();

    /// <summary>Stop monitoring.</summary>
    void Stop();

    /// <summary>Force reload of scheduled sessions from DB.</summary>
    Task RefreshAsync();

    /// <summary>Get the next upcoming occurrence.</summary>
    ScheduledOccurrence? GetNextOccurrence();

    /// <summary>Fires when a scheduled session is about to start.</summary>
    event EventHandler<ScheduledOccurrence>? SessionStarting;

    /// <summary>Fires when a scheduled session ends.</summary>
    event EventHandler<ScheduledOccurrence>? SessionEnding;
}
```

**`src/FocusGuard.Core/Scheduling/SchedulingEngine.cs`**
```csharp
namespace FocusGuard.Core.Scheduling;

public class SchedulingEngine : ISchedulingEngine, IDisposable
{
    private readonly IScheduledSessionRepository _repository;
    private readonly OccurrenceExpander _expander;
    private readonly ILogger<SchedulingEngine> _logger;

    private System.Timers.Timer? _checkTimer;
    private List<ScheduledOccurrence> _upcomingOccurrences = [];
    private ScheduledOccurrence? _activeOccurrence;

    private const int CheckIntervalMs = 15_000; // Check every 15 seconds

    public async Task StartAsync()
    {
        await RefreshAsync();

        _checkTimer = new System.Timers.Timer(CheckIntervalMs);
        _checkTimer.Elapsed += OnCheck;
        _checkTimer.Start();
    }

    public async Task RefreshAsync()
    {
        // Load enabled sessions, expand occurrences for today + next 24 hours
        var sessions = await _repository.GetEnabledAsync();
        var now = DateTime.Now;
        _upcomingOccurrences = sessions
            .SelectMany(s => _expander.Expand(s, now, now.AddHours(24)))
            .OrderBy(o => o.StartTime)
            .ToList();
    }

    private void OnCheck(object? sender, System.Timers.ElapsedEventArgs e)
    {
        var now = DateTime.Now;

        // Check if a scheduled session should start
        var starting = _upcomingOccurrences
            .FirstOrDefault(o => o.StartTime <= now && o.EndTime > now);

        if (starting != null && _activeOccurrence?.ScheduledSessionId != starting.ScheduledSessionId)
        {
            _activeOccurrence = starting;
            SessionStarting?.Invoke(this, starting);
        }

        // Check if the active session has ended
        if (_activeOccurrence != null && now >= _activeOccurrence.EndTime)
        {
            SessionEnding?.Invoke(this, _activeOccurrence);
            _activeOccurrence = null;
        }

        // Refresh occurrences list periodically (every hour or on date change)
    }

    // ... Stop(), Dispose(), GetNextOccurrence()
}
```

### Orchestrator Integration

**Modify `src/FocusGuard.App/Services/BlockingOrchestrator.cs`:**
Subscribe to `ISchedulingEngine.SessionStarting`:
```csharp
_schedulingEngine.SessionStarting += async (_, occurrence) =>
{
    // Start a focus session via IFocusSessionManager
    await _sessionManager.StartSessionAsync(
        occurrence.ProfileId,
        occurrence.DurationMinutes,
        occurrence.PomodoroEnabled);
};

_schedulingEngine.SessionEnding += async (_, _) =>
{
    await _sessionManager.EndSessionNaturallyAsync();
};
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Scheduling/ISchedulingEngine.cs` | Create |
| `src/FocusGuard.Core/Scheduling/SchedulingEngine.cs` | Create |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register `ISchedulingEngine` as Singleton |
| `src/FocusGuard.App/Services/BlockingOrchestrator.cs` | Modify — subscribe to scheduling events |
| `src/FocusGuard.App/App.xaml.cs` | Modify — call `ISchedulingEngine.StartAsync()` at startup |

**Tests:** `tests/FocusGuard.Core.Tests/Scheduling/SchedulingEngineTests.cs` — Start fires event when session time arrives, stop prevents events, refresh picks up new sessions (using mocked repository and expander).

**Verify:** `dotnet test` passes. Scheduling engine fires events at the correct times.

---

## Step 4: Calendar UI Models

Display models bridging the data layer and the calendar view.

**`src/FocusGuard.App/Models/CalendarDay.cs`**
```csharp
namespace FocusGuard.App.Models;

public class CalendarDay
{
    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public List<CalendarTimeBlock> TimeBlocks { get; set; } = [];
}
```

**`src/FocusGuard.App/Models/CalendarTimeBlock.cs`**
```csharp
namespace FocusGuard.App.Models;

public class CalendarTimeBlock
{
    public Guid ScheduledSessionId { get; init; }
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public string ProfileColor { get; init; } = "#4A90D9";
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public bool IsRecurring { get; init; }
    public bool PomodoroEnabled { get; init; }
    public string TimeRangeDisplay => $"{StartTime:HH:mm} – {EndTime:HH:mm}";
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;
}
```

**`src/FocusGuard.App/Models/WeekDay.cs`**
```csharp
namespace FocusGuard.App.Models;

public class WeekDay
{
    public DateTime Date { get; init; }
    public string DayName => Date.ToString("ddd");
    public bool IsToday { get; init; }
    public List<CalendarTimeBlock> TimeBlocks { get; set; } = [];
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Models/CalendarDay.cs` | Create |
| `src/FocusGuard.App/Models/CalendarTimeBlock.cs` | Create |
| `src/FocusGuard.App/Models/WeekDay.cs` | Create |

**Verify:** `dotnet build` passes.

---

## Step 5: Calendar ViewModel — Monthly View

**`src/FocusGuard.App/ViewModels/CalendarViewModel.cs`**
```csharp
namespace FocusGuard.App.ViewModels;

public partial class CalendarViewModel : ViewModelBase
{
    private readonly IScheduledSessionRepository _repository;
    private readonly IProfileRepository _profileRepository;
    private readonly OccurrenceExpander _expander;
    private readonly ISchedulingEngine _schedulingEngine;
    private readonly IDialogService _dialogService;
    private readonly ILogger<CalendarViewModel> _logger;

    [ObservableProperty] private DateTime _currentMonth;
    [ObservableProperty] private string _monthYearDisplay = string.Empty; // "February 2026"
    [ObservableProperty] private bool _isWeeklyView;
    [ObservableProperty] private DateTime _selectedDate;
    [ObservableProperty] private CalendarTimeBlock? _selectedTimeBlock;

    public ObservableCollection<CalendarDay> Days { get; } = [];       // 42 cells (6 weeks × 7 days)
    public ObservableCollection<WeekDay> WeekDays { get; } = [];       // 7 days for weekly view
    public ObservableCollection<CalendarTimeBlock> SelectedDayBlocks { get; } = [];  // Side panel

    public override async void OnNavigatedTo()
    {
        CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        SelectedDate = DateTime.Today;
        await LoadMonthAsync();
    }

    [RelayCommand] private async Task PreviousMonth()
    {
        CurrentMonth = CurrentMonth.AddMonths(-1);
        await LoadMonthAsync();
    }

    [RelayCommand] private async Task NextMonth()
    {
        CurrentMonth = CurrentMonth.AddMonths(1);
        await LoadMonthAsync();
    }

    [RelayCommand] private async Task GoToToday()
    {
        CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        SelectedDate = DateTime.Today;
        await LoadMonthAsync();
    }

    [RelayCommand] private void ToggleView()
    {
        IsWeeklyView = !IsWeeklyView;
        // Reload current view
    }

    [RelayCommand] private async Task SelectDay(DateTime date)
    {
        SelectedDate = date;
        await LoadSelectedDayBlocksAsync();
    }

    [RelayCommand] private async Task CreateScheduledSession(DateTime? date)
    {
        // Show CreateScheduleDialog with date pre-filled
    }

    [RelayCommand] private async Task EditScheduledSession(CalendarTimeBlock block)
    {
        // Show CreateScheduleDialog pre-populated with existing data
    }

    [RelayCommand] private async Task DeleteScheduledSession(CalendarTimeBlock block)
    {
        // Confirm, then delete via repository, refresh
    }

    private async Task LoadMonthAsync()
    {
        MonthYearDisplay = CurrentMonth.ToString("MMMM yyyy");

        // Build 42-cell grid (always 6 rows)
        var firstDayOfMonth = CurrentMonth;
        var startDate = firstDayOfMonth.AddDays(-(int)firstDayOfMonth.DayOfWeek);
        // Adjust for Monday-first: startDate = Monday of first week

        var sessions = await _repository.GetAllAsync();
        var profiles = await _profileRepository.GetAllAsync();
        var profileMap = profiles.ToDictionary(p => p.Id);

        Days.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = startDate.AddDays(i);
            var occurrences = sessions
                .SelectMany(s => _expander.Expand(s, date, date.AddDays(1)))
                .OrderBy(o => o.StartTime)
                .Select(o => ToTimeBlock(o, profileMap))
                .ToList();

            Days.Add(new CalendarDay
            {
                Date = date,
                IsCurrentMonth = date.Month == CurrentMonth.Month,
                IsToday = date.Date == DateTime.Today,
                TimeBlocks = occurrences
            });
        }

        await LoadSelectedDayBlocksAsync();
    }

    private async Task LoadSelectedDayBlocksAsync()
    {
        // Populate SelectedDayBlocks for the side panel
    }

    private static CalendarTimeBlock ToTimeBlock(
        ScheduledOccurrence o, Dictionary<Guid, ProfileEntity> profiles)
    {
        var profile = profiles.GetValueOrDefault(o.ProfileId);
        return new CalendarTimeBlock
        {
            ScheduledSessionId = o.ScheduledSessionId,
            ProfileId = o.ProfileId,
            ProfileName = profile?.Name ?? "Unknown",
            ProfileColor = profile?.Color ?? "#4A90D9",
            StartTime = o.StartTime,
            EndTime = o.EndTime,
            PomodoroEnabled = o.PomodoroEnabled
        };
    }
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/CalendarViewModel.cs` | Create |

**Verify:** `dotnet build` passes.

---

## Step 6: Calendar View — Monthly Grid

**`src/FocusGuard.App/Views/CalendarView.xaml`**

Layout:
```
┌──────────────────────────────────────────────────────────┐
│  Calendar                        [< ] February 2026 [> ]│
│                               [Today] [Month|Week]      │
├──────────────────────────────────────┬───────────────────┤
│  Mon  Tue  Wed  Thu  Fri  Sat  Sun  │  Selected Day     │
│ ┌────┬────┬────┬────┬────┬────┬────┐│  Fri, Feb 20      │
│ │ 26 │ 27 │ 28 │ 29 │ 30 │ 31 │  1 ││  ┌─────────────┐ │
│ │    │    │    │    │    │    │    ││  │ 09:00–12:00 │ │
│ ├────┼────┼────┼────┼────┼────┼────┤│  │ ● Deep Work  │ │
│ │  2 │  3 │  4 │  5 │  6 │  7 │  8 ││  │ Pomodoro: On │ │
│ │    │    │    │    │    │    │    ││  └─────────────┘ │
│ ├────┼────┼────┼────┼────┼────┼────┤│  ┌─────────────┐ │
│ │  9 │ 10 │ 11 │ 12 │ 13 │ 14 │ 15 ││  │ 14:00–16:00 │ │
│ │    │    │■■■■│    │    │    │    ││  │ ● Study      │ │
│ │    │    │████│    │    │    │    ││  └─────────────┘ │
│ ├────┼────┼────┼────┼────┼────┼────┤│                   │
│ │ 16 │ 17 │ 18 │ 19 │ 20 │ 21 │ 22 ││  [+ New Session]  │
│ │    │    │    │    │████│    │    ││                   │
│ ├────┼────┼────┼────┼────┼────┼────┤│                   │
│ │ 23 │ 24 │ 25 │ 26 │ 27 │ 28 │  1 ││                   │
│ └────┴────┴────┴────┴────┴────┴────┘│                   │
└──────────────────────────────────────┴───────────────────┘
```

Key XAML structure:
```xml
<UserControl x:Class="FocusGuard.App.Views.CalendarView" ...>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />         <!-- Calendar grid -->
            <ColumnDefinition Width="260" />       <!-- Side panel -->
        </Grid.ColumnDefinitions>

        <!-- Left: Calendar -->
        <DockPanel Grid.Column="0">
            <!-- Top bar: nav arrows, month title, view toggle -->
            <Grid DockPanel.Dock="Top">
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Left">
                    <Button Content="◀" Command="{Binding PreviousMonthCommand}" />
                    <TextBlock Text="{Binding MonthYearDisplay}" Style="{StaticResource HeadingStyle}" />
                    <Button Content="▶" Command="{Binding NextMonthCommand}" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                    <Button Content="Today" Command="{Binding GoToTodayCommand}" />
                    <Button Content="Month / Week" Command="{Binding ToggleViewCommand}" />
                </StackPanel>
            </Grid>

            <!-- Day-of-week headers -->
            <UniformGrid DockPanel.Dock="Top" Columns="7" Rows="1">
                <!-- Mon, Tue, Wed, ... Sun -->
            </UniformGrid>

            <!-- 6×7 grid of day cells -->
            <ItemsControl ItemsSource="{Binding Days}">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <UniformGrid Columns="7" Rows="6" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border ... Opacity="{Binding IsCurrentMonth, ...}">
                            <StackPanel>
                                <TextBlock Text="{Binding Date.Day}" />
                                <!-- Color dots/bars for time blocks -->
                                <ItemsControl ItemsSource="{Binding TimeBlocks}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Border Height="4" Margin="1"
                                                CornerRadius="2"
                                                Background="{Binding ProfileColor, Converter=...}" />
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </DockPanel>

        <!-- Right: Selected day detail panel -->
        <Border Grid.Column="1" Background="{StaticResource SurfaceBrush}" ...>
            <DockPanel>
                <TextBlock DockPanel.Dock="Top" Text="{Binding SelectedDate, StringFormat='ddd, MMM d'}" />
                <Button DockPanel.Dock="Bottom" Content="+ New Session"
                        Command="{Binding CreateScheduledSessionCommand}" />
                <ItemsControl ItemsSource="{Binding SelectedDayBlocks}">
                    <!-- Time block cards with profile color, time, name, edit/delete -->
                </ItemsControl>
            </DockPanel>
        </Border>
    </Grid>
</UserControl>
```

**`src/FocusGuard.App/Views/CalendarView.xaml.cs`** — Parameterless constructor.

Day cells support click-to-select (via `InputBindings` or attached behavior). Selected day is highlighted with primary-color border. Today cell has a distinct background accent.

Time blocks in the monthly grid are shown as thin colored bars (profile color). The side panel shows full detail cards for the selected day.

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/CalendarView.xaml` | Create |
| `src/FocusGuard.App/Views/CalendarView.xaml.cs` | Create |

**Verify:** Calendar view renders with 42-cell grid. Clicking a day updates the side panel.

---

## Step 7: Create / Edit Scheduled Session Dialog

**`src/FocusGuard.App/Views/ScheduleSessionDialog.xaml` + `.xaml.cs`**

A modal dialog for creating or editing a scheduled session:
- **Profile picker:** ComboBox listing all profiles (color dot + name)
- **Date picker:** `DatePicker` for the session date (or first occurrence date for recurring)
- **Start time:** TextBox with `HH:mm` format (hour:minute spinners or masked input)
- **End time:** TextBox with `HH:mm` format
- **Duration display:** Auto-calculated label (e.g., "2h 30m")
- **Pomodoro toggle:** CheckBox "Enable Pomodoro Mode"
- **Recurring section:**
  - CheckBox "Repeat this session"
  - When checked, shows:
    - Recurrence type: ComboBox (Daily / Weekdays / Weekly / Custom)
    - Day checkboxes (for Weekly/Custom): Mon, Tue, Wed, Thu, Fri, Sat, Sun
    - Optional end date: DatePicker or "No end date"
- **Buttons:** "Save" (primary), "Cancel" (secondary)

**`src/FocusGuard.App/ViewModels/ScheduleSessionDialogViewModel.cs`**
```csharp
namespace FocusGuard.App.ViewModels;

public partial class ScheduleSessionDialogViewModel : ObservableObject
{
    [ObservableProperty] private ProfileListItem? _selectedProfile;
    [ObservableProperty] private DateTime _sessionDate = DateTime.Today;
    [ObservableProperty] private TimeSpan _startTime = new(9, 0, 0);
    [ObservableProperty] private TimeSpan _endTime = new(10, 0, 0);
    [ObservableProperty] private bool _pomodoroEnabled;
    [ObservableProperty] private bool _isRecurring;
    [ObservableProperty] private RecurrenceType _recurrenceType = RecurrenceType.Weekdays;
    [ObservableProperty] private bool _mondaySelected;
    [ObservableProperty] private bool _tuesdaySelected;
    [ObservableProperty] private bool _wednesdaySelected;
    [ObservableProperty] private bool _thursdaySelected;
    [ObservableProperty] private bool _fridaySelected;
    [ObservableProperty] private bool _saturdaySelected;
    [ObservableProperty] private bool _sundaySelected;
    [ObservableProperty] private DateTime? _recurrenceEndDate;
    [ObservableProperty] private bool _hasEndDate;
    [ObservableProperty] private string _durationDisplay = string.Empty;

    public ObservableCollection<ProfileListItem> Profiles { get; } = [];

    public bool Confirmed { get; set; }
    public Guid? EditingSessionId { get; set; } // Non-null when editing

    partial void OnStartTimeChanged(TimeSpan value) => UpdateDuration();
    partial void OnEndTimeChanged(TimeSpan value) => UpdateDuration();

    private void UpdateDuration()
    {
        var duration = EndTime - StartTime;
        if (duration.TotalMinutes <= 0) duration = TimeSpan.FromHours(24) + duration; // Overnight
        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;
        DurationDisplay = hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }

    [RelayCommand] private void Confirm() { Confirmed = true; /* Close dialog */ }
    [RelayCommand] private void Cancel() { Confirmed = false; /* Close dialog */ }
}
```

**`src/FocusGuard.App/Models/ScheduleSessionDialogResult.cs`**
```csharp
namespace FocusGuard.App.Models;

public class ScheduleSessionDialogResult
{
    public Guid ProfileId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public bool PomodoroEnabled { get; init; }
    public bool IsRecurring { get; init; }
    public RecurrenceRule? RecurrenceRule { get; init; }
}
```

### Dialog Service Extension

**Modify `src/FocusGuard.App/Services/IDialogService.cs`:**
```csharp
Task<ScheduleSessionDialogResult?> ShowScheduleSessionDialogAsync(
    DateTime? prefilledDate = null,
    ScheduledSessionEntity? existingSession = null);
```

**Modify `src/FocusGuard.App/Services/DialogService.cs`:** — Implement the new dialog.

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/ScheduleSessionDialog.xaml` | Create |
| `src/FocusGuard.App/Views/ScheduleSessionDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/ScheduleSessionDialogViewModel.cs` | Create |
| `src/FocusGuard.App/Models/ScheduleSessionDialogResult.cs` | Create |
| `src/FocusGuard.App/Services/IDialogService.cs` | Modify |
| `src/FocusGuard.App/Services/DialogService.cs` | Modify |

**Verify:** Dialog opens, profile picker works, recurrence section shows/hides, duration auto-calculates.

---

## Step 8: Weekly View

An alternative view showing a 7-day week with hour-granularity rows.

**`src/FocusGuard.App/Views/WeeklyCalendarView.xaml`**

Layout:
```
┌─────────────────────────────────────────────────────────────┐
│          Mon 16   Tue 17   Wed 18   Thu 19   Fri 20   ...  │
├─────────┬────────┬────────┬────────┬────────┬────────┬─────┤
│  06:00  │        │        │        │        │        │     │
│  07:00  │        │        │        │        │        │     │
│  08:00  │        │        │        │        │        │     │
│  09:00  │ ██████ │        │ ██████ │        │ ██████ │     │
│  10:00  │ Deep   │        │ Deep   │        │ Deep   │     │
│  11:00  │ Work   │        │ Work   │        │ Work   │     │
│  12:00  │        │        │        │        │        │     │
│  13:00  │        │        │        │        │        │     │
│  14:00  │        │ ██████ │        │ ██████ │        │     │
│  15:00  │        │ Study  │        │ Study  │        │     │
│  16:00  │        │        │        │        │        │     │
│  ...    │        │        │        │        │        │     │
└─────────┴────────┴────────┴────────┴────────┴────────┴─────┘
```

This is a `Grid` with 7 columns (one per day) and 24 rows (one per hour, typically showing 06:00–22:00 range). Time blocks are positioned absolutely within each column using `Canvas` or `Grid` row offsets calculated from start/end time.

Time block rendering:
```csharp
// Position within the day column:
double topPixels = (block.StartTime.Hour - firstVisibleHour) * hourHeight
                   + block.StartTime.Minute * hourHeight / 60.0;
double heightPixels = block.DurationMinutes * hourHeight / 60.0;
```

Integrated as a content swap within `CalendarView.xaml`:
```xml
<!-- In CalendarView.xaml, toggle between monthly and weekly -->
<ContentControl>
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="Content" Value="{Binding}" />
            <Setter Property="ContentTemplate" Value="{StaticResource MonthlyViewTemplate}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsWeeklyView}" Value="True">
                    <Setter Property="ContentTemplate" Value="{StaticResource WeeklyViewTemplate}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

### Week Navigation

Add to `CalendarViewModel`:
```csharp
[ObservableProperty] private DateTime _weekStart; // Monday of current week

[RelayCommand] private async Task PreviousWeek()
{
    _weekStart = _weekStart.AddDays(-7);
    await LoadWeekAsync();
}

[RelayCommand] private async Task NextWeek()
{
    _weekStart = _weekStart.AddDays(7);
    await LoadWeekAsync();
}

private async Task LoadWeekAsync()
{
    WeekDays.Clear();
    for (var i = 0; i < 7; i++)
    {
        var date = _weekStart.AddDays(i);
        var occurrences = /* expand for this day */;
        WeekDays.Add(new WeekDay { Date = date, IsToday = date == DateTime.Today, TimeBlocks = ... });
    }
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/CalendarView.xaml` | Modify — add weekly view DataTemplate |
| `src/FocusGuard.App/ViewModels/CalendarViewModel.cs` | Modify — add week navigation commands |

**Verify:** Toggle between month and week views. Weekly view shows hour grid with positioned time blocks.

---

## Step 9: Drag-and-Drop Session Creation (Weekly View)

In the weekly view, users can click and drag vertically within a day column to create a new time block.

### Drag Logic (Code-Behind)

**Modify `src/FocusGuard.App/Views/CalendarView.xaml.cs`:**

```csharp
private DateTime? _dragStartTime;
private int _dragDayIndex;
private Border? _dragPreview;

private void WeekColumn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    var column = (FrameworkElement)sender;
    var y = e.GetPosition(column).Y;
    _dragStartTime = CalculateTimeFromY(y);
    _dragDayIndex = GetDayIndex(column);
    column.CaptureMouse();

    // Create a semi-transparent preview rectangle
    _dragPreview = new Border
    {
        Background = new SolidColorBrush(Color.FromArgb(80, 74, 144, 217)),
        CornerRadius = new CornerRadius(4),
        IsHitTestVisible = false
    };
    // Add to canvas overlay
}

private void WeekColumn_MouseMove(object sender, MouseEventArgs e)
{
    if (_dragStartTime == null || _dragPreview == null) return;
    var y = e.GetPosition((FrameworkElement)sender).Y;
    var currentTime = CalculateTimeFromY(y);
    // Update _dragPreview size and position
}

private void WeekColumn_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
{
    if (_dragStartTime == null) return;

    var column = (FrameworkElement)sender;
    var y = e.GetPosition(column).Y;
    var endTime = CalculateTimeFromY(y);
    column.ReleaseMouseCapture();

    // Remove preview, open ScheduleSessionDialog with pre-filled times
    var vm = DataContext as CalendarViewModel;
    vm?.CreateScheduledSessionCommand.Execute(/* date with drag times */);

    _dragStartTime = null;
    _dragPreview = null;
}

private DateTime CalculateTimeFromY(double y)
{
    // Convert pixel position to time based on hour height and first visible hour
    var hours = y / _hourHeight + _firstVisibleHour;
    var h = (int)hours;
    var m = (int)((hours - h) * 60);
    m = (m / 15) * 15; // Snap to 15-minute intervals
    return SelectedDate.Date.AddHours(h).AddMinutes(m);
}
```

Time snaps to 15-minute intervals for clean scheduling. The drag preview uses a semi-transparent primary color overlay.

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/CalendarView.xaml.cs` | Modify — add drag-and-drop event handlers |
| `src/FocusGuard.App/Views/CalendarView.xaml` | Modify — add mouse event bindings to week columns |

**Verify:** In weekly view, click and drag vertically to define a time range. On release, the schedule dialog opens with the times pre-filled.

---

## Step 10: Today View on Dashboard

Add an "Upcoming Sessions" section to the dashboard showing today's schedule.

**Modify `src/FocusGuard.App/ViewModels/DashboardViewModel.cs`:**
```csharp
// New properties:
public ObservableCollection<CalendarTimeBlock> TodaySessions { get; } = [];

// In OnNavigatedTo():
await LoadTodaySessionsAsync();

private async Task LoadTodaySessionsAsync()
{
    var sessions = await _scheduledSessionRepository.GetEnabledAsync();
    var today = DateTime.Today;
    var occurrences = sessions
        .SelectMany(s => _expander.Expand(s, today, today.AddDays(1)))
        .OrderBy(o => o.StartTime)
        .ToList();

    TodaySessions.Clear();
    foreach (var o in occurrences)
    {
        TodaySessions.Add(ToTimeBlock(o));
    }
}
```

**Modify `src/FocusGuard.App/Views/DashboardView.xaml`:**
Replace the "Coming Soon — Calendar" placeholder with:
```xml
<!-- Today's Schedule -->
<TextBlock Text="Today's Schedule" Style="{StaticResource SubheadingStyle}" Margin="0,16,0,12" />
<ItemsControl ItemsSource="{Binding TodaySessions}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Style="{StaticResource CardStyle}" Padding="12" Margin="0,0,0,8">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="4" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <Border Grid.Column="0" CornerRadius="2"
                            Background="{Binding ProfileColor, Converter={StaticResource HexToColorConverter}}" />
                    <StackPanel Grid.Column="1" Margin="12,0,0,0">
                        <TextBlock Text="{Binding ProfileName}" FontWeight="SemiBold" />
                        <TextBlock Text="{Binding TimeRangeDisplay}"
                                   Foreground="{StaticResource TextSecondaryBrush}" FontSize="12" />
                    </StackPanel>
                </Grid>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | Modify |
| `src/FocusGuard.App/Views/DashboardView.xaml` | Modify |

**Verify:** Dashboard shows today's scheduled sessions with profile color bars and time ranges.

---

## Step 11: Navigation Wiring & DI

Enable the Calendar button and wire all navigation.

### MainWindowViewModel

**Modify `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs`:**
```csharp
[RelayCommand]
private void NavigateToCalendar() => _navigationService.NavigateTo<CalendarViewModel>();
```

### MainWindow.xaml

**Modify `src/FocusGuard.App/Views/MainWindow.xaml`:**
```xml
<!-- Change Calendar button from disabled to enabled -->
<Button Content="&#x1F4C5;  Calendar"
        Style="{StaticResource NavButtonStyle}"
        Command="{Binding NavigateToCalendarCommand}" />
```

Update version string:
```xml
<TextBlock DockPanel.Dock="Bottom" Text="v1.0.0 — Phase 3"
```

### App.xaml

**Modify `src/FocusGuard.App/App.xaml`:**
Add DataTemplate mapping:
```xml
<DataTemplate DataType="{x:Type viewModels:CalendarViewModel}">
    <views:CalendarView />
</DataTemplate>
```

### App.xaml.cs

**Modify `src/FocusGuard.App/App.xaml.cs`:**
```csharp
// Add to DI registration:
services.AddTransient<CalendarViewModel>();

// Add at startup after DatabaseMigrator:
var schedulingEngine = Services.GetRequiredService<ISchedulingEngine>();
await schedulingEngine.StartAsync();
```

### Full DI Registration (Core)

**Modify `src/FocusGuard.Core/ServiceCollectionExtensions.cs`:**
```csharp
// Repositories (add):
services.AddScoped<IScheduledSessionRepository, ScheduledSessionRepository>();

// Scheduling (add):
services.AddSingleton<OccurrenceExpander>();
services.AddSingleton<ISchedulingEngine, SchedulingEngine>();
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs` | Modify |
| `src/FocusGuard.App/Views/MainWindow.xaml` | Modify |
| `src/FocusGuard.App/App.xaml` | Modify |
| `src/FocusGuard.App/App.xaml.cs` | Modify |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify (final) |

**Verify:** Calendar button is enabled. Clicking it navigates to the calendar view. Scheduling engine starts at app launch.

---

## Step Dependency Graph

```
Step 1  (DB Schema)
  ↓
Step 2  (Recurrence Engine)  ← depends on Step 1
  ↓
Step 3  (Scheduling Engine)  ← depends on Steps 1 + 2
  ↓
Step 4  (UI Models)
  ↓
Step 5  (Calendar ViewModel) ← depends on Steps 1 + 2 + 4
  ↓
Step 6  (Calendar View — Monthly)  ← depends on Step 5
  ↓
Step 7  (Schedule Dialog)  ← depends on Steps 1 + 4
  ↓
Step 8  (Weekly View)  ← depends on Steps 5 + 6
  ↓
Step 9  (Drag-and-Drop)  ← depends on Steps 7 + 8
  ↓
Step 10 (Today View on Dashboard)  ← depends on Steps 2 + 4
  ↓
Step 11 (Navigation & DI Wiring)  ← depends on all above
```

Steps 1–3 are sequential backend. Steps 4–6 are sequential UI. Steps 7 and 10 can be parallelized after their prerequisites.

---

## File Summary

### New Files (~16)

| # | File | Step |
|---|------|------|
| 1 | `src/FocusGuard.Core/Data/Entities/ScheduledSessionEntity.cs` | 1 |
| 2 | `src/FocusGuard.Core/Scheduling/RecurrenceRule.cs` | 1 |
| 3 | `src/FocusGuard.Core/Data/Repositories/IScheduledSessionRepository.cs` | 1 |
| 4 | `src/FocusGuard.Core/Data/Repositories/ScheduledSessionRepository.cs` | 1 |
| 5 | `src/FocusGuard.Core/Scheduling/ScheduledOccurrence.cs` | 2 |
| 6 | `src/FocusGuard.Core/Scheduling/OccurrenceExpander.cs` | 2 |
| 7 | `src/FocusGuard.Core/Scheduling/ISchedulingEngine.cs` | 3 |
| 8 | `src/FocusGuard.Core/Scheduling/SchedulingEngine.cs` | 3 |
| 9 | `src/FocusGuard.App/Models/CalendarDay.cs` | 4 |
| 10 | `src/FocusGuard.App/Models/CalendarTimeBlock.cs` | 4 |
| 11 | `src/FocusGuard.App/Models/WeekDay.cs` | 4 |
| 12 | `src/FocusGuard.App/ViewModels/CalendarViewModel.cs` | 5 |
| 13 | `src/FocusGuard.App/Views/CalendarView.xaml` | 6 |
| 14 | `src/FocusGuard.App/Views/CalendarView.xaml.cs` | 6 |
| 15 | `src/FocusGuard.App/Views/ScheduleSessionDialog.xaml` | 7 |
| 16 | `src/FocusGuard.App/Views/ScheduleSessionDialog.xaml.cs` | 7 |
| 17 | `src/FocusGuard.App/ViewModels/ScheduleSessionDialogViewModel.cs` | 7 |
| 18 | `src/FocusGuard.App/Models/ScheduleSessionDialogResult.cs` | 7 |

### New Test Files (3)

| # | File | Step |
|---|------|------|
| 1 | `tests/FocusGuard.Core.Tests/Data/ScheduledSessionRepositoryTests.cs` | 1 |
| 2 | `tests/FocusGuard.Core.Tests/Scheduling/OccurrenceExpanderTests.cs` | 2 |
| 3 | `tests/FocusGuard.Core.Tests/Scheduling/SchedulingEngineTests.cs` | 3 |

### Modified Files (~12)

| # | File | Steps |
|---|------|-------|
| 1 | `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` | 1 |
| 2 | `src/FocusGuard.Core/Data/DatabaseMigrator.cs` | 1 |
| 3 | `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | 1, 2, 3, 11 |
| 4 | `src/FocusGuard.App/Services/BlockingOrchestrator.cs` | 3 |
| 5 | `src/FocusGuard.App/App.xaml.cs` | 11 |
| 6 | `src/FocusGuard.App/App.xaml` | 11 |
| 7 | `src/FocusGuard.App/Views/MainWindow.xaml` | 11 |
| 8 | `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs` | 11 |
| 9 | `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | 10 |
| 10 | `src/FocusGuard.App/Views/DashboardView.xaml` | 10 |
| 11 | `src/FocusGuard.App/Services/IDialogService.cs` | 7 |
| 12 | `src/FocusGuard.App/Services/DialogService.cs` | 7 |

---

## Risk Mitigations

| Risk | Mitigation |
|------|------------|
| **Recurring sessions produce too many occurrences** | `OccurrenceExpander` only expands within the requested date range (typically 1 month or 24 hours). Never expand unbounded. |
| **Scheduling engine misses exact start time** | 15-second polling is adequate. A ±15s variance on session start is acceptable for scheduled sessions. |
| **Drag-and-drop on touch screens** | WPF mouse events work on touch via promotion. 15-minute snap grid ensures usable block sizes even on imprecise input. |
| **Timezone / DST changes** | Store and compare in local time (`DateTime.Now`). Scheduled sessions are user-facing local-time concepts, not UTC. Log timezone in session metadata for debugging. |
| **Calendar grid performance with many sessions** | Each day cell shows at most 3–4 colored bars (truncate with "+N more" indicator). Full details are in the side panel only. |
| **Conflicting schedules (overlapping time blocks)** | Allow overlaps — the scheduling engine activates the first matching session. Display a warning icon on overlapping blocks in the calendar view. |
| **Orphaned sessions after profile deletion** | When a profile is deleted, cascade-disable its scheduled sessions (`IsEnabled = false`). Display "Unknown Profile" for orphaned blocks. |
| **Monthly grid date math edge cases** | Always render exactly 42 cells (6 weeks). Start from the Monday of the first week that contains the 1st. This avoids partial-week rendering issues. |
| **Weekly view hour alignment** | Use a fixed `hourHeight` constant (e.g., 60px). Time blocks calculate their position from this constant. Scrollable container handles hours outside viewport. |

---

## End-to-End Verification Checklist

1. `dotnet build` — no errors
2. `dotnet test` — all existing + new tests pass
3. **Calendar navigation:** Click Calendar in sidebar → monthly grid renders → click ◀/▶ → month changes → click "Today" → returns to current month
4. **Day selection:** Click a day cell → side panel updates → shows sessions for that day
5. **Create session:** Click "+ New Session" → dialog opens → pick profile, set times, save → block appears on calendar
6. **Recurring session:** Create session with "Weekdays" recurrence → colored bars appear on Mon–Fri across the month
7. **Edit session:** Click a time block in side panel → dialog opens pre-populated → modify → save → calendar updates
8. **Delete session:** Click delete on a time block → confirm → block removed from calendar
9. **Weekly view:** Toggle to week view → hour grid renders → time blocks positioned correctly → navigate weeks
10. **Drag-and-drop:** In weekly view, click and drag on a day column → preview appears → release → dialog opens with pre-filled times
11. **Today view:** Dashboard shows today's scheduled sessions with profile colors and times
12. **Auto-activation:** Create a session starting 1 minute from now → wait → session auto-starts → blocking activates → session auto-ends at scheduled time
13. **Profile color coding:** Sessions display the correct profile color in all views (monthly bars, side panel cards, weekly blocks, dashboard)
14. **Schedule persistence:** Create sessions → restart app → sessions still appear on calendar
