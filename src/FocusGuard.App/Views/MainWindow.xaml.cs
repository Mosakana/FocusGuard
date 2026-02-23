using System.Windows;
using FocusGuard.App.ViewModels;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Hardening;
using FocusGuard.Core.Security;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.DependencyInjection;

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

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized)
        {
            // Check if minimize-to-tray is enabled
            try
            {
                using var scope = App.Services.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
                var value = settings.GetAsync(SettingsKeys.MinimizeToTray).GetAwaiter().GetResult();

                if (value is not null && bool.TryParse(value, out var enabled) && enabled)
                {
                    Hide();
                    ShowInTaskbar = false;
                }
            }
            catch
            {
                // Settings not available — ignore
            }
        }
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var isSessionActive = _sessionManager.CurrentState != FocusSessionState.Idle
                           && _sessionManager.CurrentState != FocusSessionState.Ended;

        if (isSessionActive)
        {
            // During active session: strict mode prevents close entirely
            if (await _strictModeService.IsEnabledAsync())
            {
                e.Cancel = true;
                return;
            }

            // Active session without strict mode: hide to tray
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            return;
        }

        // No active session: check minimize-to-tray setting
        try
        {
            using var scope = App.Services.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var value = settings.GetAsync(SettingsKeys.MinimizeToTray).GetAwaiter().GetResult();

            if (value is not null && bool.TryParse(value, out var minimizeToTray) && minimizeToTray)
            {
                // Hide to tray instead of closing
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }
        }
        catch
        {
            // Settings not available — proceed with close
        }

        // Actually closing: perform full cleanup and exit process
        base.OnClosing(e);
        App.PerformShutdown();
    }

    public void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }
}
