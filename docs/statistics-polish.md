# FocusGuard Phase 4: Statistics & Polish — Implementation Plan

## Context

Phase 1 (Core Foundation) is complete. Phase 2 (Focus Session & Timer) adds session lifecycle, password unlock, Pomodoro timer, system tray, and floating overlay. Phase 3 (Calendar & Scheduling) adds calendar UI, drag-and-drop scheduling, recurring sessions, and auto-activation.

Phase 4 adds statistics & analytics, focus goals, streak tracking, desktop notifications, auto-start on boot, and CSV export — per `requirements.md` §2.5, §3.2, §3.3, §3.4, and §7 Phase 4.

**Prerequisite:** Phase 3 code builds and tests pass (`dotnet build && dotnet test`). Phase 2/3's `IFocusSessionManager`, `BlockingOrchestrator`, `PomodoroTimer`, `ISchedulingEngine`, and tray integration are fully functional.

---

## Step 1: Blocked Attempt Logging — Data Layer

### New Entity

**`src/FocusGuard.Core/Data/Entities/BlockedAttemptEntity.cs`**
```csharp
namespace FocusGuard.Core.Data.Entities;

public class BlockedAttemptEntity
{
    public Guid Id { get; set; }
    public Guid? SessionId { get; set; }      // FK to FocusSessions (null if no active session)
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = "Application";  // "Website" or "Application"
    public string Target { get; set; } = string.Empty;  // Domain or process name
}
```

### Repository

**`src/FocusGuard.Core/Data/Repositories/IBlockedAttemptRepository.cs`**
```csharp
namespace FocusGuard.Core.Data.Repositories;

public interface IBlockedAttemptRepository
{
    Task CreateAsync(BlockedAttemptEntity attempt);
    Task<List<BlockedAttemptEntity>> GetBySessionIdAsync(Guid sessionId);
    Task<List<BlockedAttemptEntity>> GetByDateRangeAsync(DateTime start, DateTime end);
    Task<int> GetCountByDateRangeAsync(DateTime start, DateTime end);
}
```

**`src/FocusGuard.Core/Data/Repositories/BlockedAttemptRepository.cs`** — Implementation using `IDbContextFactory<FocusGuardDbContext>` (same pattern as other repositories).

### DbContext Changes

**Modify `src/FocusGuard.Core/Data/FocusGuardDbContext.cs`:**
- Add `DbSet<BlockedAttemptEntity> BlockedAttempts`
- Configure with `HasKey(e => e.Id)` and indexes on `SessionId` and `Timestamp`

### Migration

**Modify `src/FocusGuard.Core/Data/DatabaseMigrator.cs`:**
Append after existing Phase 3 migrations:
```csharp
// Phase 4: BlockedAttemptLogs table
await context.Database.ExecuteSqlRawAsync(@"
    CREATE TABLE IF NOT EXISTS BlockedAttempts (
        Id TEXT PRIMARY KEY,
        SessionId TEXT,
        Timestamp TEXT NOT NULL,
        Type TEXT NOT NULL DEFAULT 'Application',
        Target TEXT NOT NULL DEFAULT ''
    );
");

await context.Database.ExecuteSqlRawAsync(@"
    CREATE INDEX IF NOT EXISTS IX_BlockedAttempts_SessionId
    ON BlockedAttempts (SessionId);
");

await context.Database.ExecuteSqlRawAsync(@"
    CREATE INDEX IF NOT EXISTS IX_BlockedAttempts_Timestamp
    ON BlockedAttempts (Timestamp);
");
```

### Blocked Attempt Logger Service

**`src/FocusGuard.Core/Statistics/BlockedAttemptLogger.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class BlockedAttemptLogger : IDisposable
{
    private readonly IBlockedAttemptRepository _repository;
    private readonly IApplicationBlocker _applicationBlocker;
    private readonly IFocusSessionManager _sessionManager;
    private readonly ILogger<BlockedAttemptLogger> _logger;

    // Constructor: subscribe to IApplicationBlocker.ProcessBlocked event
    // On ProcessBlocked: create BlockedAttemptEntity with Type="Application",
    //   Target = processName, SessionId = current session ID (if any)

    // Note: Website blocking via hosts file doesn't produce events — the OS
    // silently redirects. Blocked website attempts cannot be tracked at this layer.
    // Future: could parse DNS logs or use a local proxy, but out of scope for Phase 4.

    public void Dispose() { /* Unsubscribe from events */ }
}
```

### DI Registration

