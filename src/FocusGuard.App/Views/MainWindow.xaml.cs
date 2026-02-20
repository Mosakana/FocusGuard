using System.Windows;
using FocusGuard.App.ViewModels;
using FocusGuard.Core.Hardening;
using FocusGuard.Core.Sessions;

namespace FocusGuard.App.Views;

public partial class MainWindow : Window
{
    private readonly IFocusSessionManager _sessionManager;
    private readonly IStrictModeService _strictModeService;

    public MainWindow(MainWindowViewModel viewModel,
        IFocusSessionManager sessionManager,
        IStrictModeService strictModeService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _sessionManager = sessionManager;
        _strictModeService = strictModeService;
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_sessionManager.CurrentState != FocusSessionState.Idle
            && await _strictModeService.IsEnabledAsync())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
