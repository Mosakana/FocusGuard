using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.Core.Statistics;

namespace FocusGuard.App.ViewModels;

public partial class SetGoalDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDailySelected = true;

    [ObservableProperty]
    private int _targetHours = 1;

    [ObservableProperty]
    private int _targetMinutes;

    [ObservableProperty]
    private bool _confirmed;

    public List<int> HourOptions { get; } = Enumerable.Range(0, 13).ToList();
    public List<int> MinuteOptions { get; } = [0, 15, 30, 45];

    public FocusGoal? GetResult()
    {
        if (!Confirmed) return null;

        var totalMinutes = TargetHours * 60 + TargetMinutes;
        if (totalMinutes <= 0) return null;

        return new FocusGoal
        {
            Period = IsDailySelected ? GoalPeriod.Daily : GoalPeriod.Weekly,
            TargetMinutes = totalMinutes,
            ProfileId = null
        };
    }

    [RelayCommand]
    private void Confirm()
    {
        Confirmed = true;
    }
}
