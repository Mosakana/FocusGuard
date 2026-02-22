using System.Windows;
using FocusGuard.App.Models;
using FocusGuard.App.ViewModels;
using FocusGuard.App.Views;
using FocusGuard.Core.Sessions;
using FocusGuard.Core.Statistics;
using Microsoft.Win32;

namespace FocusGuard.App.Services;

public class DialogService : IDialogService
{
    private readonly IFocusSessionManager _sessionManager;

    public DialogService(IFocusSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public Task<string?> OpenFileAsync(string filter, string title = "Open File")
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Title = title
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> SaveFileAsync(string filter, string defaultFileName = "", string title = "Save File")
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = title,
            FileName = defaultFileName
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<StartSessionDialogResult?> ShowStartSessionDialogAsync(
        Guid profileId, string profileName, string profileColor)
    {
        var vm = new StartSessionDialogViewModel
        {
            ProfileId = profileId,
            ProfileName = profileName,
            ProfileColor = profileColor
        };

        var dialog = new StartSessionDialog
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };

        var ok = dialog.ShowDialog() == true;
        if (!ok || !vm.Confirmed)
            return Task.FromResult<StartSessionDialogResult?>(null);

        return Task.FromResult<StartSessionDialogResult?>(new StartSessionDialogResult
        {
            DurationMinutes = vm.DurationMinutes,
            EnablePomodoro = vm.EnablePomodoro,
            Difficulty = vm.SelectedDifficulty
        });
    }

    public Task<UnlockDialogResult?> ShowUnlockDialogAsync(string generatedPassword)
    {
        var vm = new UnlockDialogViewModel(_sessionManager)
        {
            GeneratedPassword = generatedPassword
        };

        var dialog = new UnlockDialog
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };

        var ok = dialog.ShowDialog() == true;
        if (!ok || !vm.UnlockSucceeded)
            return Task.FromResult<UnlockDialogResult?>(null);

        return Task.FromResult<UnlockDialogResult?>(new UnlockDialogResult
        {
            Unlocked = vm.UnlockSucceeded,
            EmergencyUsed = vm.EmergencyUnlockUsed
        });
    }

    public Task<ScheduleSessionDialogResult?> ShowScheduleSessionDialogAsync(
        List<ProfileSummary> profiles, DateTime defaultDate)
    {
        var vm = new ScheduleSessionDialogViewModel
        {
            SessionDate = defaultDate
        };
        foreach (var p in profiles)
            vm.AvailableProfiles.Add(p);
        if (profiles.Count > 0)
            vm.SelectedProfile = profiles[0];

        var dialog = new ScheduleSessionDialog
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };

        var ok = dialog.ShowDialog() == true;
        return Task.FromResult(vm.GetResult());
    }

    public Task<FocusGoal?> ShowSetGoalDialogAsync()
    {
        var vm = new SetGoalDialogViewModel();

        var dialog = new SetGoalDialog
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };

        var ok = dialog.ShowDialog() == true;
        return Task.FromResult(vm.GetResult());
    }
}
