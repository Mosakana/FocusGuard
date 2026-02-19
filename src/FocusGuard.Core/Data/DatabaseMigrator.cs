using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Core.Data;

public class DatabaseMigrator
{
    private readonly IDbContextFactory<FocusGuardDbContext> _contextFactory;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(IDbContextFactory<FocusGuardDbContext> contextFactory, ILogger<DatabaseMigrator> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task MigrateAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Phase 1 tables already exist via EnsureCreated()
        // Phase 2: Add new tables if they don't exist
        _logger.LogInformation("Running database migrations...");

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS FocusSessions (
                Id TEXT PRIMARY KEY,
                ProfileId TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT,
                PlannedDurationMinutes INTEGER NOT NULL DEFAULT 0,
                ActualDurationMinutes INTEGER NOT NULL DEFAULT 0,
                PomodoroCompletedCount INTEGER NOT NULL DEFAULT 0,
                WasUnlockedEarly INTEGER NOT NULL DEFAULT 0,
                State TEXT NOT NULL DEFAULT 'Ended',
                CreatedAt TEXT NOT NULL
            );
        ");

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL DEFAULT ''
            );
        ");

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE INDEX IF NOT EXISTS IX_FocusSessions_State
            ON FocusSessions (State);
        ");

        _logger.LogInformation("Database migrations completed");
    }
}
