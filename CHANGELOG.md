# Changelog

All notable changes to FocusGuard will be documented in this file.

This project follows [Semantic Versioning](https://semver.org/).

## [1.0.0-beta.1] - 2025-02-22

First public beta release. All core features are implemented and functional.

### Features

#### Website & Application Blocking
- Block distracting websites via system hosts file modification (works across all browsers)
- Block applications with real-time process detection (WMI + polling) and automatic termination
- Multi-profile blacklists — create profiles like "Deep Work", "Study", etc.
- 3 built-in preset profiles: Social Media, Gaming, Entertainment
- Profile import/export via JSON files

#### Focus Sessions
- Timer-based focus sessions with configurable duration (25/45/60/90/120 min or custom)
- Random password unlock protection (Easy/Medium/Hard difficulty) — must be typed manually, no copy-paste
- Master recovery key (SHA-256 hashed) for genuine emergencies, generated on first launch
- Automatic blocking activation when sessions start, deactivation when sessions end

#### Pomodoro Mode
- Optional Pomodoro cycling: Work → Short Break → Work → ... → Long Break
- Configurable work/break durations and long break interval
- Circular progress ring with large countdown timer display
- Interval dot indicators and system sound alerts on transitions
- Auto-start next interval option

#### Calendar & Scheduling
- Monthly calendar view with day selection and session scheduling
- Recurring sessions: Daily, Weekdays, Weekly, Custom day selection
- Background scheduling engine auto-starts sessions at scheduled times (15-second polling)
- Today's schedule overview on dashboard

#### Statistics & Analytics
- Period-based statistics: Day, Week, Month with navigation
- Summary cards: total focus time, session count, Pomodoro count, blocked attempts
- Bar chart (daily breakdown) and pie chart (profile breakdown) via LiveCharts2
- 90-day focus heatmap
- Streak tracking (current and longest consecutive focus days)
- Focus goals with progress bars (daily/weekly, global or per-profile)
- CSV export for sessions and daily summaries

#### System Tray & Notifications
- System tray icon with context menu (show/hide, session status, exit)
- Windows toast notifications for session start/end, Pomodoro transitions, blocked apps
- Floating timer overlay window

#### Settings
- Centralized settings page with 5 sections:
  - **General** — Auto-start on boot, minimize to tray, strict mode
  - **Session Defaults** — Default duration, password difficulty, password length
  - **Pomodoro** — Work/break durations, long break interval, auto-start next
  - **Notifications** — Toggle session, Pomodoro, blocked app, and goal notifications; sound effects
  - **Security** — Master key status and regeneration
- All settings auto-persist on change

#### Hardening & Reliability
- Global exception handling with emergency cleanup (hosts file, process blocker)
- Crash recovery: orphaned session cleanup, stale hosts entry removal
- Strict mode: prevents app closure during active sessions
- Watchdog process: heartbeat monitoring, auto-restart on crash
- Session recovery after crash with remaining time preservation
- Auto-start on Windows boot (registry-based, with `--minimized` flag)
- Portable mode: drop `portable.marker` file for local data storage
- Single-instance enforcement via named Mutex

#### UI
- Dark theme WPF application with MVVM architecture
- Sidebar navigation: Dashboard, Profiles, Calendar, Statistics, Settings
- Start Session dialog with duration presets and Pomodoro toggle
- Unlock dialog with hidden password, paste protection, emergency master key
- Master key setup dialog on first launch

### Technical Details
- Built with C# / .NET 8 / WPF
- SQLite database via Entity Framework Core
- CommunityToolkit.Mvvm for MVVM source generators
- LiveCharts2 for chart visualizations
- Serilog for structured logging (rolling daily files, 7-day retention)
- 209 unit tests (xUnit + Moq + EF Core InMemory)
- Requires administrator privileges (hosts file modification + process termination)
- Inno Setup installer included

[1.0.0-beta.1]: https://github.com/Mosakana/FocusGuard/releases/tag/v1.0.0-beta.1
