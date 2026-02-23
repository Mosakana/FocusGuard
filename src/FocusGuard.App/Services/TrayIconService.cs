using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using FocusGuard.Core.Sessions;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;

namespace FocusGuard.App.Services;

public class TrayIconService : ITrayIconService
{
    private readonly IFocusSessionManager _sessionManager;
    private readonly PomodoroTimer _pomodoroTimer;
    private readonly ILogger<TrayIconService> _logger;

    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private bool _disposed;

    public TrayIconService(
        IFocusSessionManager sessionManager,
        PomodoroTimer pomodoroTimer,
        ILogger<TrayIconService> logger)
    {
        _sessionManager = sessionManager;
        _pomodoroTimer = pomodoroTimer;
        _logger = logger;
    }

    public void Initialize()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Open", null, OnOpenClicked);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, OnExitClicked);

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath),
            Text = "FocusGuard — Idle",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += OnTrayDoubleClick;

        _sessionManager.StateChanged += OnSessionStateChanged;
        _pomodoroTimer.TimerTick += OnTimerTick;

        _logger.LogInformation("Tray icon initialized");
    }

    public void ShowBalloonTip(string title, string text, ToolTipIcon icon, int timeoutMs = 3000)
    {
        if (_notifyIcon is null || _disposed) return;

        try
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                _notifyIcon.ShowBalloonTip(timeoutMs, title, text, icon);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show balloon tip");
        }
    }

    public void UpdateTooltip(string text)
    {
        if (_notifyIcon is null || _disposed) return;

        // NotifyIcon.Text has a 127-character limit
        _notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void OnSessionStateChanged(object? sender, FocusSessionState state)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var session = _sessionManager.CurrentSession;
            var tooltip = state switch
            {
                FocusSessionState.Working => $"FocusGuard — Focusing: {session?.ProfileName ?? "Unknown"}",
                FocusSessionState.ShortBreak => "FocusGuard — Short Break",
                FocusSessionState.LongBreak => "FocusGuard — Long Break",
                _ => "FocusGuard — Idle"
            };

            UpdateTooltip(tooltip);
        });
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var session = _sessionManager.CurrentSession;
            if (session is null) return;

            var remaining = _pomodoroTimer.IsRunning
                ? _pomodoroTimer.IntervalRemaining
                : session.CurrentIntervalRemaining;

            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            var timeStr = remaining.TotalHours >= 1
                ? remaining.ToString(@"h\:mm\:ss")
                : remaining.ToString(@"mm\:ss");

            UpdateTooltip($"FocusGuard — {session.ProfileName} ({timeStr})");
        });
    }

    private void OnTrayDoubleClick(object? sender, EventArgs e)
    {
        RestoreMainWindow();
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        RestoreMainWindow();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        if (_sessionManager.CurrentState != FocusSessionState.Idle
            && _sessionManager.CurrentState != FocusSessionState.Ended)
        {
            ShowBalloonTip("FocusGuard",
                "A focus session is active. End the session before exiting.",
                ToolTipIcon.Warning);
            return;
        }

        App.PerformShutdown();
    }

    private static void RestoreMainWindow()
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow is null) return;

            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.ShowInTaskbar = true;
            mainWindow.Activate();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sessionManager.StateChanged -= OnSessionStateChanged;
        _pomodoroTimer.TimerTick -= OnTimerTick;

        if (_notifyIcon is not null)
        {
            _notifyIcon.DoubleClick -= OnTrayDoubleClick;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _contextMenu?.Dispose();
        _contextMenu = null;
    }
}
