using FocusGuard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FocusGuard.Core.Data;

public class FocusGuardDbContext : DbContext
{
    public FocusGuardDbContext(DbContextOptions<FocusGuardDbContext> options) : base(options) { }

    public DbSet<ProfileEntity> Profiles => Set<ProfileEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProfileEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Color).IsRequired().HasMaxLength(9);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Seed preset profiles
        modelBuilder.Entity<ProfileEntity>().HasData(
            new ProfileEntity
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Name = "Social Media",
                Color = "#E74C3C",
                BlockedWebsites = "[\"facebook.com\",\"twitter.com\",\"x.com\",\"instagram.com\",\"tiktok.com\",\"snapchat.com\",\"linkedin.com\"]",
                BlockedApplications = "[]",
                IsPreset = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ProfileEntity
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Name = "Gaming",
                Color = "#9B59B6",
                BlockedWebsites = "[\"store.steampowered.com\",\"twitch.tv\",\"discord.com\"]",
                BlockedApplications = "[\"steam.exe\",\"discord.exe\",\"epicgameslauncher.exe\"]",
                IsPreset = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ProfileEntity
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Name = "Entertainment",
                Color = "#F39C12",
                BlockedWebsites = "[\"youtube.com\",\"netflix.com\",\"reddit.com\",\"9gag.com\",\"imgur.com\"]",
                BlockedApplications = "[\"spotify.exe\"]",
                IsPreset = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
