using System.IO;
using System.Windows;
using FocusGuard.App.Services;
using FocusGuard.App.ViewModels;
using FocusGuard.App.Views;
using FocusGuard.Core;
using FocusGuard.Core.Configuration;
using FocusGuard.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FocusGuard.App;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        }

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        Log.Information("FocusGuard started");
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("FocusGuard shutting down");

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}
