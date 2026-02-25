using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Install WM_GETMINMAXINFO hook to prevent maximized window from covering taskbar
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // Toggle maximize/restore icon
        UpdateMaximizeIcon();

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

    private void UpdateMaximizeIcon()
    {
        if (MaximizeIcon is null) return;

        if (WindowState == WindowState.Maximized)
        {
            // Restore icon: two overlapping rectangles
            MaximizeIcon.Data = Geometry.Parse("M 2,0 L 10,0 L 10,8 L 8,8 L 8,10 L 0,10 L 0,2 L 2,2 Z M 2,2 L 8,2 L 8,8 L 2,8 Z");
        }
        else
        {
            // Maximize icon: single rectangle
            MaximizeIcon.Data = Geometry.Parse("M 0,0 L 10,0 L 10,10 L 0,10 Z");
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

    #region WM_GETMINMAXINFO — Prevent maximized window from covering taskbar

    private const int WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var work = monitorInfo.rcWork;
                    var mon = monitorInfo.rcMonitor;
                    mmi.ptMaxPosition.X = work.Left - mon.Left;
                    mmi.ptMaxPosition.Y = work.Top - mon.Top;
                    mmi.ptMaxSize.X = work.Right - work.Left;
                    mmi.ptMaxSize.Y = work.Bottom - work.Top;
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    #endregion
}
