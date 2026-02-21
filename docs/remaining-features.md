# FocusGuard — Remaining Features Implementation Plan

## Context

Phases 1–5 are complete at the **backend/infrastructure** level: solution scaffolding, EF Core data layer, hosts-file website blocker, WMI/polling application blocker, WPF shell, profiles CRUD, `BlockingOrchestrator`, security layer (password generation, master key), Focus Session State Machine, crash recovery, strict mode, watchdog, heartbeat, auto-start registry service, portable mode, and Inno Setup installer. All 148 tests pass.

However, most **user-facing UI** is still missing. The dashboard shows three "Coming Soon" placeholders, and the sidebar has Calendar, Statistics, and Settings buttons all disabled. The session lifecycle (start, unlock, pomodoro visualization) has no UI — only backend logic exists.

This document consolidates the 6 remaining feature areas into a single step-by-step implementation plan, referencing the original phase docs (`focus-session-timer.md`, `calendar-scheduling.md`, `statistics-polish.md`) for detailed code snippets.

**Prerequisite:** `dotnet build && dotnet test` passes (148 tests, 0 failures).

---

## Feature Area 1: Session Interaction UI

**Goal:** Users can start focus sessions, see unlock password, type to unlock, and set up master key on first launch.

**References:** `focus-session-timer.md` Steps 6 + 9

### Step 1.1: Start Session Dialog

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/StartSessionDialog.xaml` | Create |
| `src/FocusGuard.App/Views/StartSessionDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/StartSessionDialogViewModel.cs` | Create |
| `src/FocusGuard.App/Models/StartSessionDialogResult.cs` | Create |

**Details:**
- Modal dialog shown when user clicks a profile card on the dashboard
- Profile name + color indicator (read-only)
- Duration picker: preset buttons (25m, 45m, 60m, 90m, 120m) + custom TextBox
- Pomodoro toggle: CheckBox "Enable Pomodoro Mode"
- Password difficulty: ComboBox (Easy / Medium / Hard)
- "Start Session" primary button, "Cancel" secondary button
- Uses dark theme styles from `Theme.xaml`
- `StartSessionDialogViewModel`: `[ObservableProperty]` for profileName, profileColor, durationMinutes (default 25), enablePomodoro, selectedDifficulty (default Medium). `Confirmed` bool + commands for Confirm/Cancel
- `StartSessionDialogResult`: DurationMinutes, EnablePomodoro, Difficulty

### Step 1.2: Unlock Dialog

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/UnlockDialog.xaml` | Create |
| `src/FocusGuard.App/Views/UnlockDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/UnlockDialogViewModel.cs` | Create |
| `src/FocusGuard.App/Models/UnlockDialogResult.cs` | Create |

**Details:**
- Modal dialog for ending a session early
- Password is hidden by default — revealed on button click ("Show Password")
- TextBox for typing password with **paste disabled** via `DataObject.AddPastingHandler` + `CancelCommand()`
- Live character counter: "12 / 30 characters"
- "Unlock" button enabled only when typed length matches expected length
- "Emergency Unlock" expander with master key TextBox
- Visual feedback: green border on correct, red on wrong
- `UnlockDialogViewModel`: `[ObservableProperty]` for generatedPassword, typedPassword, isPasswordRevealed, isCorrect, masterKeyInput. Commands: RevealPassword, TryUnlock (calls `IFocusSessionManager.TryUnlockAsync`), TryEmergencyUnlock (calls `EmergencyUnlockAsync`)
- `UnlockDialogResult`: Unlocked, EmergencyUsed

### Step 1.3: Master Key Setup Dialog

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/MasterKeySetupDialog.xaml` | Create |
| `src/FocusGuard.App/Views/MasterKeySetupDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/MasterKeySetupViewModel.cs` | Create |

**Details:**
- Shown on first launch (when `MasterKeyService.IsSetupCompleteAsync()` returns false)
- Explanation text about master recovery key
- Large monospace TextBlock displaying generated key (copyable via button)
- CheckBox: "I have saved this key"
- "Continue" button enabled only when checkbox checked
- `MasterKeySetupViewModel`: calls `MasterKeyService.GenerateMasterKeyAsync()` on load, exposes key and `HasSavedKey` property

### Step 1.4: Dialog Service Extension

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/IDialogService.cs` | Modify — add `ShowStartSessionDialogAsync`, `ShowUnlockDialogAsync` |
| `src/FocusGuard.App/Services/DialogService.cs` | Modify — implement new dialog methods |

