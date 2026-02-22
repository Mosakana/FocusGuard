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
using Microsoft.Extensions.DependencyInjection;
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

                // ViewModels
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProfilesViewModel>();
                services.AddTransient<ProfileEditorViewModel>();
                services.AddTransient<CalendarViewModel>();
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

        // Master key setup on first launch
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

        var mainWindow = Services.GetRequiredService<MainWindow>();

        if (isMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowInTaskbar = false;
        }

        mainWindow.Show();

        Log.Information("FocusGuard started");
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("FocusGuard shutting down");

        EmergencyCleanup();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        await Log.CloseAndFlushAsync();
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
