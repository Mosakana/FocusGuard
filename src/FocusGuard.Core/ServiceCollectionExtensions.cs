using FocusGuard.Core.Blocking;
using FocusGuard.Core.Configuration;
using FocusGuard.Core.Data;
using FocusGuard.Core.Data.Repositories;
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

        // Blocking engines
        services.AddSingleton<IWebsiteBlocker, HostsFileWebsiteBlocker>();
        services.AddSingleton<IApplicationBlocker, ProcessApplicationBlocker>();

        // Security
        services.AddSingleton<PasswordGenerator>();
        services.AddSingleton<PasswordValidator>();
        services.AddSingleton<MasterKeyService>();

        // Sessions
        services.AddSingleton<IFocusSessionManager, FocusSessionManager>();

        return services;
    }
}
