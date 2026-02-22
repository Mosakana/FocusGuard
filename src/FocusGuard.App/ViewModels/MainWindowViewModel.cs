using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FocusGuard.App.Services;

namespace FocusGuard.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private ViewModelBase _currentView = null!;

    public MainWindowViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.CurrentViewChanged += () => CurrentView = _navigationService.CurrentView;

        // Navigate to Dashboard by default
        _navigationService.NavigateTo<DashboardViewModel>();
    }

    [RelayCommand]
    private void NavigateToDashboard() => _navigationService.NavigateTo<DashboardViewModel>();

    [RelayCommand]
    private void NavigateToProfiles() => _navigationService.NavigateTo<ProfilesViewModel>();

    [RelayCommand]
    private void NavigateToCalendar() => _navigationService.NavigateTo<CalendarViewModel>();

    [RelayCommand]
    private void NavigateToStatistics() => _navigationService.NavigateTo<StatisticsViewModel>();

    [RelayCommand]
    private void NavigateToSettings() => _navigationService.NavigateTo<SettingsViewModel>();
}
