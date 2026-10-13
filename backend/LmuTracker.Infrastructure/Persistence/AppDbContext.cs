using LmuTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ScanEvent> ScanEvents => Set<ScanEvent>();
    public DbSet<RaceSplit> RaceSplits => Set<RaceSplit>();
    public DbSet<RaceResult> RaceResults => Set<RaceResult>();
    public DbSet<Driver> Drivers => Set<Driver>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        b.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.SteamId); // não único por si só; a unicidade do vínculo é tratada na aplicação
            e.Property(u => u.Username).HasMaxLength(32);
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.DisplayName).HasMaxLength(64);
            e.Property(u => u.SteamId).HasMaxLength(20);
        });

        b.Entity<ScanEvent>(e =>
        {
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.RaceEndsAt);
            e.Property(x => x.EventId).HasMaxLength(64);
            e.Property(x => x.SeriesId).HasMaxLength(64);
            e.Property(x => x.EventType).HasMaxLength(32);
            e.Property(x => x.Tier).HasMaxLength(32);
            e.HasMany(x => x.Splits).WithOne(s => s.ScanEvent)
                .HasForeignKey(s => s.ScanEventId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RaceSplit>(e =>
        {
            e.HasIndex(x => new { x.ScanEventId, x.SplitNo }).IsUnique();
            e.HasMany(x => x.Results).WithOne(r => r.RaceSplit)
                .HasForeignKey(r => r.RaceSplitId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RaceResult>(e =>
        {
            e.HasIndex(x => x.SteamId);
            e.Property(x => x.SteamId).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(96);
        });

        b.Entity<Driver>(e =>
        {
            e.HasIndex(x => x.SteamId).IsUnique();
            e.Property(x => x.SteamId).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(96);
        });
    }
}
