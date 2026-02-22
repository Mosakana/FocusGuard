using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.App.Models;
using FocusGuard.Core.Scheduling;

namespace FocusGuard.App.ViewModels;

public partial class ScheduleSessionDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private ProfileSummary? _selectedProfile;

    [ObservableProperty]
    private DateTime _sessionDate = DateTime.Today;

    [ObservableProperty]
    private int _startHour = 9;

    [ObservableProperty]
    private int _startMinute;

    [ObservableProperty]
    private int _endHour = 10;

    [ObservableProperty]
    private int _endMinute;

    [ObservableProperty]
    private bool _pomodoroEnabled;

    [ObservableProperty]
    private bool _isRecurring;

    [ObservableProperty]
    private RecurrenceType _selectedRecurrenceType = RecurrenceType.Daily;

    [ObservableProperty]
    private bool _mondayChecked;

    [ObservableProperty]
    private bool _tuesdayChecked;

    [ObservableProperty]
    private bool _wednesdayChecked;

    [ObservableProperty]
    private bool _thursdayChecked;

    [ObservableProperty]
    private bool _fridayChecked;

    [ObservableProperty]
    private bool _saturdayChecked;

    [ObservableProperty]
    private bool _sundayChecked;

    [ObservableProperty]
    private DateTime? _recurrenceEndDate;

    [ObservableProperty]
    private string _durationDisplay = "60 min";

    public bool Confirmed { get; private set; }

    public ObservableCollection<ProfileSummary> AvailableProfiles { get; } = [];

    public RecurrenceType[] AvailableRecurrenceTypes { get; } =
        [RecurrenceType.Daily, RecurrenceType.Weekdays, RecurrenceType.Weekly, RecurrenceType.Custom];

    public int[] AvailableHours { get; } = Enumerable.Range(0, 24).ToArray();
    public int[] AvailableMinutes { get; } = [0, 15, 30, 45];

    // For editing existing sessions
    public Guid? EditingSessionId { get; set; }

    partial void OnStartHourChanged(int value) => UpdateDuration();
    partial void OnStartMinuteChanged(int value) => UpdateDuration();
    partial void OnEndHourChanged(int value) => UpdateDuration();
    partial void OnEndMinuteChanged(int value) => UpdateDuration();

    private void UpdateDuration()
    {
        var start = new TimeSpan(StartHour, StartMinute, 0);
        var end = new TimeSpan(EndHour, EndMinute, 0);
        var duration = end - start;
        if (duration.TotalMinutes <= 0)
            duration = duration.Add(TimeSpan.FromHours(24)); // crosses midnight
        DurationDisplay = $"{(int)duration.TotalMinutes} min";
    }

    [RelayCommand]
    private void Confirm(Window window)
    {
        if (SelectedProfile is null) return;
        Confirmed = true;
        window.DialogResult = true;
        window.Close();
    }

    [RelayCommand]
    private void Cancel(Window window)
    {
        window.DialogResult = false;
        window.Close();
    }

    public ScheduleSessionDialogResult? GetResult()
    {
        if (!Confirmed || SelectedProfile is null) return null;

        var startTime = SessionDate.Date.Add(new TimeSpan(StartHour, StartMinute, 0));
        var endTime = SessionDate.Date.Add(new TimeSpan(EndHour, EndMinute, 0));
        if (endTime <= startTime)
            endTime = endTime.AddDays(1); // crosses midnight

        // Convert local to UTC
        startTime = startTime.ToUniversalTime();
        endTime = endTime.ToUniversalTime();

        RecurrenceRule? rule = null;
        if (IsRecurring)
        {
            rule = new RecurrenceRule
            {
                Type = SelectedRecurrenceType,
                EndDate = RecurrenceEndDate?.ToUniversalTime(),
                DaysOfWeek = GetSelectedDays()
            };
        }

        return new ScheduleSessionDialogResult
        {
            ProfileId = SelectedProfile.Id,
            StartTime = startTime,
            EndTime = endTime,
            PomodoroEnabled = PomodoroEnabled,
            IsRecurring = IsRecurring,
            RecurrenceRule = rule
        };
    }

    private List<DayOfWeek> GetSelectedDays()
    {
        var days = new List<DayOfWeek>();
        if (MondayChecked) days.Add(DayOfWeek.Monday);
        if (TuesdayChecked) days.Add(DayOfWeek.Tuesday);
        if (WednesdayChecked) days.Add(DayOfWeek.Wednesday);
        if (ThursdayChecked) days.Add(DayOfWeek.Thursday);
        if (FridayChecked) days.Add(DayOfWeek.Friday);
        if (SaturdayChecked) days.Add(DayOfWeek.Saturday);
        if (SundayChecked) days.Add(DayOfWeek.Sunday);
        return days;
    }
}
