using System.Windows;
using FocusGuard.App.ViewModels;
using FocusGuard.App.Views;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FocusGuard.App.Services;

public class OverlayService : IOverlayService
{
    private readonly IFocusSessionManager _sessionManager;
    private readonly PomodoroTimer _pomodoroTimer;
    private readonly ILogger<OverlayService> _logger;

    private TimerOverlayWindow? _overlayWindow;
    private TimerOverlayViewModel? _overlayViewModel;
    private bool _disposed;

    public bool IsVisible => _overlayWindow is not null;

    public OverlayService(
        IFocusSessionManager sessionManager,
        PomodoroTimer pomodoroTimer,
        ILogger<OverlayService> logger)
    {
        _sessionManager = sessionManager;
        _pomodoroTimer = pomodoroTimer;
        _logger = logger;
    }

    public void Initialize()
    {
        _sessionManager.StateChanged += OnSessionStateChanged;

        // If a session is already active (e.g. recovery), show overlay
        if (_sessionManager.CurrentState is FocusSessionState.Working
            or FocusSessionState.ShortBreak or FocusSessionState.LongBreak)
        {
            ShowOverlay();
        }

        _logger.LogInformation("Overlay service initialized");
    }

    public void ShowOverlay()
    {
        if (_disposed || _overlayWindow is not null) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            _overlayViewModel = new TimerOverlayViewModel(_sessionManager, _pomodoroTimer);
            _overlayWindow = new TimerOverlayWindow
            {
                DataContext = _overlayViewModel
            };

            // Position at bottom-right of work area
            var workArea = SystemParameters.WorkArea;
            _overlayWindow.Left = workArea.Right - 200;
            _overlayWindow.Top = workArea.Bottom - 200;

            _overlayWindow.Show();
            _logger.LogDebug("Timer overlay shown");
        });
    }

    public void HideOverlay()
    {
        if (_overlayWindow is null) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            _overlayWindow.Close();
            _overlayWindow = null;

            _overlayViewModel?.Dispose();
            _overlayViewModel = null;

            _logger.LogDebug("Timer overlay hidden");
        });
    }

    private void OnSessionStateChanged(object? sender, FocusSessionState state)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            switch (state)
            {
                case FocusSessionState.Working when _overlayWindow is null:
                    ShowOverlay();
                    break;
                case FocusSessionState.Idle:
                case FocusSessionState.Ended:
                    HideOverlay();
                    break;
                // ShortBreak, LongBreak → overlay stays visible
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sessionManager.StateChanged -= OnSessionStateChanged;
        HideOverlay();
    }
}
