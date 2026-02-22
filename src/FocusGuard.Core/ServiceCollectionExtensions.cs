using FocusGuard.Core.Blocking;
using FocusGuard.Core.Configuration;
using FocusGuard.Core.Data;
using FocusGuard.Core.Data.Repositories;
using FocusGuard.Core.Hardening;
using FocusGuard.Core.Recovery;
using FocusGuard.Core.Scheduling;
using FocusGuard.Core.Security;
using FocusGuard.Core.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FocusGuard.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFocusGuardCore(this IServiceCollection services)
    {
        // Database
        services.AddDbContextFactory<FocusGuardDbContext>(options =>
            options.UseSqlite($"Data Source={AppPaths.DatabasePath}"));

        // Migration
        services.AddSingleton<DatabaseMigrator>();

        // Repositories
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IFocusSessionRepository, FocusSessionRepository>();
        services.AddSingleton<IScheduledSessionRepository, ScheduledSessionRepository>();

        // Blocking engines
        services.AddSingleton<IWebsiteBlocker, HostsFileWebsiteBlocker>();
        services.AddSingleton<IApplicationBlocker, ProcessApplicationBlocker>();

        // Security
        services.AddSingleton<PasswordGenerator>();
        services.AddSingleton<PasswordValidator>();
        services.AddSingleton<MasterKeyService>();

        // Sessions
        services.AddSingleton<IFocusSessionManager, FocusSessionManager>();
        services.AddSingleton<PomodoroIntervalCalculator>();
        services.AddSingleton<PomodoroTimer>();
        services.AddSingleton<SoundAlertService>();

        // Scheduling
        services.AddSingleton<OccurrenceExpander>();
        services.AddSingleton<ISchedulingEngine, SchedulingEngine>();

        // Recovery
        services.AddSingleton<ICrashRecoveryService, CrashRecoveryService>();

        // Hardening
        services.AddSingleton<IStrictModeService, StrictModeService>();
        services.AddSingleton<IHeartbeatService, HeartbeatService>();
        services.AddSingleton<IWatchdogLauncher, WatchdogLauncher>();
        services.AddSingleton<IAutoStartService, AutoStartService>();
        services.AddSingleton<ISessionRecoveryService, SessionRecoveryService>();

        return services;
    }
}