**Modify `src/FocusGuard.Core/ServiceCollectionExtensions.cs`:**
- Add `services.AddSingleton<IBlockedAttemptRepository, BlockedAttemptRepository>();`
- Add `services.AddSingleton<BlockedAttemptLogger>();`

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Data/Entities/BlockedAttemptEntity.cs` | Create |
| `src/FocusGuard.Core/Data/Repositories/IBlockedAttemptRepository.cs` | Create |
| `src/FocusGuard.Core/Data/Repositories/BlockedAttemptRepository.cs` | Create |
| `src/FocusGuard.Core/Statistics/BlockedAttemptLogger.cs` | Create |
| `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` | Modify |
| `src/FocusGuard.Core/Data/DatabaseMigrator.cs` | Modify |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify |

**Tests:** `tests/FocusGuard.Core.Tests/Data/BlockedAttemptRepositoryTests.cs` — Create, query by session, query by date range, count (using InMemory provider).

**Verify:** `dotnet build && dotnet test` passes. New table exists in `focusguard.db`. Blocked app attempts logged during active sessions.

---

## Step 2: Statistics Aggregation Service

Pure query service that computes analytics from `FocusSessions` and `BlockedAttempts` data.

### Statistics Models

**`src/FocusGuard.Core/Statistics/DailyFocusSummary.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class DailyFocusSummary
{
    public DateTime Date { get; init; }
    public int TotalFocusMinutes { get; init; }
    public int SessionCount { get; init; }
    public int PomodoroCount { get; init; }
    public int BlockedAttemptCount { get; init; }
}
```

**`src/FocusGuard.Core/Statistics/ProfileFocusSummary.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class ProfileFocusSummary
{
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public string ProfileColor { get; init; } = "#4A90D9";
    public int TotalFocusMinutes { get; init; }
    public int SessionCount { get; init; }
}
```

**`src/FocusGuard.Core/Statistics/StreakInfo.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class StreakInfo
{
    public int CurrentStreak { get; init; }    // Consecutive days with completed sessions
    public int LongestStreak { get; init; }    // All-time longest streak
    public DateTime? StreakStartDate { get; init; }
}
```

**`src/FocusGuard.Core/Statistics/PeriodStatistics.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class PeriodStatistics
{
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }
    public int TotalFocusMinutes { get; init; }
    public int TotalSessions { get; init; }
    public int TotalPomodoros { get; init; }
    public int TotalBlockedAttempts { get; init; }
    public int EarlyUnlockCount { get; init; }
    public double AverageSessionMinutes { get; init; }
    public List<DailyFocusSummary> DailyBreakdown { get; init; } = [];
    public List<ProfileFocusSummary> ProfileBreakdown { get; init; } = [];
    public StreakInfo Streak { get; init; } = new();
}
```

### Service Interface & Implementation

**`src/FocusGuard.Core/Statistics/IStatisticsService.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public interface IStatisticsService
{
    Task<PeriodStatistics> GetStatisticsAsync(DateTime start, DateTime end);
    Task<StreakInfo> GetStreakInfoAsync();
    Task<List<DailyFocusSummary>> GetDailyFocusAsync(DateTime start, DateTime end);
    Task<List<ProfileFocusSummary>> GetProfileBreakdownAsync(DateTime start, DateTime end);
}
```

**`src/FocusGuard.Core/Statistics/StatisticsService.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class StatisticsService : IStatisticsService
{
    private readonly IFocusSessionRepository _sessionRepository;
    private readonly IBlockedAttemptRepository _attemptRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly ILogger<StatisticsService> _logger;

    // GetStatisticsAsync: query sessions in date range, group by day, calculate totals
    // GetStreakInfoAsync: walk backward from today, count consecutive days with ≥1 completed session
    // GetDailyFocusAsync: group sessions by date, sum ActualDurationMinutes
    // GetProfileBreakdownAsync: group sessions by ProfileId, join with profile names/colors

    // Note: needs a new method on IFocusSessionRepository:
    //   Task<List<FocusSessionEntity>> GetByDateRangeAsync(DateTime start, DateTime end);
}
```

### Repository Extension

**Modify `src/FocusGuard.Core/Data/Repositories/IFocusSessionRepository.cs`:**
- Add `Task<List<FocusSessionEntity>> GetByDateRangeAsync(DateTime start, DateTime end);`

**Modify `src/FocusGuard.Core/Data/Repositories/FocusSessionRepository.cs`:**
- Implement `GetByDateRangeAsync`: query `FocusSessions` where `State == "Ended"` and `StartTime` falls within range, ordered by `StartTime desc`.

### DI Registration

**Modify `src/FocusGuard.Core/ServiceCollectionExtensions.cs`:**
- Add `services.AddSingleton<IStatisticsService, StatisticsService>();`

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Statistics/DailyFocusSummary.cs` | Create |
| `src/FocusGuard.Core/Statistics/ProfileFocusSummary.cs` | Create |
| `src/FocusGuard.Core/Statistics/StreakInfo.cs` | Create |
| `src/FocusGuard.Core/Statistics/PeriodStatistics.cs` | Create |
| `src/FocusGuard.Core/Statistics/IStatisticsService.cs` | Create |
| `src/FocusGuard.Core/Statistics/StatisticsService.cs` | Create |
| `src/FocusGuard.Core/Data/Repositories/IFocusSessionRepository.cs` | Modify |
| `src/FocusGuard.Core/Data/Repositories/FocusSessionRepository.cs` | Modify |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify |

**Tests:** `tests/FocusGuard.Core.Tests/Statistics/StatisticsServiceTests.cs` — Period totals, daily breakdown, profile breakdown, streak calculation (no sessions → 0, consecutive days → correct count, gap breaks streak), empty date range returns zeroes. Uses mocked repositories.

**Verify:** `dotnet test` passes. Statistics queries return correct aggregations.

---

## Step 3: Focus Goals

### Goal Models

**`src/FocusGuard.Core/Statistics/FocusGoal.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class FocusGoal
{
    public GoalPeriod Period { get; set; }            // Daily or Weekly
    public int TargetMinutes { get; set; }            // e.g., 240 (4 hours)
    public Guid? ProfileId { get; set; }              // Null = global goal, non-null = per-profile
}

public enum GoalPeriod
{
    Daily,
    Weekly
}
```

**`src/FocusGuard.Core/Statistics/GoalProgress.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class GoalProgress
{
    public FocusGoal Goal { get; init; } = new();
    public int CurrentMinutes { get; init; }
    public double CompletionPercent => Goal.TargetMinutes > 0
        ? Math.Min(100.0, (double)CurrentMinutes / Goal.TargetMinutes * 100)
        : 0;
    public bool IsCompleted => CurrentMinutes >= Goal.TargetMinutes;
    public int RemainingMinutes => Math.Max(0, Goal.TargetMinutes - CurrentMinutes);
}
```

### Goal Service

**`src/FocusGuard.Core/Statistics/IGoalService.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public interface IGoalService
{
    Task<FocusGoal?> GetGoalAsync(GoalPeriod period, Guid? profileId = null);
    Task SetGoalAsync(FocusGoal goal);
    Task RemoveGoalAsync(GoalPeriod period, Guid? profileId = null);
    Task<GoalProgress?> GetProgressAsync(GoalPeriod period, Guid? profileId = null);
    Task<List<GoalProgress>> GetAllProgressAsync();
}
```

**`src/FocusGuard.Core/Statistics/GoalService.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class GoalService : IGoalService
{
    private readonly ISettingsRepository _settings;
    private readonly IStatisticsService _statistics;
    private readonly ILogger<GoalService> _logger;

    // Storage: goals serialized as JSON in Settings table
    //   Key: "goal.daily" or "goal.weekly" or "goal.daily.{profileId}"
    //   Value: JSON { "targetMinutes": 240 }

    // GetProgressAsync: load goal from settings, query statistics for current
    //   day/week, calculate completion percentage
    // GetAllProgressAsync: enumerate all goal keys, calculate progress for each
}
```

### Settings Keys

**Modify `src/FocusGuard.Core/Security/SettingsKeys.cs`:**
```csharp
// Goals
public const string DailyGoalPrefix = "goal.daily";
public const string WeeklyGoalPrefix = "goal.weekly";
```

### DI Registration

