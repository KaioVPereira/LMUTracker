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
    public DbSet<RaceCrewStint> RaceCrewStints => Set<RaceCrewStint>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketImage> TicketImages => Set<TicketImage>();
    public DbSet<TicketUpdate> TicketUpdates => Set<TicketUpdate>();
    public DbSet<AppRelease> AppReleases => Set<AppRelease>();

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
            e.HasIndex(x => x.Track);           // página de pista, filtro de pista, rankings
            e.HasIndex(x => x.EventStartsAt);   // listas por data
            e.Property(x => x.Track).HasMaxLength(96);
            e.Property(x => x.EventId).HasMaxLength(64);
            e.Property(x => x.SeriesId).HasMaxLength(64);
            e.Property(x => x.EventType).HasMaxLength(32);
            e.Property(x => x.Tier).HasMaxLength(32);
            e.Property(x => x.Classes).HasMaxLength(64);
            e.Property(x => x.LiveData).HasMaxLength(Scanning.LiveSnapshot.MaxLength);
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
            e.HasIndex(x => x.CarType);         // página de carro, filtro de carro
            // Índice de cobertura das agregações (início, rankings, pistas): a linha da tabela carrega o volta a volta
            // e o JSON cru comprimidos (~2,7 KB); este índice tem só as colunas que as estatísticas leem, então varrer
            // a semana/o mês lê bem menos páginas e cabe na memória do SQL Express. Também é o índice da FK.
            e.HasIndex(x => x.RaceSplitId).IncludeProperties(x => new
            {
                x.SteamId, x.Name, x.Nationality, x.CarClass, x.CarType, x.ClassPosition, x.ClassGridPos, x.QualiClassPosition,
                x.Laps, x.BestLapTime, x.FinishTime, x.FinishStatus, x.DriverRankAdjustment, x.SafetyRankAdjustment,
                x.Incidents, x.Penalties, x.DriverRankTitle, x.DriverRankTier, x.DriverRankProgress,
            });
            e.Property(x => x.SteamId).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(RaceResult.NameMax);
            e.Property(x => x.CarType).HasMaxLength(RaceResult.CarTypeMax);
            e.Property(x => x.Nationality).HasMaxLength(RaceResult.NationalityMax);
            e.Property(x => x.CarClass).HasMaxLength(RaceResult.CarClassMax);
            e.Property(x => x.FinishStatus).HasMaxLength(RaceResult.FinishStatusMax);
            e.Property(x => x.DriverRankTitle).HasMaxLength(RaceResult.RankTitleMax);
            e.Property(x => x.SafetyRankTitle).HasMaxLength(RaceResult.RankTitleMax);
        });

        b.Entity<RaceCrewStint>(e =>
        {
            e.ToTable("RaceCrewStints");
            e.HasIndex(x => x.SteamId).IncludeProperties(x => x.RaceResultId); // corridas de equipe no perfil
            e.Property(x => x.SteamId).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(RaceResult.NameMax);
            e.HasOne(x => x.RaceResult).WithMany(r => r.Crew)
                .HasForeignKey(x => x.RaceResultId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Follow>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.DriverId }).IsUnique();
        });

        // ---------- App Android (versões liberadas) ----------
        b.Entity<AppRelease>(e =>
        {
            e.HasIndex(r => r.VersionCode).IsUnique();
            e.Property(r => r.Version).HasMaxLength(32);
            e.Property(r => r.FileName).HasMaxLength(128);
            e.Property(r => r.Notes).HasMaxLength(2000);
        });

        // ---------- Chamados (bugs e sugestões) ----------
        b.Entity<Ticket>(e =>
        {
            e.HasIndex(t => new { t.Status, t.CreatedAt });
            e.HasIndex(t => new { t.ReporterId, t.CreatedAt });
            e.HasIndex(t => t.Number).IsUnique();
            // Número amigável ("#42") — IDENTITY própria, independente da PK Guid.
            e.Property(t => t.Number).UseIdentityColumn();
            e.Property(t => t.Message).HasMaxLength(5000);
            e.HasOne(t => t.Reporter).WithMany().HasForeignKey(t => t.ReporterId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TicketImage>(e =>
        {
            e.Property(i => i.Url).HasMaxLength(300);
            e.HasOne(i => i.Ticket).WithMany(t => t.Images).HasForeignKey(i => i.TicketId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TicketUpdate>(e =>
        {
            e.HasIndex(u => new { u.TicketId, u.CreatedAt });
            e.Property(u => u.Note).HasMaxLength(2000);
            e.HasOne(u => u.Ticket).WithMany(t => t.Updates).HasForeignKey(u => u.TicketId).OnDelete(DeleteBehavior.Cascade);
            // Restrict: cascata múltipla para Users (via Ticket.Reporter) o SQL Server recusa.
            e.HasOne(u => u.Admin).WithMany().HasForeignKey(u => u.AdminId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Driver>(e =>
        {
            e.HasIndex(x => x.SteamId).IsUnique();
            e.HasIndex(x => x.Name); // página ao vivo: liga inscritos (só vêm com o nome) aos perfis
            e.Property(x => x.SteamId).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(96);
        });
    }
}
