using FocusGuard.Core.Blocking;
using FocusGuard.Core.Configuration;
using FocusGuard.Core.Data;
using FocusGuard.Core.Data.Repositories;
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

        // Repositories
        services.AddScoped<IProfileRepository, ProfileRepository>();

        // Blocking engines
        services.AddSingleton<IWebsiteBlocker, HostsFileWebsiteBlocker>();
        services.AddSingleton<IApplicationBlocker, ProcessApplicationBlocker>();

        return services;
    }
}