**Modify `src/FocusGuard.Core/ServiceCollectionExtensions.cs`:**
- Add `services.AddSingleton<IGoalService, GoalService>();`

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Statistics/FocusGoal.cs` | Create |
| `src/FocusGuard.Core/Statistics/GoalProgress.cs` | Create |
| `src/FocusGuard.Core/Statistics/IGoalService.cs` | Create |
| `src/FocusGuard.Core/Statistics/GoalService.cs` | Create |
| `src/FocusGuard.Core/Security/SettingsKeys.cs` | Modify |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify |

**Tests:** `tests/FocusGuard.Core.Tests/Statistics/GoalServiceTests.cs` — Set and retrieve goal, progress calculation (50%, 100%, over 100% capped), no goal returns null, remove goal. Uses mocked settings and statistics services.

**Verify:** `dotnet test` passes.

---

## Step 4: Statistics UI Models

Display models bridging the data layer and the statistics view.

**`src/FocusGuard.App/Models/StatsSummaryCard.cs`**
```csharp
namespace FocusGuard.App.Models;

public class StatsSummaryCard
{
    public string Title { get; init; } = string.Empty;      // "Total Focus Time"
    public string Value { get; init; } = string.Empty;      // "12h 30m"
    public string Subtitle { get; init; } = string.Empty;   // "this week"
    public string Icon { get; init; } = string.Empty;       // Unicode icon
}
```

**`src/FocusGuard.App/Models/ChartDataPoint.cs`**
```csharp
namespace FocusGuard.App.Models;

public class ChartDataPoint
{
    public string Label { get; init; } = string.Empty;    // "Mon", "Tue", "Feb 1", etc.
    public double Value { get; init; }                     // Hours or minutes
    public string Color { get; init; } = "#4A90D9";       // For pie chart segments
}
```

**`src/FocusGuard.App/Models/HeatmapDay.cs`**
```csharp
namespace FocusGuard.App.Models;

public class HeatmapDay
{
    public DateTime Date { get; init; }
    public int FocusMinutes { get; init; }
    public int IntensityLevel { get; init; }  // 0-4 (like GitHub contributions)
    public string Color { get; init; } = "#2A2A3E";  // Mapped from intensity
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Models/StatsSummaryCard.cs` | Create |
| `src/FocusGuard.App/Models/ChartDataPoint.cs` | Create |
| `src/FocusGuard.App/Models/HeatmapDay.cs` | Create |

**Verify:** `dotnet build` passes.

---

## Step 5: Statistics ViewModel

**`src/FocusGuard.App/ViewModels/StatisticsViewModel.cs`**
```csharp
namespace FocusGuard.App.ViewModels;

public partial class StatisticsViewModel : ViewModelBase
{
    private readonly IStatisticsService _statisticsService;
    private readonly IGoalService _goalService;
    private readonly ILogger<StatisticsViewModel> _logger;

    // Date range selection
    [ObservableProperty] private string _selectedPeriod = "Week";  // "Day", "Week", "Month"
    [ObservableProperty] private DateTime _periodStart;
    [ObservableProperty] private DateTime _periodEnd;
    [ObservableProperty] private string _periodLabel = string.Empty;  // "This Week", "February 2026"

    // Summary cards
    public ObservableCollection<StatsSummaryCard> SummaryCards { get; } = [];

    // Chart data
    public ObservableCollection<ChartDataPoint> DailyFocusData { get; } = [];      // Bar chart
    public ObservableCollection<ChartDataPoint> ProfileDistribution { get; } = [];  // Pie chart
    public ObservableCollection<HeatmapDay> HeatmapData { get; } = [];             // Heatmap grid

    // Streak
    [ObservableProperty] private int _currentStreak;
    [ObservableProperty] private int _longestStreak;

    // Goal progress
    public ObservableCollection<GoalProgress> GoalProgressItems { get; } = [];

    public override async void OnNavigatedTo()
    {
        SelectedPeriod = "Week";
        await LoadStatisticsAsync();
    }

    [RelayCommand] private async Task SelectPeriod(string period)
    {
        SelectedPeriod = period;
        CalculateDateRange();
        await LoadStatisticsAsync();
    }

    [RelayCommand] private async Task PreviousPeriod() { /* Shift back */ }
    [RelayCommand] private async Task NextPeriod() { /* Shift forward */ }
    [RelayCommand] private async Task ExportCsv() { /* Export to CSV file */ }
    [RelayCommand] private async Task SetGoal() { /* Show goal dialog */ }

    private void CalculateDateRange()
    {
        var today = DateTime.Today;
        (PeriodStart, PeriodEnd) = SelectedPeriod switch
        {
            "Day" => (today, today.AddDays(1)),
            "Week" => (today.AddDays(-(int)today.DayOfWeek + 1), // Monday
                       today.AddDays(7 - (int)today.DayOfWeek + 1)),
            "Month" => (new DateTime(today.Year, today.Month, 1),
                        new DateTime(today.Year, today.Month, 1).AddMonths(1)),
            _ => (today.AddDays(-7), today.AddDays(1))
        };
        PeriodLabel = FormatPeriodLabel();
    }

