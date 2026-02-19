using FocusGuard.App.ViewModels;

namespace FocusGuard.App.Services;

public interface INavigationService
{
    ViewModelBase CurrentView { get; }
    event Action? CurrentViewChanged;
    void NavigateTo<T>() where T : ViewModelBase;
}