### Step 1.5: Dashboard Integration

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | Modify — add `StartSessionCommand(profileId)`, `EndSessionEarlyCommand`, `IsSessionActive`, `ActiveProfileName` bindings |
| `src/FocusGuard.App/Views/DashboardView.xaml` | Modify — profile cards gain "Start Focus Session" button, status card shows active session info + "End Session Early" danger button |
| `src/FocusGuard.App/App.xaml.cs` | Modify — add master key setup check at startup before showing MainWindow |

**Verify:** Click profile → start dialog → set duration → Start → blocking activates. Click "End Session Early" → unlock dialog → type password → session ends. First launch → master key dialog.

---

## Feature Area 2: Pomodoro Timer Visualization

**Goal:** Visual countdown, progress ring, Pomodoro interval indicators, and sound alerts.

**References:** `focus-session-timer.md` Steps 5 + 6 + 12

### Step 2.1: Pomodoro Timer Engine

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Sessions/PomodoroConfiguration.cs` | Create — WorkMinutes(25), ShortBreakMinutes(5), LongBreakMinutes(15), LongBreakInterval(4), AutoStartNext |
| `src/FocusGuard.Core/Sessions/PomodoroInterval.cs` | Create — Type (FocusSessionState), DurationMinutes, SequenceNumber |
| `src/FocusGuard.Core/Sessions/PomodoroIntervalCalculator.cs` | Create — CalculateIntervals, GetNextInterval |
| `src/FocusGuard.Core/Sessions/PomodoroTimer.cs` | Create — 1-second tick timer, IntervalRemaining, events (IntervalStarted, IntervalCompleted, TimerTick) |

**Details:**
- `PomodoroTimer`: Singleton, wraps `System.Timers.Timer` at 1s interval
- Each tick updates `IntervalRemaining`, fires `TimerTick` for UI refresh
- On interval complete: fires `IntervalCompleted`, calls `_sessionManager.AdvancePomodoroInterval()`, starts next interval
- Loads config from `ISettingsRepository` (PomodoroWorkMinutes, etc.)
- `PomodoroIntervalCalculator`: pure logic, calculates Work → ShortBreak → Work → ... → LongBreak cycle

### Step 2.2: Circular Progress Ring Control

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Controls/CircularProgressRing.cs` | Create — custom WPF Control |

**Details:**
- DependencyProperties: Progress (0.0–1.0), StrokeThickness (default 6), ProgressColor, TrackColor
- `OnRender`: draws background circle (track) + arc from 12 o'clock for Progress * 360 degrees
- Uses `DrawingContext` with `StreamGeometry` for the arc path

### Step 2.3: Dashboard Timer UI

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | Modify — add `TimerDisplay` (MM:SS), `TimerProgress` (0–1), `CurrentIntervalLabel`, `PomodoroCount`, subscribe to `PomodoroTimer.TimerTick` and `IFocusSessionManager.StateChanged` |
| `src/FocusGuard.App/Views/DashboardView.xaml` | Modify — replace status card with timer card: large timer display (48px Consolas), CircularProgressRing, interval label, pomodoro dot indicators, "End Session Early" button. Remove "Coming Soon — Pomodoro Timer" placeholder |