    private async Task LoadStatisticsAsync()
    {
        var stats = await _statisticsService.GetStatisticsAsync(PeriodStart, PeriodEnd);
        var streak = await _statisticsService.GetStreakInfoAsync();
        var goals = await _goalService.GetAllProgressAsync();

        // Populate SummaryCards: Total Focus Time, Sessions, Pomodoros, Blocked Attempts
        // Populate DailyFocusData from stats.DailyBreakdown
        // Populate ProfileDistribution from stats.ProfileBreakdown
        // Populate HeatmapData (always last 90 days for heatmap)
        // Populate GoalProgressItems
        // Update CurrentStreak, LongestStreak
    }
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/StatisticsViewModel.cs` | Create |

**Verify:** `dotnet build` passes.

---

## Step 6: Statistics View — Charts & Dashboard

### NuGet Package

**Modify `src/FocusGuard.App/FocusGuard.App.csproj`:**
```xml
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.0.*" />
```

LiveCharts2 (SkiaSharp backend) provides bar charts, pie charts, and customizable rendering. It integrates natively with WPF data binding.

### Statistics View

**`src/FocusGuard.App/Views/StatisticsView.xaml`**

Layout:
```
┌──────────────────────────────────────────────────────────┐
│  Statistics               [< ] This Week [> ]            │
│                           [Day] [Week] [Month]  [Export] │
├──────────────────────────────────────────────────────────┤
│  ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌────────┐│
│  │ 🕐 12h 30m │ │ 📊 18      │ │ 🍅 24      │ │ 🔥 7   ││
│  │ Focus Time │ │ Sessions   │ │ Pomodoros  │ │ Streak ││
│  └────────────┘ └────────────┘ └────────────┘ └────────┘│
├──────────────────────────────────────────────────────────┤
│  Daily Focus Hours                                       │
│  ┌──────────────────────────────────────────────────┐   │
│  │  ██                      ██                      │   │
│  │  ██  ██      ██  ██      ██  ██                  │   │
│  │  ██  ██  ██  ██  ██  ██  ██  ██                  │   │
│  │  Mon Tue Wed Thu Fri Sat Sun                     │   │
│  └──────────────────────────────────────────────────┘   │
├────────────────────────────┬─────────────────────────────┤
│  Profile Distribution      │  Focus Heatmap (90 days)    │
│  ┌──────────────────┐     │  ┌─────────────────────┐    │
│  │    ████████       │     │  │ ░░▓▓██░░▓▓░░░░██▓▓ │    │
│  │  ██ Deep Work ██  │     │  │ ░░▓▓██░░▓▓░░░░██▓▓ │    │
│  │  ██  42%      ██  │     │  │ ░░▓▓██░░▓▓░░░░██▓▓ │    │
│  │    ████████       │     │  │ ░░▓▓██░░▓▓░░░░██▓▓ │    │
│  │    Study 28%      │     │  │         ← Less  More → │ │
│  │    Gaming 18%     │     │  └─────────────────────┘    │
│  │    Other 12%      │     │                             │
│  └──────────────────┘     │                             │
├────────────────────────────┴─────────────────────────────┤
│  Goals                                                   │
│  ┌────────────────────────────────────────────────────┐ │
│  │  Daily Goal: 4h          ████████████░░░░ 75%  3/4h│ │
│  │  Weekly Goal: 20h        ████████░░░░░░░░ 50% 10/20│ │
│  └────────────────────────────────────────────────────┘ │
│                                                [Set Goal]│
└──────────────────────────────────────────────────────────┘
```

Key XAML structure:
```xml
<UserControl x:Class="FocusGuard.App.Views.StatisticsView" ...
    xmlns:lvc="clr-namespace:LiveChartsCore.SkiaSharpView.WPF;assembly=LiveChartsCore.SkiaSharpView.WPF">
    <ScrollViewer>
        <StackPanel>
            <!-- Header with period selector and navigation -->
            <!-- Summary cards row (UniformGrid Columns=4) -->
            <!-- Bar chart: Daily Focus Hours -->
            <lvc:CartesianChart Series="{Binding DailyFocusSeries}" ... />
            <!-- Two-column: Pie chart + Heatmap -->
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <lvc:PieChart Grid.Column="0" Series="{Binding ProfilePieSeries}" ... />
                <!-- Heatmap: custom ItemsControl with UniformGrid -->
                <ItemsControl Grid.Column="1" ItemsSource="{Binding HeatmapData}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <UniformGrid Columns="13" /> <!-- ~13 weeks = 91 days -->
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Width="12" Height="12" Margin="1" CornerRadius="2"
                                    Background="{Binding Color, Converter=...}"
                                    ToolTip="{Binding Date, StringFormat='d MMM: {0}'}" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Grid>
            <!-- Goal progress bars -->
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

**`src/FocusGuard.App/Views/StatisticsView.xaml.cs`** — Parameterless constructor.

### LiveCharts2 Integration in ViewModel

The `StatisticsViewModel` needs to expose LiveCharts-specific series properties:

```csharp
// In StatisticsViewModel — add chart series properties:
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

public ISeries[] DailyFocusSeries { get; set; } = [];
public ISeries[] ProfilePieSeries { get; set; } = [];

private void BuildDailyFocusChart(List<DailyFocusSummary> data)
{
    DailyFocusSeries = new ISeries[]
    {
        new ColumnSeries<double>
        {
            Values = data.Select(d => d.TotalFocusMinutes / 60.0).ToArray(),
            Fill = new SolidColorPaint(SKColors.CornflowerBlue)
        }
    };
    OnPropertyChanged(nameof(DailyFocusSeries));
}

private void BuildProfilePieChart(List<ProfileFocusSummary> data)
{
    ProfilePieSeries = data.Select(p => new PieSeries<double>
    {
        Values = new[] { (double)p.TotalFocusMinutes },
        Name = p.ProfileName,
        // Fill color from p.ProfileColor
    } as ISeries).ToArray();
    OnPropertyChanged(nameof(ProfilePieSeries));
}
```

### Heatmap Intensity Mapping

In `StatisticsViewModel`, build heatmap from the last 90 days of daily data:

```csharp
private async Task BuildHeatmapAsync()
{
    var end = DateTime.Today.AddDays(1);
    var start = end.AddDays(-91); // 13 weeks
    var daily = await _statisticsService.GetDailyFocusAsync(start, end);
    var dailyMap = daily.ToDictionary(d => d.Date.Date);

    // Calculate intensity thresholds based on max focus time
    var maxMinutes = daily.Any() ? daily.Max(d => d.TotalFocusMinutes) : 0;

    HeatmapData.Clear();
    for (var date = start; date < end; date = date.AddDays(1))
    {
        var minutes = dailyMap.TryGetValue(date.Date, out var d) ? d.TotalFocusMinutes : 0;
        var level = maxMinutes > 0 ? (int)(4.0 * minutes / maxMinutes) : 0;
        HeatmapData.Add(new HeatmapDay
        {
            Date = date,
            FocusMinutes = minutes,
            IntensityLevel = level,
            Color = IntensityToColor(level)
        });
    }
}

private static string IntensityToColor(int level) => level switch
{
    0 => "#2A2A3E",  // Surface (no activity)
    1 => "#1A4A2A",  // Light green
    2 => "#2A7A3A",  // Medium green
    3 => "#3AAA4A",  // Strong green
    4 => "#4ADA5A",  // Bright green
    _ => "#2A2A3E"
};
```

### Set Goal Dialog

**`src/FocusGuard.App/Views/SetGoalDialog.xaml` + `.xaml.cs`**

A simple modal dialog:
- Period selector: Radio buttons "Daily" / "Weekly"
- Target hours/minutes: TextBox with up/down buttons (or a slider)
- Optional profile filter: ComboBox with "All Profiles" + specific profiles
- "Save" / "Cancel" buttons

**`src/FocusGuard.App/ViewModels/SetGoalDialogViewModel.cs`**
```csharp
namespace FocusGuard.App.ViewModels;

public partial class SetGoalDialogViewModel : ObservableObject
{
    [ObservableProperty] private GoalPeriod _period = GoalPeriod.Daily;
    [ObservableProperty] private int _targetHours = 4;
    [ObservableProperty] private int _targetMinutes = 0;
    [ObservableProperty] private ProfileListItem? _selectedProfile;

    public ObservableCollection<ProfileListItem> Profiles { get; } = [];
    public bool Confirmed { get; set; }

    public int TotalTargetMinutes => TargetHours * 60 + TargetMinutes;

    [RelayCommand] private void Confirm() { Confirmed = true; }
    [RelayCommand] private void Cancel() { Confirmed = false; }
}
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/StatisticsView.xaml` | Create |
| `src/FocusGuard.App/Views/StatisticsView.xaml.cs` | Create |
| `src/FocusGuard.App/Views/SetGoalDialog.xaml` | Create |
| `src/FocusGuard.App/Views/SetGoalDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/SetGoalDialogViewModel.cs` | Create |
| `src/FocusGuard.App/ViewModels/StatisticsViewModel.cs` | Modify — add LiveCharts series properties |
| `src/FocusGuard.App/FocusGuard.App.csproj` | Modify — add LiveChartsCore.SkiaSharpView.WPF |

**Verify:** Statistics view renders. Bar chart shows daily focus hours. Pie chart shows profile distribution. Heatmap renders 90-day grid. Goal progress bars display correctly.

---

## Step 7: CSV Data Export

**`src/FocusGuard.Core/Statistics/CsvExporter.cs`**
```csharp
namespace FocusGuard.Core.Statistics;

public class CsvExporter
{
    private readonly IFocusSessionRepository _sessionRepository;
    private readonly IBlockedAttemptRepository _attemptRepository;
    private readonly IProfileRepository _profileRepository;

    /// <summary>
    /// Export focus sessions within a date range to CSV.
    /// Columns: Date, StartTime, EndTime, ProfileName, DurationMinutes,
    ///          PomodorosCompleted, UnlockedEarly, BlockedAttempts
    /// </summary>
    public async Task ExportSessionsAsync(string filePath, DateTime start, DateTime end)
    {
        var sessions = await _sessionRepository.GetByDateRangeAsync(start, end);
        var profiles = (await _profileRepository.GetAllAsync()).ToDictionary(p => p.Id);

        using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        await writer.WriteLineAsync("Date,StartTime,EndTime,Profile,DurationMinutes,Pomodoros,UnlockedEarly");

        foreach (var s in sessions)
        {
            var profileName = profiles.TryGetValue(s.ProfileId, out var p) ? p.Name : "Unknown";
            // Escape CSV fields containing commas
            var escapedName = profileName.Contains(',') ? $"\"{profileName}\"" : profileName;
            await writer.WriteLineAsync(
                $"{s.StartTime:yyyy-MM-dd},{s.StartTime:HH:mm},{s.EndTime:HH:mm},{escapedName}," +
                $"{s.ActualDurationMinutes},{s.PomodoroCompletedCount},{s.WasUnlockedEarly}");
        }
    }

    /// <summary>
    /// Export daily summary within a date range to CSV.
    /// </summary>
    public async Task ExportDailySummaryAsync(string filePath, DateTime start, DateTime end)
    {
        // Similar: Date, TotalFocusMinutes, SessionCount, PomodoroCount, BlockedAttempts
    }
}
```

The export is triggered from `StatisticsViewModel.ExportCsvCommand`, which calls `IDialogService.SaveFileAsync` to get the file path, then `CsvExporter.ExportSessionsAsync`.

### Dialog Service Extension

**Modify `src/FocusGuard.App/Services/IDialogService.cs`:**
- Ensure `SaveFileAsync` exists (it was declared in Phase 1).

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Statistics/CsvExporter.cs` | Create |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register `CsvExporter` as Singleton |

**Tests:** `tests/FocusGuard.Core.Tests/Statistics/CsvExporterTests.cs` — Export sessions produces valid CSV format, handles commas in profile names, empty date range produces header only. Uses mocked repositories and temp file paths.

**Verify:** `dotnet test` passes. Export button saves valid CSV file.

---

## Step 8: Desktop Notifications (Toast)

### NuGet Package

**Modify `src/FocusGuard.App/FocusGuard.App.csproj`:**
```xml
<PackageReference Include="Microsoft.Toolkit.Uwp.Notifications" Version="7.1.*" />
```

### Notification Service

**`src/FocusGuard.App/Services/NotificationService.cs`**
```csharp
using Microsoft.Toolkit.Uwp.Notifications;

namespace FocusGuard.App.Services;

public class NotificationService : IDisposable
{
    private readonly ISettingsRepository _settings;
    private readonly IFocusSessionManager _sessionManager;
    private readonly IGoalService _goalService;
    private readonly ILogger<NotificationService> _logger;

