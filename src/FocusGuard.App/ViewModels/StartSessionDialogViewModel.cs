using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.Core.Security;

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