### Step 2.4: Sound Alerts

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Sessions/SoundAlertService.cs` | Create — PlayWorkStart (Exclamation), PlayBreakStart (Asterisk), PlaySessionEnd (Hand). Checks `SettingsKeys.SoundEnabled`. Wraps in try-catch for headless systems |

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register PomodoroIntervalCalculator, PomodoroTimer, SoundAlertService |

**Tests:**
| File | Action |
|------|--------|
| `tests/FocusGuard.Core.Tests/Sessions/PomodoroIntervalCalculatorTests.cs` | Create — interval sequence, long break placement, duration fitting |

**Verify:** Start Pomodoro session → timer counts down with progress ring → interval ends → sound plays → break → back to work → long break after 4 cycles.

---

## Feature Area 3: Calendar & Scheduling

**Goal:** Visual calendar to schedule focus sessions, drag-and-drop creation, recurring schedules, auto-activation.

**References:** `calendar-scheduling.md` Steps 1–11

### Step 3.1: Database Schema — ScheduledSession

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Data/Entities/ScheduledSessionEntity.cs` | Create — Id, ProfileId, StartTime, EndTime, IsRecurring, RecurrenceRule (JSON), PomodoroEnabled, IsEnabled |
| `src/FocusGuard.Core/Scheduling/RecurrenceRule.cs` | Create — RecurrenceType enum (Daily, Weekdays, Weekly, Custom), DaysOfWeek list, IntervalWeeks, EndDate |
| `src/FocusGuard.Core/Data/Repositories/IScheduledSessionRepository.cs` | Create — CRUD, GetAllAsync, GetEnabledAsync, GetByDateRangeAsync |
| `src/FocusGuard.Core/Data/Repositories/ScheduledSessionRepository.cs` | Create — IDbContextFactory-based implementation |

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` | Modify — add `DbSet<ScheduledSessionEntity>`, configure key + indexes |
| `src/FocusGuard.Core/Data/DatabaseMigrator.cs` | Modify — `CREATE TABLE IF NOT EXISTS ScheduledSessions` + indexes on ProfileId, StartTime |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register IScheduledSessionRepository |

### Step 3.2: Recurrence Engine & Scheduling Engine

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Scheduling/ScheduledOccurrence.cs` | Create — ScheduledSessionId, ProfileId, StartTime, EndTime, PomodoroEnabled, DurationMinutes |
| `src/FocusGuard.Core/Scheduling/OccurrenceExpander.cs` | Create — Expand(session, rangeStart, rangeEnd) → List\<ScheduledOccurrence\>. Handles Daily, Weekdays, Weekly, Custom recurrence types |
| `src/FocusGuard.Core/Scheduling/ISchedulingEngine.cs` | Create — StartAsync, Stop, RefreshAsync, GetNextOccurrence, SessionStarting/SessionEnding events |
| `src/FocusGuard.Core/Scheduling/SchedulingEngine.cs` | Create — 15s polling timer, loads enabled sessions, expands occurrences for next 24h, fires events when session time arrives |

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/BlockingOrchestrator.cs` | Modify — subscribe to ISchedulingEngine.SessionStarting → auto-start session, SessionEnding → auto-end |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register OccurrenceExpander, ISchedulingEngine |

### Step 3.3: Calendar UI

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Models/CalendarDay.cs` | Create — Date, IsCurrentMonth, IsToday, TimeBlocks list |
| `src/FocusGuard.App/Models/CalendarTimeBlock.cs` | Create — ScheduledSessionId, ProfileId, ProfileName, ProfileColor, StartTime, EndTime, IsRecurring, PomodoroEnabled, TimeRangeDisplay |
| `src/FocusGuard.App/Models/WeekDay.cs` | Create — Date, DayName, IsToday, TimeBlocks list |
| `src/FocusGuard.App/ViewModels/CalendarViewModel.cs` | Create — CurrentMonth, MonthYearDisplay, IsWeeklyView, SelectedDate, Days (42-cell ObservableCollection), WeekDays, SelectedDayBlocks. Commands: PreviousMonth, NextMonth, GoToToday, ToggleView, SelectDay, CreateScheduledSession, EditScheduledSession, DeleteScheduledSession |
| `src/FocusGuard.App/Views/CalendarView.xaml` | Create — Two-column: left = 6×7 monthly grid (UniformGrid) with day-of-week headers + nav arrows, right = 260px side panel showing selected day's sessions + "New Session" button. Toggle between monthly grid and weekly hour-grid view |
| `src/FocusGuard.App/Views/CalendarView.xaml.cs` | Create — Parameterless constructor. Drag-and-drop event handlers for weekly view (mouse down/move/up → CalculateTimeFromY → snap to 15min → open dialog with pre-filled times) |

### Step 3.4: Schedule Session Dialog

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/ScheduleSessionDialog.xaml` | Create |
| `src/FocusGuard.App/Views/ScheduleSessionDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/ScheduleSessionDialogViewModel.cs` | Create — SelectedProfile, SessionDate, StartTime, EndTime, PomodoroEnabled, IsRecurring, RecurrenceType, day checkboxes (Mon–Sun), RecurrenceEndDate, DurationDisplay (auto-calculated) |
| `src/FocusGuard.App/Models/ScheduleSessionDialogResult.cs` | Create — ProfileId, StartTime, EndTime, PomodoroEnabled, IsRecurring, RecurrenceRule |

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/IDialogService.cs` | Modify — add ShowScheduleSessionDialogAsync |
| `src/FocusGuard.App/Services/DialogService.cs` | Modify — implement |

### Step 3.5: Navigation & Dashboard Integration

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs` | Modify — add `NavigateToCalendarCommand` |
| `src/FocusGuard.App/Views/MainWindow.xaml` | Modify — enable Calendar button with command binding |
| `src/FocusGuard.App/App.xaml` | Modify — add CalendarViewModel → CalendarView DataTemplate |
| `src/FocusGuard.App/App.xaml.cs` | Modify — register CalendarViewModel (Transient), call ISchedulingEngine.StartAsync() at startup |
| `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | Modify — add TodaySessions collection, replace "Coming Soon — Calendar" placeholder |
| `src/FocusGuard.App/Views/DashboardView.xaml` | Modify — show Today's Schedule section with profile-colored cards and time ranges |

