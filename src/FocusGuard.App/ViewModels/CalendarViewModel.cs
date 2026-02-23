using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.App.Models;
using FocusGuard.App.Services;
using FocusGuard.Core.Data.Entities;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Scheduling;
using Microsoft.Extensions.Logging;

namespace FocusGuard.App.ViewModels;

public partial class CalendarViewModel : ViewModelBase
{
    private readonly IScheduledSessionRepository _scheduledSessionRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly OccurrenceExpander _occurrenceExpander;
    private readonly ISchedulingEngine _schedulingEngine;
    private readonly IDialogService _dialogService;
    private readonly ILogger<CalendarViewModel> _logger;

    private DateTime _currentMonth;
    private Dictionary<Guid, ProfileSummary> _profileLookup = [];

    [ObservableProperty]
    private string _monthYearDisplay = string.Empty;

    [ObservableProperty]
    private CalendarDay? _selectedDay;

    public ObservableCollection<CalendarDay> Days { get; } = [];
    public ObservableCollection<CalendarTimeBlock> SelectedDayBlocks { get; } = [];

    public CalendarViewModel(
        IScheduledSessionRepository scheduledSessionRepository,
        IProfileRepository profileRepository,
        OccurrenceExpander occurrenceExpander,
        ISchedulingEngine schedulingEngine,
        IDialogService dialogService,
        ILogger<CalendarViewModel> logger)
    {
        _scheduledSessionRepository = scheduledSessionRepository;
        _profileRepository = profileRepository;
        _occurrenceExpander = occurrenceExpander;
        _schedulingEngine = schedulingEngine;
        _dialogService = dialogService;
        _logger = logger;

        _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    }

    public override async void OnNavigatedTo()
    {
        await LoadProfileLookupAsync();
        await BuildMonthGridAsync();
    }

    [RelayCommand]
    private async Task PreviousMonth()
    {
        _currentMonth = _currentMonth.AddMonths(-1);
        await BuildMonthGridAsync();
    }

    [RelayCommand]
    private async Task NextMonth()
    {
        _currentMonth = _currentMonth.AddMonths(1);
        await BuildMonthGridAsync();
    }

    [RelayCommand]
    private async Task GoToToday()
    {
        _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        await BuildMonthGridAsync();
        SelectDay(Days.FirstOrDefault(d => d.IsToday));
    }

    [RelayCommand]
    private void SelectDay(CalendarDay? day)
    {
        if (day is null) return;

        // Clear previous selection
        if (SelectedDay is not null)
            SelectedDay.IsSelected = false;

        SelectedDay = day;
        day.IsSelected = true;

        SelectedDayBlocks.Clear();
        foreach (var block in day.TimeBlocks)
        {
            SelectedDayBlocks.Add(block);
        }
    }

