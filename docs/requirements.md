# FocusGuard - Requirements & Technical Design

## 1. Project Overview

FocusGuard is a Windows desktop application designed to help users maintain focus by blocking distracting websites and applications during scheduled focus sessions. The app features multiple blacklist profiles, a Pomodoro timer, a visual calendar scheduler, and a random-text password mechanism to prevent impulsive unlocking of focus mode.

**Target Platform:** Windows 10/11
**Technology:** C# / .NET 8 / WPF

---

## 2. Core Features

### 2.1 Blacklist Management (Multi-Profile)

- Users can create multiple blacklist profiles (e.g., "Deep Work", "Study", "Writing"), each containing:
  - **Blocked Websites:** Domain list (e.g., `youtube.com`, `reddit.com`). Supports wildcard matching (e.g., `*.youtube.com`).
  - **Blocked Applications:** Process name list (e.g., `chrome.exe`, `steam.exe`). Supports browsing and selecting from installed programs.
- Each profile can be independently activated or linked to a calendar schedule.
- Profiles support import/export (JSON format) for backup and sharing.
- Built-in preset profiles (e.g., "Social Media", "Gaming", "Entertainment") for quick setup.

### 2.2 Random Text Password (Anti-Unlock Protection)

- When a focus session starts, the system generates a random text string (configurable length, default 30 characters).
- To unlock (end a focus session early), the user must **manually type the entire random string exactly** — no copy-paste allowed.
- Password difficulty levels:
  - **Easy:** Lowercase letters only (e.g., `abcdefghij`)
  - **Medium:** Mixed case + digits (e.g., `aB3kF9mQ2x`)
  - **Hard:** Mixed case + digits + special characters (e.g., `aB3$kF9@mQ`)
- The password is NOT displayed until the user requests to unlock, adding a psychological barrier.
- Optional: Require typing the password multiple times for sessions longer than 2 hours.
- Emergency override: A master recovery key generated during first setup, stored securely, intended only for genuine emergencies.

### 2.3 Visual Calendar System

- A monthly/weekly calendar view to schedule focus sessions in advance.
- Drag-and-drop time block creation on the calendar.
- Each time block specifies:
  - Start time and end time
  - Which blacklist profile to activate
  - Optional: Pomodoro mode (auto-cycle work/break intervals)
- Recurring schedules (e.g., "Every weekday 9:00-12:00, activate Deep Work profile").
- Color-coded by blacklist profile for visual clarity.
- Today view: A timeline showing upcoming and active focus sessions.

### 2.4 Pomodoro Timer

- Configurable work/break intervals (default: 25 min work, 5 min short break, 15 min long break).
- Long break interval configurable (default: every 4 work sessions).
- Visual countdown timer with progress ring/bar.
- Desktop notifications at session transitions (work -> break, break -> work).
- Optional sound alerts (configurable or mutable).
- Auto-start next session option or manual start.
- Pomodoro can run independently or be linked to a calendar-scheduled focus session.

### 2.5 Statistics & Analytics

- **Daily/Weekly/Monthly** summary views.
- Track per-profile focus time (e.g., "Deep Work: 4h 30m this week").
- Track completed Pomodoro sessions count.
- Blocked attempts log: How many times a blocked site/app was attempted (indicates distraction frequency).
- Streak tracking: Consecutive days with completed focus sessions.
- Charts and visualizations:
  - Bar chart: Daily focus hours over the past week/month.
  - Pie chart: Time distribution across profiles.
  - Heatmap: Focus intensity across days (GitHub contribution-style).
- Data export to CSV for external analysis.

---

## 3. Additional Features

### 3.1 System Tray Integration

- App minimizes to system tray instead of closing.
- Tray icon shows current status (idle / focus active / break).
- Right-click tray menu: Quick start focus session, view timer, open main window, exit.
- Tray tooltip shows remaining time in current session.

### 3.2 Auto-Start on Boot