**Tests:**
| File | Action |
|------|--------|
| `tests/FocusGuard.Core.Tests/Data/ScheduledSessionRepositoryTests.cs` | Create |
| `tests/FocusGuard.Core.Tests/Scheduling/OccurrenceExpanderTests.cs` | Create |
| `tests/FocusGuard.Core.Tests/Scheduling/SchedulingEngineTests.cs` | Create |

**Verify:** Calendar button enabled → monthly grid renders → click day → side panel updates → create session → block appears → recurring sessions expand across days → weekly view with hour grid → drag-and-drop → auto-activation at scheduled time → Dashboard shows today's schedule.

---

## Feature Area 4: Statistics & Analytics

**Goal:** Charts, heatmap, streak tracking, focus goals, blocked attempt logging, CSV export.

**References:** `statistics-polish.md` Steps 1–7, 10

### Step 4.1: Blocked Attempt Logging — Data Layer

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Data/Entities/BlockedAttemptEntity.cs` | Create — Id, SessionId, Timestamp, Type ("Website"/"Application"), Target |
| `src/FocusGuard.Core/Data/Repositories/IBlockedAttemptRepository.cs` | Create — CreateAsync, GetBySessionIdAsync, GetByDateRangeAsync, GetCountByDateRangeAsync |
| `src/FocusGuard.Core/Data/Repositories/BlockedAttemptRepository.cs` | Create |
| `src/FocusGuard.Core/Statistics/BlockedAttemptLogger.cs` | Create — subscribes to IApplicationBlocker.ProcessBlocked, logs to DB. Exposes `AttemptLogged` event for UI |

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` | Modify — add DbSet\<BlockedAttemptEntity\>, configure indexes |
| `src/FocusGuard.Core/Data/DatabaseMigrator.cs` | Modify — CREATE TABLE IF NOT EXISTS BlockedAttempts + indexes |

### Step 4.2: Statistics Aggregation Service

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Statistics/DailyFocusSummary.cs` | Create — Date, TotalFocusMinutes, SessionCount, PomodoroCount, BlockedAttemptCount |
| `src/FocusGuard.Core/Statistics/ProfileFocusSummary.cs` | Create — ProfileId, ProfileName, ProfileColor, TotalFocusMinutes, SessionCount |
| `src/FocusGuard.Core/Statistics/StreakInfo.cs` | Create — CurrentStreak, LongestStreak, StreakStartDate |
| `src/FocusGuard.Core/Statistics/PeriodStatistics.cs` | Create — PeriodStart, PeriodEnd, totals, DailyBreakdown, ProfileBreakdown, Streak |
| `src/FocusGuard.Core/Statistics/IStatisticsService.cs` | Create — GetStatisticsAsync, GetStreakInfoAsync, GetDailyFocusAsync, GetProfileBreakdownAsync |
| `src/FocusGuard.Core/Statistics/StatisticsService.cs` | Create — queries FocusSessions + BlockedAttempts, groups by day/profile, calculates streaks |

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Data/Repositories/IFocusSessionRepository.cs` | Modify — add GetByDateRangeAsync |
| `src/FocusGuard.Core/Data/Repositories/FocusSessionRepository.cs` | Modify — implement GetByDateRangeAsync |

### Step 4.3: Focus Goals

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Statistics/FocusGoal.cs` | Create — GoalPeriod (Daily/Weekly), TargetMinutes, ProfileId (nullable = global) |
| `src/FocusGuard.Core/Statistics/GoalProgress.cs` | Create — Goal, CurrentMinutes, CompletionPercent, IsCompleted, RemainingMinutes |
| `src/FocusGuard.Core/Statistics/IGoalService.cs` | Create — Get/Set/Remove goal, GetProgressAsync, GetAllProgressAsync |
| `src/FocusGuard.Core/Statistics/GoalService.cs` | Create — stores goals as JSON in Settings table ("goal.daily", "goal.weekly", "goal.daily.{profileId}") |

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Security/SettingsKeys.cs` | Modify — add DailyGoalPrefix, WeeklyGoalPrefix |

### Step 4.4: Statistics UI