    // Notification categories (each independently toggleable):
    private bool _sessionNotifications = true;
    private bool _pomodoroNotifications = true;
    private bool _blockedNotifications = true;
    private bool _goalNotifications = true;

    public async Task InitializeAsync()
    {
        // Load notification preferences from settings
        // Subscribe to events:
        //   _sessionManager.StateChanged → session start/end notifications
        //   _sessionManager.PomodoroIntervalChanged → pomodoro transition notifications
        //   BlockedAttemptLogger can raise events → blocked attempt notifications
        //   _goalService progress → goal reached notification (check after each session end)
    }

    public void ShowSessionStarted(string profileName, int durationMinutes)
    {
        if (!_sessionNotifications) return;

        new ToastContentBuilder()
            .AddText("Focus Session Started")
            .AddText($"{profileName} — {durationMinutes} minutes")
            .Show();
    }

    public void ShowSessionEnded(string profileName, int actualMinutes)
    {
        if (!_sessionNotifications) return;

        new ToastContentBuilder()
            .AddText("Session Complete!")
            .AddText($"Great work! {profileName}: {actualMinutes} minutes")
            .Show();
    }

    public void ShowPomodoroTransition(string intervalName, int durationMinutes)
    {
        if (!_pomodoroNotifications) return;

        var title = intervalName == "Working" ? "Back to Work!" : intervalName;
        new ToastContentBuilder()
            .AddText(title)
            .AddText($"{durationMinutes} minutes")
            .Show();
    }

