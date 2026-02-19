using FocusGuard.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FocusGuard.App.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase _currentView = null!;

    public ViewModelBase CurrentView
    {
        get => _currentView;
        private set
        {
            _currentView = value;
            CurrentViewChanged?.Invoke();
        }
    }

    public event Action? CurrentViewChanged;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void NavigateTo<T>() where T : ViewModelBase
    {
        var viewModel = _serviceProvider.GetRequiredService<T>();
        viewModel.OnNavigatedTo();
        CurrentView = viewModel;
    }
}