**New NuGet:**
```xml
<PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.0.*" />
```

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Models/StatsSummaryCard.cs` | Create — Title, Value, Subtitle, Icon |
| `src/FocusGuard.App/Models/ChartDataPoint.cs` | Create — Label, Value, Color |
| `src/FocusGuard.App/Models/HeatmapDay.cs` | Create — Date, FocusMinutes, IntensityLevel (0–4), Color |
| `src/FocusGuard.App/ViewModels/StatisticsViewModel.cs` | Create — SelectedPeriod (Day/Week/Month), period navigation, SummaryCards, DailyFocusSeries (bar chart), ProfilePieSeries (pie chart), HeatmapData (90-day grid), GoalProgressItems, streak info. Commands: SelectPeriod, PreviousPeriod, NextPeriod, ExportCsv, SetGoal |
| `src/FocusGuard.App/Views/StatisticsView.xaml` | Create — Header with period selector, 4 summary cards (UniformGrid), bar chart (LiveCharts CartesianChart), two-column: pie chart + heatmap (UniformGrid Columns=13 with 12x12 colored cells), goal progress bars |
| `src/FocusGuard.App/Views/StatisticsView.xaml.cs` | Create — parameterless constructor |
| `src/FocusGuard.App/Views/SetGoalDialog.xaml` | Create — Period radio (Daily/Weekly), target hours/minutes, optional profile filter, Save/Cancel |
| `src/FocusGuard.App/Views/SetGoalDialog.xaml.cs` | Create |
| `src/FocusGuard.App/ViewModels/SetGoalDialogViewModel.cs` | Create — Period, TargetHours, TargetMinutes, SelectedProfile, TotalTargetMinutes |

### Step 4.5: CSV Export

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Statistics/CsvExporter.cs` | Create — ExportSessionsAsync (Date, StartTime, EndTime, Profile, DurationMinutes, Pomodoros, UnlockedEarly), ExportDailySummaryAsync. CSV-safe (escape commas, prefix injection chars) |

### Step 4.6: Dashboard Enhancements & Navigation

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | Modify — add WeeklyMiniChart (ISeries[]), CurrentStreak, WeeklyFocusDisplay, GoalProgressItems. Replace "Coming Soon — Statistics" placeholder |
| `src/FocusGuard.App/Views/DashboardView.xaml` | Modify — add weekly mini bar chart, streak badge, goal progress bars |
| `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs` | Modify — add NavigateToStatisticsCommand |
| `src/FocusGuard.App/Views/MainWindow.xaml` | Modify — enable Statistics button with command binding |
| `src/FocusGuard.App/App.xaml` | Modify — add StatisticsViewModel → StatisticsView DataTemplate |
| `src/FocusGuard.App/App.xaml.cs` | Modify — register StatisticsViewModel, BlockedAttemptLogger, start logger at startup |
| `src/FocusGuard.App/FocusGuard.App.csproj` | Modify — add LiveChartsCore.SkiaSharpView.WPF |
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | Modify — register IBlockedAttemptRepository, BlockedAttemptLogger, IStatisticsService, IGoalService, CsvExporter |

**Tests:**
| File | Action |
|------|--------|
| `tests/FocusGuard.Core.Tests/Data/BlockedAttemptRepositoryTests.cs` | Create |
| `tests/FocusGuard.Core.Tests/Statistics/StatisticsServiceTests.cs` | Create |
| `tests/FocusGuard.Core.Tests/Statistics/GoalServiceTests.cs` | Create |
| `tests/FocusGuard.Core.Tests/Statistics/CsvExporterTests.cs` | Create |

**Verify:** Statistics button enabled → view renders with charts → toggle Day/Week/Month → charts update → heatmap shows 90-day grid → set goal → progress bars → export CSV → valid file → streak counter → blocked attempts logged during sessions.

---

## Feature Area 5: System Tray & Notifications

**Goal:** System tray icon with status, minimize-to-tray, toast notifications for session/pomodoro/blocked/goal events.

**References:** `focus-session-timer.md` Steps 7 + 11, `statistics-polish.md` Step 8

### Step 5.1: System Tray Integration

**New NuGet (csproj change):**
```xml
<UseWindowsForms>true</UseWindowsForms>
```

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/TrayIconService.cs` | Create — `System.Windows.Forms.NotifyIcon`, context menu (Open / Quick Start / Exit), tooltip shows remaining time, double-click restores window. Subscribes to `IFocusSessionManager.StateChanged` + `PomodoroTimer.TimerTick` for tooltip updates. `ShowBalloonTip()` for Pomodoro transitions |

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/FocusGuard.App.csproj` | Modify — add `<UseWindowsForms>true</UseWindowsForms>` |
| `src/FocusGuard.App/Views/MainWindow.xaml.cs` | Modify — minimize-to-tray behavior: `OnClosing` hides to tray during active session, `OnStateChanged` hides when minimized if tray enabled. Add `RestoreFromTray()` method |
| `src/FocusGuard.App/App.xaml.cs` | Modify — initialize TrayIconService, register in DI |