    public void ShowBlockedAttempt(string target, string type)
    {
        if (!_blockedNotifications) return;

        new ToastContentBuilder()
            .AddText($"Blocked: {target}")
            .AddText($"FocusGuard blocked this {type.ToLower()}")
            .Show();
    }

    public void ShowGoalReached(string goalDescription)
    {
        if (!_goalNotifications) return;

        new ToastContentBuilder()
            .AddText("Goal Reached! 🎯")
            .AddText(goalDescription)
            .Show();
    }

    public void Dispose()
    {
        ToastNotificationManagerCompat.Uninstall();
    }
}
```

### Notification Settings Keys

**Modify `src/FocusGuard.Core/Security/SettingsKeys.cs`:**
```csharp
// Notification categories
public const string NotifySessionEnabled = "notifications.session_enabled";
public const string NotifyPomodoroEnabled = "notifications.pomodoro_enabled";
public const string NotifyBlockedEnabled = "notifications.blocked_enabled";
public const string NotifyGoalEnabled = "notifications.goal_enabled";
```

### Blocked Attempt Event

**Modify `src/FocusGuard.Core/Statistics/BlockedAttemptLogger.cs`:**
Add an event for UI consumption:
```csharp
public event EventHandler<BlockedAttemptEntity>? AttemptLogged;
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/NotificationService.cs` | Create |
| `src/FocusGuard.App/FocusGuard.App.csproj` | Modify — add Microsoft.Toolkit.Uwp.Notifications |
| `src/FocusGuard.Core/Security/SettingsKeys.cs` | Modify |
| `src/FocusGuard.Core/Statistics/BlockedAttemptLogger.cs` | Modify — add event |

**Verify:** Start session → toast notification. Pomodoro transition → toast. Blocked app attempt → toast. Goal reached → celebration toast.

---

## Step 9: Auto-Start on Boot

**`src/FocusGuard.App/Services/AutoStartService.cs`**
```csharp
using Microsoft.Win32;

namespace FocusGuard.App.Services;

public class AutoStartService
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "FocusGuard";
    private readonly ISettingsRepository _settings;
    private readonly ILogger<AutoStartService> _logger;

    public bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
        return key?.GetValue(AppName) is not null;
    }

    public void EnableAutoStart()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is null) return;

        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
        key?.SetValue(AppName, $"\"{exePath}\" --minimized");

        _logger.LogInformation("Auto-start enabled: {Path}", exePath);
    }

    public void DisableAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
        key?.DeleteValue(AppName, false);

        _logger.LogInformation("Auto-start disabled");
    }
}
```

### Startup Integration

**Modify `src/FocusGuard.App/App.xaml.cs`:**
```csharp
// Parse command line args
var args = Environment.GetCommandLineArgs();
var startMinimized = args.Contains("--minimized");

// After showing MainWindow:
if (startMinimized)
{
    mainWindow.WindowState = WindowState.Minimized;
    mainWindow.Hide(); // Go straight to tray
}
```

### Settings Keys

**Modify `src/FocusGuard.Core/Security/SettingsKeys.cs`:**
```csharp
public const string AutoStartEnabled = "app.auto_start_enabled";
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/AutoStartService.cs` | Create |
| `src/FocusGuard.App/App.xaml.cs` | Modify — `--minimized` flag handling |
| `src/FocusGuard.Core/Security/SettingsKeys.cs` | Modify |

**Verify:** Enable auto-start → registry key created. Reboot → app starts minimized to tray. Disable → registry key removed.

---

## Step 10: Dashboard Enhancements

Add a weekly focus mini-chart and goal progress to the dashboard.

**Modify `src/FocusGuard.App/ViewModels/DashboardViewModel.cs`:**
```csharp
// New properties:
[ObservableProperty] private int _currentStreak;
[ObservableProperty] private string _weeklyFocusDisplay = "0h 0m";
public ISeries[] WeeklyMiniChart { get; set; } = [];
public ObservableCollection<GoalProgress> GoalProgressItems { get; } = [];

// In OnNavigatedTo(), add:
await LoadDashboardStatisticsAsync();

private async Task LoadDashboardStatisticsAsync()
{
    // Weekly focus summary
    var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + 1);
    var stats = await _statisticsService.GetStatisticsAsync(weekStart, weekStart.AddDays(7));
    WeeklyFocusDisplay = FormatDuration(stats.TotalFocusMinutes);

    // Mini bar chart for the week
    WeeklyMiniChart = new ISeries[] { new ColumnSeries<double> {
        Values = stats.DailyBreakdown.Select(d => d.TotalFocusMinutes / 60.0).ToArray()
    }};
    OnPropertyChanged(nameof(WeeklyMiniChart));

    // Streak
    var streak = await _statisticsService.GetStreakInfoAsync();
    CurrentStreak = streak.CurrentStreak;

    // Goal progress
    var goals = await _goalService.GetAllProgressAsync();
    GoalProgressItems.Clear();
    foreach (var g in goals) GoalProgressItems.Add(g);
}
```

**Modify `src/FocusGuard.App/Views/DashboardView.xaml`:**
- Add a "Weekly Summary" section with a small bar chart and focus time display
- Add a streak counter badge
- Add goal progress bars (if goals are set)
- Replace remaining "Coming Soon — Statistics" placeholder

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | Modify |
| `src/FocusGuard.App/Views/DashboardView.xaml` | Modify |

**Verify:** Dashboard shows weekly mini-chart, streak counter, and goal progress.

---

## Step 11: Settings View (Notification & Auto-Start Preferences)

**`src/FocusGuard.App/ViewModels/SettingsViewModel.cs`**
```csharp
namespace FocusGuard.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsRepository _settings;
    private readonly AutoStartService _autoStartService;
    private readonly ILogger<SettingsViewModel> _logger;

    // Auto-start
    [ObservableProperty] private bool _autoStartEnabled;

    // Notifications
    [ObservableProperty] private bool _sessionNotificationsEnabled = true;
    [ObservableProperty] private bool _pomodoroNotificationsEnabled = true;
    [ObservableProperty] private bool _blockedNotificationsEnabled = true;
    [ObservableProperty] private bool _goalNotificationsEnabled = true;
    [ObservableProperty] private bool _soundEnabled = true;
    [ObservableProperty] private bool _minimizeToTray = true;

    public override async void OnNavigatedTo()
    {
        await LoadSettingsAsync();
    }

    partial void OnAutoStartEnabledChanged(bool value)
    {
        if (value) _autoStartService.EnableAutoStart();
        else _autoStartService.DisableAutoStart();
        _ = _settings.SetAsync(SettingsKeys.AutoStartEnabled, value.ToString());
    }

    // Similar partial OnChanged methods for each notification category
    // Each saves to ISettingsRepository
}
```

**`src/FocusGuard.App/Views/SettingsView.xaml` + `.xaml.cs`**

Layout: grouped toggle switches for notifications, auto-start, sound, minimize-to-tray.

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/SettingsViewModel.cs` | Create |
| `src/FocusGuard.App/Views/SettingsView.xaml` | Create |
| `src/FocusGuard.App/Views/SettingsView.xaml.cs` | Create |

