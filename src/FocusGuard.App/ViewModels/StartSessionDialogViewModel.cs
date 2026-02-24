using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.Core.Security;
using FocusGuard.Core.Sessions;

namespace FocusGuard.App.ViewModels;

public partial class StartSessionDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _profileColor = "#4A90D9";

    [ObservableProperty]
    private int _durationMinutes = 25;

    [ObservableProperty]
    private bool _enablePomodoro = true;

    [ObservableProperty]
    private PasswordDifficulty _selectedDifficulty = PasswordDifficulty.Medium;

    [ObservableProperty]
    private PomodoroConfiguration? _pomodoroConfig;

    [ObservableProperty]
    private int _pomodoroCycleCount = 4;

    [ObservableProperty]
    private string _pomodoroDurationDisplay = string.Empty;

    public Guid ProfileId { get; set; }
    public bool Confirmed { get; private set; }

    public PasswordDifficulty[] AvailableDifficulties { get; } =
        [PasswordDifficulty.Easy, PasswordDifficulty.Medium, PasswordDifficulty.Hard];

    [RelayCommand]
    private void SetDuration(string minutes)
    {
        if (int.TryParse(minutes, out var m) && m > 0)
            DurationMinutes = m;
    }

    [RelayCommand]
    private void SetCycleCount(string count)
    {
        if (int.TryParse(count, out var c) && c > 0)
            PomodoroCycleCount = c;
    }

    partial void OnPomodoroCycleCountChanged(int value)
    {
        if (value < 1)
        {
            PomodoroCycleCount = 1;
            return;
        }

        UpdatePomodoroDurationDisplay();

        if (EnablePomodoro)
            DurationMinutes = CalculatePomodoroTotalMinutes(value);
    }

    partial void OnEnablePomodoroChanged(bool value)
    {
        if (value)
        {
            UpdatePomodoroDurationDisplay();
            DurationMinutes = CalculatePomodoroTotalMinutes(PomodoroCycleCount);
        }
    }

    partial void OnPomodoroConfigChanged(PomodoroConfiguration? value)
    {
        UpdatePomodoroDurationDisplay();

        if (EnablePomodoro)
            DurationMinutes = CalculatePomodoroTotalMinutes(PomodoroCycleCount);
    }

    private int CalculatePomodoroTotalMinutes(int cycles)
    {
        var config = PomodoroConfig ?? new PomodoroConfiguration();
        var total = cycles * config.WorkMinutes;
        for (int k = 1; k < cycles; k++)
        {
            total += (k % config.LongBreakInterval == 0)
                ? config.LongBreakMinutes
                : config.ShortBreakMinutes;
        }
        return total;
    }

    private void UpdatePomodoroDurationDisplay()
    {
        var config = PomodoroConfig ?? new PomodoroConfiguration();
        var totalMinutes = CalculatePomodoroTotalMinutes(PomodoroCycleCount);

        string totalStr;
        if (totalMinutes >= 60)
        {
            var hours = totalMinutes / 60;
            var mins = totalMinutes % 60;
            totalStr = mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
        }
        else
        {
            totalStr = $"{totalMinutes}m";
        }

        PomodoroDurationDisplay = $"{PomodoroCycleCount} \u00d7 {config.WorkMinutes}m focus + breaks = {totalStr}";
    }

    [RelayCommand]
    private void Confirm(Window window)
    {
        Confirmed = true;
        window.DialogResult = true;
        window.Close();
    }

    [RelayCommand]
    private void Cancel(Window window)
    {
        Confirmed = false;
        window.DialogResult = false;
        window.Close();
    }
}