### Step 5.2: Timer Overlay Window

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Views/TimerOverlayWindow.xaml` | Create — 180x180, WindowStyle=None, AllowsTransparency, Topmost, ShowInTaskbar=False, ShowActivated=False. Circular border with CircularProgressRing + timer display + interval label + pomodoro count. Draggable via MouseLeftButtonDown → DragMove() |
| `src/FocusGuard.App/Views/TimerOverlayWindow.xaml.cs` | Create — DragMove handler |
| `src/FocusGuard.App/ViewModels/TimerOverlayViewModel.cs` | Create — TimerDisplay, TimerProgress, IntervalLabel, PomodoroCountDisplay. Subscribes to PomodoroTimer.TimerTick |

**Lifecycle:** Session starts → create and show overlay (bottom-right of primary screen). Session ends → close overlay. Managed by `BlockingOrchestrator` or `DashboardViewModel`.

### Step 5.3: Toast Notifications

**New NuGet:**
```xml
<PackageReference Include="Microsoft.Toolkit.Uwp.Notifications" Version="7.1.*" />
```

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/Services/NotificationService.cs` | Create — Uses `ToastContentBuilder` for rich toast notifications. Categories: session start/end, pomodoro transitions, blocked attempts, goal reached. Each category independently toggleable via settings. Subscribes to `IFocusSessionManager.StateChanged`, `PomodoroIntervalChanged`, `BlockedAttemptLogger.AttemptLogged`, `IGoalService` (check on session end) |

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.Core/Security/SettingsKeys.cs` | Modify — add NotifySessionEnabled, NotifyPomodoroEnabled, NotifyBlockedEnabled, NotifyGoalEnabled |
| `src/FocusGuard.Core/Statistics/BlockedAttemptLogger.cs` | Modify — add `AttemptLogged` event |
| `src/FocusGuard.App/FocusGuard.App.csproj` | Modify — add Microsoft.Toolkit.Uwp.Notifications |
| `src/FocusGuard.App/App.xaml.cs` | Modify — register NotificationService, call InitializeAsync at startup |

**Verify:** Tray icon appears → tooltip shows status → right-click → context menu → minimize to tray → double-click restores. Session starts → overlay appears, toast notification. Pomodoro transition → balloon tip + sound. Blocked app → toast. Goal reached → celebration toast.

---

## Feature Area 6: Settings View

**Goal:** Centralized settings page for strict mode, auto-start, notifications, sound, password defaults, Pomodoro config.

**References:** `statistics-polish.md` Step 11

### Step 6.1: Settings ViewModel & View

**New Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/SettingsViewModel.cs` | Create |
| `src/FocusGuard.App/Views/SettingsView.xaml` | Create |
| `src/FocusGuard.App/Views/SettingsView.xaml.cs` | Create |

**SettingsViewModel properties (grouped):**

**General:**
- `AutoStartEnabled` (bool) — calls `IAutoStartService.Enable/Disable()`, persists
- `MinimizeToTray` (bool) — persists to `SettingsKeys.MinimizeToTray`
- `StrictModeEnabled` (bool) — calls `IStrictModeService.SetEnabledAsync()`, disabled during active session

**Session Defaults:**
- `DefaultDurationMinutes` (int) — persists to `SettingsKeys.DefaultSessionDuration`
- `PasswordDifficulty` (ComboBox: Easy/Medium/Hard) — persists to `SettingsKeys.PasswordDifficulty`
- `PasswordLength` (int, 10–100) — persists to `SettingsKeys.PasswordLength`

**Pomodoro:**
- `PomodoroWorkMinutes` (int) — default 25
- `PomodoroShortBreakMinutes` (int) — default 5
- `PomodoroLongBreakMinutes` (int) — default 15
- `PomodoroLongBreakInterval` (int) — default 4
- `PomodoroAutoStart` (bool) — default false

**Notifications:**
- `SessionNotificationsEnabled` (bool) — default true
- `PomodoroNotificationsEnabled` (bool) — default true
- `BlockedNotificationsEnabled` (bool) — default true
- `GoalNotificationsEnabled` (bool) — default true
- `SoundEnabled` (bool) — default true

