using System.IO;
using System.Threading;
using System.Windows;
using FocusGuard.App.Services;
using FocusGuard.App.ViewModels;
using FocusGuard.App.Views;
using FocusGuard.Core;
using FocusGuard.Core.Blocking;
using FocusGuard.Core.Configuration;
using FocusGuard.Core.Data;
using FocusGuard.Core.Hardening;
using FocusGuard.Core.Recovery;
using FocusGuard.Core.Scheduling;
using FocusGuard.Core.Sessions;
using FocusGuard.Core.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FocusGuard.App;

public partial class App : Application
{
    private IHost? _host;
    private static Mutex? _singleInstanceMutex;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Install global exception handlers FIRST
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        // Enforce single instance
        _singleInstanceMutex = new Mutex(true, "FocusGuard_SingleInstance", out var isNewInstance);
        if (!isNewInstance)
        {
            Log.Warning("Another instance of FocusGuard is already running");
            Shutdown();
            return;
        }

        // Ensure app directories exist
        Directory.CreateDirectory(AppPaths.DataDirectory);
        Directory.CreateDirectory(AppPaths.LogDirectory);

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(AppPaths.LogDirectory, "focusguard-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                // Core services
                services.AddFocusGuardCore();

                // App services
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<BlockingOrchestrator>();
                services.AddSingleton<ITrayIconService, TrayIconService>();
                services.AddSingleton<IOverlayService, OverlayService>();
                services.AddSingleton<INotificationService, NotificationService>();

                // ViewModels
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProfilesViewModel>();
                services.AddTransient<ProfileEditorViewModel>();
                services.AddTransient<CalendarViewModel>();
                services.AddTransient<StatisticsViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<MainWindowViewModel>();

                // Windows
                services.AddSingleton<MainWindow>();
            })
            .Build();

        Services = _host.Services;

        await _host.StartAsync();

        // Ensure database is created and seeded
        using (var scope = Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<FocusGuardDbContext>>()
                .CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();

            // Run Phase 2+ migrations (CREATE TABLE IF NOT EXISTS)
            var migrator = Services.GetRequiredService<FocusGuard.Core.Data.DatabaseMigrator>();
            await migrator.MigrateAsync();
        }

        // Crash recovery / session recovery
        var isRecovery = e.Args.Contains("--recovered");
        var isMinimized = e.Args.Contains("--minimized");

        if (isRecovery)
        {
            Log.Information("App started in recovery mode (--recovered)");
            var sessionRecovery = Services.GetRequiredService<ISessionRecoveryService>();
            var recovered = await sessionRecovery.TryRecoverSessionAsync();

            if (recovered)
            {
                Log.Information("Session recovered successfully after crash");
                var strictMode = Services.GetRequiredService<IStrictModeService>();
                if (await strictMode.IsEnabledAsync())
                {
                    var sessionManager = Services.GetRequiredService<IFocusSessionManager>();
                    var session = sessionManager.CurrentSession;
                    if (session is not null)
                    {
                        var heartbeat = Services.GetRequiredService<IHeartbeatService>();
                        heartbeat.Start(session.SessionId, session.ProfileId);
                        var watchdog = Services.GetRequiredService<IWatchdogLauncher>();
                        watchdog.Launch();
                    }
                }
            }
            else
            {
                // Session expired or not found — run normal cleanup
                var crashRecovery = Services.GetRequiredService<ICrashRecoveryService>();
                await crashRecovery.RecoverAsync();
            }
        }
        else
        {
            // Normal startup: clean up orphans and stale hosts entries
            var crashRecovery = Services.GetRequiredService<ICrashRecoveryService>();
            await crashRecovery.RecoverAsync();
        }

        // Resolve MainWindow early so WPF knows the real main window
        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;

        // Master key setup on first launch (shown before main window)
        var masterKeyService = Services.GetRequiredService<FocusGuard.Core.Security.MasterKeyService>();
        if (!await masterKeyService.IsSetupCompleteAsync())
        {
            var setupVm = new ViewModels.MasterKeySetupViewModel(masterKeyService);
            await setupVm.GenerateKeyAsync();
            var setupDialog = new Views.MasterKeySetupDialog { DataContext = setupVm };
            setupDialog.ShowDialog();
        }

        // Start scheduling engine
        var schedulingEngine = Services.GetRequiredService<ISchedulingEngine>();
        await schedulingEngine.StartAsync();

        // Resolve BlockedAttemptLogger singleton so it subscribes to events
        _ = Services.GetRequiredService<BlockedAttemptLogger>();

        // Initialize system tray, overlay, and notifications
        Services.GetRequiredService<ITrayIconService>().Initialize();
        Services.GetRequiredService<IOverlayService>().Initialize();
        Services.GetRequiredService<INotificationService>().Initialize();

        if (isMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowInTaskbar = false;
            mainWindow.Show();
            mainWindow.Hide();
        }
        else
        {
            mainWindow.Show();
        }

        Log.Information("FocusGuard started");
    }

    /// <summary>
    /// Performs full cleanup and terminates the process.
    /// Called directly from MainWindow close and tray Exit — does NOT rely on
    /// WPF's Shutdown()/OnExit() chain which can fail with OnExplicitShutdown.
    /// </summary>
    public static void PerformShutdown()
    {
        Log.Information("FocusGuard shutting down");

        // 1. UI-thread-only work (tray icon must be disposed on its creator thread,
        //    mutex must be released on the thread that acquired it)
        try { (Services?.GetService<ITrayIconService>() as IDisposable)?.Dispose(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        try { _singleInstanceMutex?.Dispose(); } catch { }

        // 2. Fire off background cleanup (hosts file, blocker, host stop)
        //    Don't block the UI thread — Environment.Exit kills everything anyway
        Task.Run(() =>
        {
            try
            {
                Services?.GetService<IWebsiteBlocker>()?.RemoveBlocklistAsync().GetAwaiter().GetResult();
                var appBlocker = Services?.GetService<IApplicationBlocker>();
                if (appBlocker?.IsActive == true) appBlocker.StopBlocking();
                Services?.GetService<IHeartbeatService>()?.Stop();
            }
            catch { }

            try { (Services?.GetService<IOverlayService>() as IDisposable)?.Dispose(); } catch { }
            try { (Services?.GetService<INotificationService>() as IDisposable)?.Dispose(); } catch { }
            try { ToastNotificationManagerCompat.Uninstall(); } catch { }

            try
            {
                (Current as App)?._host?.StopAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            }
            catch { }

            Log.CloseAndFlush();
        });

        // 3. Give background cleanup a brief moment for the critical hosts file restore,
        //    then terminate. OS cleans up the rest.
        Thread.Sleep(500);
        Environment.Exit(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Safety net — PerformShutdown() should have already run
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled dispatcher exception");
        EmergencyCleanup();
        e.Handled = false; // Let the app crash after cleanup
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating})", e.IsTerminating);
        EmergencyCleanup();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved(); // Prevent crash for task exceptions — log and continue
    }

    private void EmergencyCleanup()
    {
        try
        {
            var websiteBlocker = Services?.GetService<IWebsiteBlocker>();
            websiteBlocker?.RemoveBlocklistAsync().GetAwaiter().GetResult();
            var appBlocker = Services?.GetService<IApplicationBlocker>();
            if (appBlocker?.IsActive == true) appBlocker.StopBlocking();

            // Stop heartbeat so watchdog exits cleanly
            var heartbeat = Services?.GetService<IHeartbeatService>();
            heartbeat?.Stop();

            // Dispose tray icon to prevent orphaned icon in system tray
            (Services?.GetService<ITrayIconService>() as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Emergency cleanup failed");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