**Verify:** Settings view renders. Toggle auto-start → registry key updated. Toggle notification categories → settings saved.

---

## Step 12: Navigation Wiring & DI

Enable Statistics and Settings buttons, wire all navigation.

### MainWindowViewModel

**Modify `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs`:**
```csharp
[RelayCommand]
private void NavigateToStatistics() => _navigationService.NavigateTo<StatisticsViewModel>();

[RelayCommand]
private void NavigateToSettings() => _navigationService.NavigateTo<SettingsViewModel>();
```

### MainWindow.xaml

**Modify `src/FocusGuard.App/Views/MainWindow.xaml`:**
```xml
<!-- Enable Statistics and Settings buttons -->
<Button Content="📊  Statistics"
        Style="{StaticResource NavButtonStyle}"
        Command="{Binding NavigateToStatisticsCommand}" />
<Button Content="⚙  Settings"
        Style="{StaticResource NavButtonStyle}"
        Command="{Binding NavigateToSettingsCommand}" />
```

Update version string:
```xml
<TextBlock DockPanel.Dock="Bottom" Text="v1.0.0 — Phase 4" />
```

### App.xaml

**Modify `src/FocusGuard.App/App.xaml`:**
```xml
<DataTemplate DataType="{x:Type viewModels:StatisticsViewModel}">
    <views:StatisticsView />
</DataTemplate>
<DataTemplate DataType="{x:Type viewModels:SettingsViewModel}">
    <views:SettingsView />
</DataTemplate>
```

### App.xaml.cs

**Modify `src/FocusGuard.App/App.xaml.cs`:**
```csharp
// DI registrations:
services.AddTransient<StatisticsViewModel>();
services.AddTransient<SettingsViewModel>();
services.AddSingleton<NotificationService>();
services.AddSingleton<AutoStartService>();

// At startup, after DI:
var notificationService = Services.GetRequiredService<NotificationService>();
await notificationService.InitializeAsync();

var blockedAttemptLogger = Services.GetRequiredService<BlockedAttemptLogger>();
// Logger is singleton — auto-subscribes to blocking events in constructor
```

### Full DI Registration (Core)

**Modify `src/FocusGuard.Core/ServiceCollectionExtensions.cs`:**
```csharp
// Statistics
services.AddSingleton<IBlockedAttemptRepository, BlockedAttemptRepository>();
services.AddSingleton<BlockedAttemptLogger>();
services.AddSingleton<IStatisticsService, StatisticsService>();
services.AddSingleton<IGoalService, GoalService>();
services.AddSingleton<CsvExporter>();
```

**Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs` | Modify |
| `src/FocusGuard.App/Views/MainWindow.xaml` | Modify |
| `src/FocusGuard.App/App.xaml` | Modify |
| `src/FocusGuard.App/App.xaml.cs` | Modify |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify (final) |

**Verify:** `dotnet build` — no missing DI registrations. Statistics and Settings buttons enabled. App launches without errors.

---

## Step Dependency Graph

```
Step 1  (Blocked Attempt Logging)
  ↓
Step 2  (Statistics Service)  ← depends on Step 1
  ↓
Step 3  (Focus Goals)  ← depends on Step 2
  ↓
Step 4  (UI Models)
  ↓
Step 5  (Statistics ViewModel)  ← depends on Steps 2 + 3 + 4
  ↓
Step 6  (Statistics View + Charts)  ← depends on Step 5
  ↓
Step 7  (CSV Export)  ← depends on Steps 1 + 2
  ↓
Step 8  (Desktop Notifications)  ← depends on Steps 1 + 3
  ↓
Step 9  (Auto-Start on Boot)
  ↓
Step 10 (Dashboard Enhancements)  ← depends on Steps 2 + 3
  ↓
Step 11 (Settings View)  ← depends on Steps 8 + 9
  ↓