**Master Key:**
- `IsMasterKeySetup` (bool, read-only indicator)
- `RegenerateMasterKeyCommand` — shows confirmation dialog then `MasterKeySetupDialog`

All properties use `partial void On{Property}Changed` to persist via `ISettingsRepository.SetAsync()`.

**SettingsView.xaml layout:**
```
┌──────────────────────────────────────────┐
│  Settings                                │
├──────────────────────────────────────────┤
│  General                                 │
│  ┌────────────────────────────────────┐  │
│  │ Auto-start on boot        [toggle] │  │
│  │ Minimize to tray           [toggle] │  │
│  │ Strict mode                [toggle] │  │
│  └────────────────────────────────────┘  │
│                                          │
│  Session Defaults                        │
│  ┌────────────────────────────────────┐  │
│  │ Default duration     [25] minutes  │  │
│  │ Password difficulty  [Medium  ▾]   │  │
│  │ Password length      [30]          │  │
│  └────────────────────────────────────┘  │
│                                          │
│  Pomodoro                                │
│  ┌────────────────────────────────────┐  │
│  │ Work duration        [25] minutes  │  │
│  │ Short break          [ 5] minutes  │  │
│  │ Long break           [15] minutes  │  │
│  │ Long break interval  [ 4] sessions │  │
│  │ Auto-start next       [toggle]     │  │
│  └────────────────────────────────────┘  │
│                                          │
│  Notifications                           │
│  ┌────────────────────────────────────┐  │
│  │ Session notifications  [toggle]    │  │
│  │ Pomodoro notifications [toggle]    │  │
│  │ Blocked app alerts     [toggle]    │  │
│  │ Goal celebrations      [toggle]    │  │
│  │ Sound effects          [toggle]    │  │
│  └────────────────────────────────────┘  │
│                                          │
│  Security                                │
│  ┌────────────────────────────────────┐  │
│  │ Master key: ✓ Configured           │  │
│  │ [Regenerate Master Key]            │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

Uses dark theme with CardStyle grouping, toggle switches (CheckBox or custom ToggleButton), and number inputs (TextBox with validation).

### Step 6.2: Navigation Wiring

**Modified Files:**
| File | Action |
|------|--------|
| `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs` | Modify — add `NavigateToSettingsCommand` |
| `src/FocusGuard.App/Views/MainWindow.xaml` | Modify — enable Settings button with command binding |
| `src/FocusGuard.App/App.xaml` | Modify — add SettingsViewModel → SettingsView DataTemplate |
| `src/FocusGuard.App/App.xaml.cs` | Modify — register SettingsViewModel (Transient) |

**Verify:** Settings button enabled → view renders with grouped toggles → toggle auto-start → registry updated → toggle notifications → persisted → change Pomodoro durations → persisted → strict mode toggle disabled during active session.

---

## Implementation Order & Dependencies

```
Feature Area 1 (Session UI)
  ↓
Feature Area 2 (Pomodoro Timer Visualization)  ← depends on 1 (dashboard integration)
  ↓
Feature Area 5 (System Tray & Notifications)   ← depends on 2 (PomodoroTimer, overlay)
  ↓
Feature Area 3 (Calendar & Scheduling)         ← depends on 2 (PomodoroTimer for scheduled sessions)
  ↓
Feature Area 4 (Statistics & Analytics)        ← depends on 3 (scheduling data for dashboard)
  ↓