- Option to register the app in Windows startup (via Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
- Start minimized to tray on boot.
- Auto-activate scheduled focus sessions without user interaction.

### 3.3 Notification System

- Desktop toast notifications for:
  - Focus session starting/ending.
  - Pomodoro work/break transitions.
  - Blocked app/website access attempts (subtle, non-intrusive).
  - Daily focus goal reached.
- Notification preferences configurable (enable/disable per category).

### 3.4 Focus Goals

- Set daily/weekly focus time goals (e.g., "4 hours of Deep Work per day").
- Progress indicator in the main dashboard.
- Celebration/notification when a goal is reached.

### 3.5 Strict Mode

- When enabled, the app itself cannot be closed or killed via Task Manager during a focus session.
- Achieved by running a background watchdog service that restarts the app if terminated.
- Strict mode can only be toggled when no focus session is active.

### 3.6 Whitelist Override

- Within a blacklist profile, allow specific exceptions (e.g., block all of `reddit.com` except `reddit.com/r/programming`).
- Useful for profiles that need partial access.

---

## 4. Technical Architecture

### 4.1 Solution Structure

```
FocusGuard/
├── FocusGuard.sln
├── src/
│   ├── FocusGuard.App/              # WPF UI application
│   │   ├── Views/                   # XAML views
│   │   ├── ViewModels/              # MVVM view models
│   │   ├── Models/                  # Data models
│   │   ├── Services/                # Business logic services
│   │   ├── Controls/                # Custom WPF controls
│   │   ├── Converters/              # Value converters
│   │   └── Assets/                  # Icons, sounds, etc.
│   ├── FocusGuard.Core/             # Core logic (no UI dependency)
│   │   ├── Blocking/                # Website & app blocking engines
│   │   ├── Scheduling/              # Calendar & timer logic
│   │   ├── Statistics/              # Data collection & aggregation
│   │   ├── Security/                # Password generation & validation
│   │   └── Configuration/           # Settings & profile management
│   └── FocusGuard.Service/          # Background watchdog service (optional)
└── tests/
    ├── FocusGuard.Core.Tests/
    └── FocusGuard.App.Tests/
```

### 4.2 Technology Stack

| Component | Technology |
|---|---|
| **Framework** | .NET 8 (LTS) |
| **UI** | WPF (Windows Presentation Foundation) with MVVM pattern |
| **MVVM Toolkit** | CommunityToolkit.Mvvm |
| **Database** | SQLite via Entity Framework Core (local data storage) |
| **Charts** | LiveCharts2 or OxyPlot |
| **Calendar Control** | Custom WPF control or WPF Toolkit Calendar |
| **Notifications** | Microsoft.Toolkit.Uwp.Notifications (toast notifications) |
| **Dependency Injection** | Microsoft.Extensions.DependencyInjection |
| **Logging** | Serilog |
| **Packaging** | dotnet publish single-file + Inno Setup for installer |
| **Testing** | xUnit + Moq |

### 4.3 Website Blocking Mechanism

**Primary approach: Hosts file modification**

- Redirect blocked domains to `127.0.0.1` by writing entries to `C:\Windows\System32\drivers\etc\hosts`.
- Requires **administrator privileges** (app must run as admin or use a helper service).
- On focus session start: Append blocked domains to hosts file.
- On focus session end: Remove the appended entries.
- Use file markers (comment tags) to identify FocusGuard-managed entries:

```
# >>> FocusGuard START - DO NOT EDIT <<<
127.0.0.1 youtube.com
127.0.0.1 www.youtube.com
127.0.0.1 reddit.com
127.0.0.1 www.reddit.com
# >>> FocusGuard END <<<
```

**DNS cache flush** after modification: Execute `ipconfig /flushdns` to apply immediately.

### 4.4 Application Blocking Mechanism

- Use **WMI (Windows Management Instrumentation)** event subscription to detect new process creation in real-time.
- Query: `SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'`
- When a blocked process is detected, immediately terminate it via `Process.Kill()`.
- Also perform periodic polling (every 2 seconds) as a fallback.
- Show a brief toast notification: "FocusGuard blocked [AppName]."

### 4.5 Data Storage (SQLite Schema Overview)

```
Profiles
├── Id (PK)
├── Name
├── Color
├── BlockedWebsites (JSON)
├── BlockedApplications (JSON)
└── CreatedAt

ScheduledSessions
├── Id (PK)
├── ProfileId (FK)
├── StartTime
├── EndTime
├── IsRecurring
├── RecurrenceRule
└── PomodoroEnabled

FocusSessions (completed sessions log)
├── Id (PK)
├── ProfileId (FK)
├── StartTime
├── EndTime
├── PlannedDuration
├── ActualDuration
├── PomodoroCount
├── WasUnlockedEarly
└── BlockedAttempts

BlockedAttemptLogs
├── Id (PK)
├── SessionId (FK)
├── Timestamp
├── Type (Website / Application)
└── Target (domain or process name)

Settings
├── Key (PK)
└── Value
```

### 4.6 Security Considerations

- The app requires **administrator privileges** to modify the hosts file and terminate processes.
- Random password strings are generated using `System.Security.Cryptography.RandomNumberGenerator` (cryptographically secure).
- Paste detection: The unlock input field disables clipboard paste via WPF input handling.
- Settings and profile data are stored locally in `%APPDATA%\FocusGuard\`.
- Master recovery key is hashed (SHA-256 + salt) before storage; the plaintext is shown only once during setup.

---

## 5. UI Layout Overview

### 5.1 Main Window - Dashboard

- **Left sidebar:** Navigation menu (Dashboard, Profiles, Calendar, Statistics, Settings).
- **Center area:** Changes based on selected navigation item.
- **Dashboard view:**
  - Current status card (Idle / Focus Active with timer).
  - Quick-start buttons for each profile.
  - Today's schedule timeline.
  - Weekly focus summary mini-chart.

### 5.2 Profiles View

- List of profiles with edit/delete/duplicate actions.
- Profile editor: Name, color picker, website list editor, application list editor.

### 5.3 Calendar View

- Monthly calendar grid with colored time blocks.
- Click/drag to create new scheduled sessions.
- Side panel shows details of selected day's sessions.

### 5.4 Statistics View

- Date range selector (day/week/month/custom).
- Summary cards (total focus time, sessions count, streak).
- Charts area (bar, pie, heatmap).

### 5.5 Timer Overlay

- A small, always-on-top floating window showing the Pomodoro timer.
- Can be dragged anywhere on screen.
- Minimal design: circular progress + time remaining.

---

## 6. Packaging & Distribution

- **Build:** `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
- **Installer:** Inno Setup script to create a standard Windows installer (.exe).
  - Registers auto-start in Windows Registry.
  - Creates Start Menu shortcuts.
  - Sets up `%APPDATA%\FocusGuard\` data directory.
- **Portable mode:** Also support running without installation (single .exe + local data folder).

---

## 7. Development Phases

### Phase 1: Core Foundation
- Project scaffolding (solution structure, DI, MVVM setup).
- Basic main window with sidebar navigation.
- Profile CRUD (create, read, update, delete blacklists).
- Hosts file blocking engine.
- Process blocking engine.

### Phase 2: Focus Session & Timer
- Focus session lifecycle (start, active, end).
- Random text password generation and unlock flow.
- Pomodoro timer with work/break cycles.
- Timer floating overlay window.
- System tray integration.

### Phase 3: Calendar & Scheduling
- Calendar UI control (monthly/weekly view).
- Drag-and-drop session scheduling.
- Recurring schedule support.
- Auto-activation of scheduled sessions.

### Phase 4: Statistics & Polish
- SQLite logging of completed sessions and blocked attempts.
- Statistics dashboard with charts.
- Focus goals and streak tracking.
- Desktop notifications.
- Auto-start on boot.

### Phase 5: Hardening
- Strict mode (anti-kill protection).
- Installer creation (Inno Setup).
- Edge case handling and error recovery.
- Testing and bug fixes.