Step 12 (Navigation & DI Wiring)  ← depends on all above
```

Steps 1–3 are sequential backend. Steps 4–6 are sequential UI. Steps 7, 8, 9, 10 can be partially parallelized after their prerequisites. Steps 11–12 are final integration.

---

## File Summary

### New Files (~24)

| # | File | Step |
|---|------|------|
| 1 | `src/FocusGuard.Core/Data/Entities/BlockedAttemptEntity.cs` | 1 |
| 2 | `src/FocusGuard.Core/Data/Repositories/IBlockedAttemptRepository.cs` | 1 |
| 3 | `src/FocusGuard.Core/Data/Repositories/BlockedAttemptRepository.cs` | 1 |
| 4 | `src/FocusGuard.Core/Statistics/BlockedAttemptLogger.cs` | 1 |
| 5 | `src/FocusGuard.Core/Statistics/DailyFocusSummary.cs` | 2 |
| 6 | `src/FocusGuard.Core/Statistics/ProfileFocusSummary.cs` | 2 |
| 7 | `src/FocusGuard.Core/Statistics/StreakInfo.cs` | 2 |
| 8 | `src/FocusGuard.Core/Statistics/PeriodStatistics.cs` | 2 |
| 9 | `src/FocusGuard.Core/Statistics/IStatisticsService.cs` | 2 |
| 10 | `src/FocusGuard.Core/Statistics/StatisticsService.cs` | 2 |
| 11 | `src/FocusGuard.Core/Statistics/FocusGoal.cs` | 3 |
| 12 | `src/FocusGuard.Core/Statistics/GoalProgress.cs` | 3 |
| 13 | `src/FocusGuard.Core/Statistics/IGoalService.cs` | 3 |
| 14 | `src/FocusGuard.Core/Statistics/GoalService.cs` | 3 |
| 15 | `src/FocusGuard.App/Models/StatsSummaryCard.cs` | 4 |
| 16 | `src/FocusGuard.App/Models/ChartDataPoint.cs` | 4 |
| 17 | `src/FocusGuard.App/Models/HeatmapDay.cs` | 4 |
| 18 | `src/FocusGuard.App/ViewModels/StatisticsViewModel.cs` | 5 |
| 19 | `src/FocusGuard.App/Views/StatisticsView.xaml` | 6 |
| 20 | `src/FocusGuard.App/Views/StatisticsView.xaml.cs` | 6 |
| 21 | `src/FocusGuard.App/Views/SetGoalDialog.xaml` | 6 |
| 22 | `src/FocusGuard.App/Views/SetGoalDialog.xaml.cs` | 6 |
| 23 | `src/FocusGuard.App/ViewModels/SetGoalDialogViewModel.cs` | 6 |
| 24 | `src/FocusGuard.Core/Statistics/CsvExporter.cs` | 7 |
| 25 | `src/FocusGuard.App/Services/NotificationService.cs` | 8 |
| 26 | `src/FocusGuard.App/Services/AutoStartService.cs` | 9 |
| 27 | `src/FocusGuard.App/ViewModels/SettingsViewModel.cs` | 11 |
| 28 | `src/FocusGuard.App/Views/SettingsView.xaml` | 11 |
| 29 | `src/FocusGuard.App/Views/SettingsView.xaml.cs` | 11 |

### New Test Files (4)

| # | File | Step |
|---|------|------|
| 1 | `tests/FocusGuard.Core.Tests/Data/BlockedAttemptRepositoryTests.cs` | 1 |
| 2 | `tests/FocusGuard.Core.Tests/Statistics/StatisticsServiceTests.cs` | 2 |
| 3 | `tests/FocusGuard.Core.Tests/Statistics/GoalServiceTests.cs` | 3 |
| 4 | `tests/FocusGuard.Core.Tests/Statistics/CsvExporterTests.cs` | 7 |

### Modified Files (~14)

| # | File | Steps |
|---|------|-------|
| 1 | `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` | 1 |
| 2 | `src/FocusGuard.Core/Data/DatabaseMigrator.cs` | 1 |
| 3 | `src/FocusGuard.Core/Data/Repositories/IFocusSessionRepository.cs` | 2 |
| 4 | `src/FocusGuard.Core/Data/Repositories/FocusSessionRepository.cs` | 2 |
| 5 | `src/FocusGuard.Core/Security/SettingsKeys.cs` | 3, 8, 9 |
| 6 | `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | 1, 2, 3, 7, 12 |
| 7 | `src/FocusGuard.App/FocusGuard.App.csproj` | 6, 8 |
| 8 | `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | 10 |
| 9 | `src/FocusGuard.App/Views/DashboardView.xaml` | 10 |
| 10 | `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs` | 12 |
| 11 | `src/FocusGuard.App/Views/MainWindow.xaml` | 12 |
| 12 | `src/FocusGuard.App/App.xaml` | 12 |
| 13 | `src/FocusGuard.App/App.xaml.cs` | 9, 12 |
| 14 | `src/FocusGuard.Core/Statistics/BlockedAttemptLogger.cs` | 8 |

---

## Risk Mitigations

| Risk | Mitigation |
|------|------------|
| **LiveCharts2 rendering performance with large datasets** | Limit heatmap to 91 days. Bar chart shows at most 31 data points (month). Pie chart shows at most 10 profiles (group remainder as "Other"). |
| **Toast notifications on Windows 10 vs 11** | `Microsoft.Toolkit.Uwp.Notifications` handles both. Fallback: balloon tip via `NotifyIcon` if toast fails. |
| **CSV injection** | Prefix values starting with `=`, `+`, `-`, `@` with a single quote. Use `StreamWriter` (not string concat) for proper encoding. |
| **Registry access denied for auto-start** | `HKCU` doesn't require admin (unlike `HKLM`). Wrap in try-catch, log failure, disable toggle. |
| **Streak calculation performance** | Query only completed sessions ordered by date, iterate backward. Stop at first gap. Cache result with 1-minute TTL. |
| **Heatmap off-by-one on DST** | Use `DateTime.Today` (local) for heatmap dates. No UTC conversion — heatmap is a user-facing concept. |
| **Statistics queries on large datasets** | Add database indexes on `FocusSessions.StartTime` and `BlockedAttempts.Timestamp`. Queries filter by date range before aggregating. |
| **Goal notifications spamming** | Only check goal completion once per session end (not per tick). Track last notification time to prevent duplicates. |
| **LiveCharts2 + dark theme** | Configure chart axis labels, tooltips, and legends with theme colors (`#ECEFF4` text, `#2A2A3E` background). Set in ViewModel via `LiveChartsCore.LiveChartsSettings`. |
| **AutoStartService on portable (non-installed) mode** | Registry path uses `Environment.ProcessPath`. If running from temp/downloads, warn user that auto-start may not work reliably. |

---

## End-to-End Verification Checklist

1. `dotnet build` — no errors
2. `dotnet test` — all existing + new tests pass
3. **Blocked attempt logging:** Start session → attempt to open blocked app → attempt logged in DB → tray notification (if enabled)
4. **Statistics navigation:** Click Statistics in sidebar → view renders with summary cards, charts, heatmap
5. **Date range selector:** Toggle Day/Week/Month → charts update → navigate ◀/▶ → period shifts
6. **Bar chart:** Shows daily focus hours for selected period → correct values
7. **Pie chart:** Shows profile time distribution → correct percentages and colors
8. **Heatmap:** 90-day grid renders → darker cells on days with more focus time → hover shows date and minutes
9. **Streak:** Complete sessions on consecutive days → streak counter increments → skip a day → streak resets
10. **Goals:** Set daily goal (4h) → progress bar shows on dashboard and statistics → complete goal → celebration toast
11. **CSV export:** Click export → file dialog → save → open CSV → correct columns and data
12. **Toast notifications:** Session start → toast. Pomodoro transition → toast. Blocked app → toast. Goal reached → toast
13. **Notification preferences:** Settings → disable "Blocked" notifications → blocked attempts no longer show toasts → re-enable → toasts resume
14. **Auto-start:** Settings → enable auto-start → reboot → app starts minimized to tray → scheduled session auto-activates
15. **Settings persistence:** Change settings → restart app → settings preserved
16. **Dashboard enhancements:** Weekly mini-chart visible → streak badge → goal progress bars (if goals set)