Feature Area 6 (Settings View)                 ← depends on all above (configures all features)
```

Feature Areas 3 and 4 can be partially parallelized (backend steps are independent).

---

## Full File Summary

### New Files (~65+)

| Area | New Files | New Test Files |
|------|-----------|----------------|
| 1 — Session UI | 11 | 0 |
| 2 — Pomodoro Visualization | 6 | 1 |
| 3 — Calendar & Scheduling | 14 | 3 |
| 4 — Statistics & Analytics | 17 | 4 |
| 5 — System Tray & Notifications | 5 | 0 |
| 6 — Settings View | 3 | 0 |
| **Total** | **~56** | **~8** |

### Modified Files (~15 unique, touched across multiple areas)

| File | Areas |
|------|-------|
| `src/FocusGuard.Core/ServiceCollectionExtensions.cs` | 2, 3, 4 |
| `src/FocusGuard.Core/Data/FocusGuardDbContext.cs` | 3, 4 |
| `src/FocusGuard.Core/Data/DatabaseMigrator.cs` | 3, 4 |
| `src/FocusGuard.Core/Data/Repositories/IFocusSessionRepository.cs` | 4 |
| `src/FocusGuard.Core/Data/Repositories/FocusSessionRepository.cs` | 4 |
| `src/FocusGuard.Core/Security/SettingsKeys.cs` | 4, 5 |
| `src/FocusGuard.App/FocusGuard.App.csproj` | 4, 5 |
| `src/FocusGuard.App/App.xaml` | 3, 4, 6 |
| `src/FocusGuard.App/App.xaml.cs` | 1, 3, 4, 5, 6 |
| `src/FocusGuard.App/Views/MainWindow.xaml` | 3, 4, 6 |
| `src/FocusGuard.App/Views/MainWindow.xaml.cs` | 5 |
| `src/FocusGuard.App/ViewModels/MainWindowViewModel.cs` | 3, 4, 6 |
| `src/FocusGuard.App/ViewModels/DashboardViewModel.cs` | 1, 2, 3, 4 |
| `src/FocusGuard.App/Views/DashboardView.xaml` | 1, 2, 3, 4 |
| `src/FocusGuard.App/Services/IDialogService.cs` | 1, 3 |
| `src/FocusGuard.App/Services/DialogService.cs` | 1, 3 |
| `src/FocusGuard.App/Services/BlockingOrchestrator.cs` | 3, 5 |

---

## Risk Mitigations

| Risk | Mitigation |
|------|------------|
| **Paste bypass in unlock dialog** | `DataObject.AddPastingHandler` + `CommandManager` covers Ctrl+V, right-click, drag-drop. Also disable IME paste |
| **Timer accuracy drift** | Use `DateTime.UtcNow` wall-clock for elapsed, not accumulated ticks. 1s timer is UI refresh only |
| **Overlay window focus stealing** | Set `ShowActivated="False"` on overlay |
| **LiveCharts2 performance** | Limit heatmap to 91 days, bar chart to 31 points, pie chart to top 10 profiles |
| **Toast notifications Win10 vs Win11** | `Microsoft.Toolkit.Uwp.Notifications` handles both. Fallback to balloon tip |
| **CSV injection** | Prefix `=`, `+`, `-`, `@` values with single quote |
| **Scheduling engine precision** | 15s polling is acceptable variance for scheduled sessions |
| **Recurring sessions unbounded** | OccurrenceExpander only expands within requested date range (never unbounded) |
| **WPF dispatcher threading** | All UI updates from timer/session events marshal via `Application.Current.Dispatcher.InvokeAsync` |
| **System tray icon on background thread** | `NotifyIcon.Visible = true` on UI thread. Use Dispatcher if set from background |
| **Large dataset statistics queries** | Indexes on FocusSessions.StartTime and BlockedAttempts.Timestamp. Filter by date range before aggregation |
| **NotifyIcon + WPF coexistence** | `UseWindowsForms` well-supported in .NET 8 WPF projects |

---

## End-to-End Verification Checklist

### Session Interaction
1. Fresh install → master key dialog → save key → main window
2. Click profile card → start session dialog → set 25m + Pomodoro → Start
3. Blocking activates → timer counts down → overlay appears → tray tooltip shows time
4. End Session Early → unlock dialog → paste disabled → type password → session ends
5. Emergency unlock → expand → type master key → session ends
6. Wrong password → denied → session continues

### Pomodoro Timer
7. Work (25m) → notification "Short Break" + sound → break → notification "Back to Work!" → repeat
8. Long break after 4 work sessions
9. Progress ring fills smoothly, timer display accurate

### Calendar & Scheduling
10. Calendar button → monthly grid renders → navigate months → Today button
11. Click day → side panel shows sessions → Create session with profile picker + times
12. Recurring session (Weekdays) → blocks appear Mon–Fri
13. Weekly view → drag-and-drop → dialog opens with pre-filled times
14. Auto-activation: scheduled session starts automatically at scheduled time
15. Dashboard shows Today's Schedule

### Statistics & Analytics
16. Statistics button → summary cards, bar chart, pie chart, heatmap
17. Toggle Day/Week/Month → charts update
18. Streak counter increments with consecutive days
19. Set goal → progress bar on dashboard + statistics
20. Export CSV → valid file with correct data
21. Blocked app attempts logged and counted

### System Tray & Notifications
22. Tray icon visible → tooltip shows status → right-click → context menu
23. Minimize → hides to tray → double-click → restores
24. Close during session → hides to tray (doesn't exit)
25. Toast notifications for all event categories
26. Notification preferences toggle independently

### Settings
27. Settings button → grouped settings view
28. Toggle auto-start → registry updated
29. Change Pomodoro durations → persisted
30. Strict mode toggle disabled during active session
31. All settings survive app restart