    [RelayCommand]
    private async Task CreateScheduledSession()
    {
        try
        {
            var profiles = _profileLookup.Values.ToList();
            var date = SelectedDay?.Date ?? DateTime.Today;

            var result = await _dialogService.ShowScheduleSessionDialogAsync(profiles, date);
            if (result is null) return;

            var entity = new ScheduledSessionEntity
            {
                Id = Guid.NewGuid(),
                ProfileId = result.ProfileId,
                StartTime = result.StartTime,
                EndTime = result.EndTime,
                PomodoroEnabled = result.PomodoroEnabled,
                IsRecurring = result.IsRecurring,
                RecurrenceRule = result.RecurrenceRule is not null
                    ? JsonSerializer.Serialize(result.RecurrenceRule)
                    : null,
                IsEnabled = true
            };

            await _scheduledSessionRepository.CreateAsync(entity);
            await _schedulingEngine.RefreshAsync();
            await BuildMonthGridAsync();

            // Re-select the day to refresh side panel
            if (SelectedDay is not null)
            {
                var refreshedDay = Days.FirstOrDefault(d => d.Date == SelectedDay.Date);
                if (refreshedDay is not null)
                    SelectDay(refreshedDay);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create scheduled session");
            MessageBox.Show($"Failed to schedule session: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteScheduledSession(Guid sessionId)
    {
        try
        {
            var confirmed = await _dialogService.ConfirmAsync("Delete Session",
                "Are you sure you want to delete this scheduled session?");
            if (!confirmed) return;

            await _scheduledSessionRepository.DeleteAsync(sessionId);
            await _schedulingEngine.RefreshAsync();
            await BuildMonthGridAsync();

            // Re-select the day to refresh side panel
            if (SelectedDay is not null)
            {
                var refreshedDay = Days.FirstOrDefault(d => d.Date == SelectedDay.Date);
                if (refreshedDay is not null)
                    SelectDay(refreshedDay);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete scheduled session");
        }
    }

    private async Task LoadProfileLookupAsync()
    {
        try
        {
            var profiles = await _profileRepository.GetAllAsync();
            _profileLookup = profiles.ToDictionary(
                p => p.Id,
                p => new ProfileSummary
                {
                    Id = p.Id,
                    Name = p.Name,
                    Color = p.Color,
                    IsPreset = p.IsPreset
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load profiles");
        }
    }

    private async Task BuildMonthGridAsync()
    {
        try
        {
            MonthYearDisplay = _currentMonth.ToString("MMMM yyyy");

            // Calculate the 42-cell grid (6 weeks)
            var firstDayOfMonth = _currentMonth;
            var daysInMonth = DateTime.DaysInMonth(_currentMonth.Year, _currentMonth.Month);

            // Find the Monday of the week containing the 1st
            var startOffset = ((int)firstDayOfMonth.DayOfWeek + 6) % 7; // Monday = 0
            var gridStart = firstDayOfMonth.AddDays(-startOffset);

            // Load all scheduled sessions for the visible range
            var gridEnd = gridStart.AddDays(42);
            var rangeStartUtc = gridStart.ToUniversalTime();
            var rangeEndUtc = gridEnd.ToUniversalTime();

            var sessions = await _scheduledSessionRepository.GetAllAsync();

            // Expand occurrences
            var allOccurrences = new List<ScheduledOccurrence>();
            foreach (var session in sessions)
            {
                allOccurrences.AddRange(_occurrenceExpander.Expand(session, rangeStartUtc, rangeEndUtc));
            }

            // Group occurrences by local date
            var occurrencesByDate = allOccurrences
                .GroupBy(o => o.StartTime.ToLocalTime().Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            var today = DateTime.Today;
            Days.Clear();

            for (int i = 0; i < 42; i++)
            {
                var date = gridStart.AddDays(i);
                var isCurrentMonth = date.Month == _currentMonth.Month;
                var isToday = date == today;

                var timeBlocks = new ObservableCollection<CalendarTimeBlock>();
                if (occurrencesByDate.TryGetValue(date, out var dayOccurrences))
                {
                    foreach (var occ in dayOccurrences.OrderBy(o => o.StartTime))
                    {
                        var profile = _profileLookup.GetValueOrDefault(occ.ProfileId);
                        timeBlocks.Add(new CalendarTimeBlock
                        {
                            ScheduledSessionId = occ.ScheduledSessionId,
                            ProfileId = occ.ProfileId,
                            ProfileName = profile?.Name ?? "Unknown",
                            ProfileColor = profile?.Color ?? "#4A90D9",
                            StartTime = occ.StartTime,
                            EndTime = occ.EndTime,
                            IsRecurring = sessions.FirstOrDefault(s => s.Id == occ.ScheduledSessionId)?.IsRecurring ?? false,
                            PomodoroEnabled = occ.PomodoroEnabled
                        });
                    }
                }

                Days.Add(new CalendarDay
                {
                    Date = date,
                    IsCurrentMonth = isCurrentMonth,
                    IsToday = isToday,
                    TimeBlocks = timeBlocks
                });
            }

            // Auto-select today or first day of month
            var autoSelect = Days.FirstOrDefault(d => d.IsToday)
                          ?? Days.FirstOrDefault(d => d.IsCurrentMonth);
            if (autoSelect is not null)
                SelectDay(autoSelect);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build calendar grid");
        }
    }
}
