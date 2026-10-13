using System.Globalization;
using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.RaceOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Coração do scanner. Em cada passada: (1) descobre as corridas da agenda e guarda o eventId cedo;
/// (2) para as que já terminaram, baixa todos os splits e grava resultados + índice de pilotos.
/// Guardar o eventId na janela da agenda é o que não dá para recuperar depois — por isso roda sempre.
/// </summary>
public sealed class ScannerService
{
    private static readonly string[] Tiers = { "beginner", "intermediate", "advanced" };
    private const int MaxFetchAttempts = 6;
    private const int MaxSplits = 40; // trava de segurança; na prática paramos no primeiro 404

    private readonly AppDbContext _db;
    private readonly RaceOsClient _raceos;
    private readonly ILogger<ScannerService> _log;

    public ScannerService(AppDbContext db, RaceOsClient raceos, ILogger<ScannerService> log)
    {
        _db = db;
        _raceos = raceos;
        _log = log;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await DiscoverAsync(ct);
        await FetchDueAsync(ct);
    }

    // ---------- 1) Descoberta: agenda -> ScanEvents ----------

    public async Task DiscoverAsync(CancellationToken ct)
    {
        // A lista de corridas não traz nome nem pista; a agenda traz, por série. Falhar aqui não pode
        // impedir a descoberta (o que importa é guardar o eventId), então só registra e segue sem título.
        var series = new Dictionary<string, ScheduleSeries>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var schedule = await _raceos.GetScheduleAsync(ct);
            foreach (var s in schedule?.Tiers.Values.SelectMany(v => v) ?? Enumerable.Empty<ScheduleSeries>())
                if (!string.IsNullOrEmpty(s.SeriesId)) series[s.SeriesId] = s;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Scanner: agenda indisponível; corridas novas ficam sem título por enquanto.");
        }

        var found = 0;
        foreach (var tier in Tiers)
        {
            var events = await _raceos.ListTierAsync(tier, take: 8, ct);
            foreach (var e in events)
            {
                if (string.IsNullOrWhiteSpace(e.Id)) continue;
                series.TryGetValue(e.SeriesId ?? "", out var info);

                var existing = await _db.ScanEvents.FirstOrDefaultAsync(x => x.EventId == e.Id, ct);
                if (existing is null)
                {
                    _db.ScanEvents.Add(new ScanEvent
                    {
                        EventId = e.Id,
                        SeriesId = e.SeriesId ?? "",
                        Title = e.Title ?? info?.EventName,
                        Track = info?.Track,
                        Scene = info?.Scene,
                        EventType = e.Type ?? "daily",
                        Tier = tier,
                        RaceStartsAt = (e.RaceStartsAt ?? DateTime.UtcNow).ToUniversalTime(),
                        RaceEndsAt = (e.RaceEndsAt ?? DateTime.UtcNow).ToUniversalTime(),
                        RaceOsStatus = e.Status,
                        Status = ScanEventStatus.Pending,
                    });
                    found++;
                }
                else if (existing.Status == ScanEventStatus.Pending)
                {
                    // Ainda não baixado: mantém horários/estado atualizados enquanto espera terminar.
                    existing.RaceOsStatus = e.Status;
                    if (e.RaceEndsAt is { } ends) existing.RaceEndsAt = ends.ToUniversalTime();
                    existing.Title ??= info?.EventName;
                    existing.Track ??= info?.Track;
                    existing.Scene ??= info?.Scene;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(ct);
        if (found > 0)
            _log.LogInformation("Scanner: {Count} novas corridas na agenda guardadas.", found);
    }

    // ---------- 2) Expansão: baixar resultados das que terminaram ----------

    public async Task FetchDueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Marca como prontas as pendentes que já passaram do fim (com folga de 1 min).
        var toReady = await _db.ScanEvents
            .Where(x => x.Status == ScanEventStatus.Pending && x.RaceEndsAt < now.AddMinutes(-1))
            .ToListAsync(ct);
        foreach (var e in toReady) e.Status = ScanEventStatus.Ready;
        if (toReady.Count > 0) await _db.SaveChangesAsync(ct);

        var due = await _db.ScanEvents
            .Where(x => (x.Status == ScanEventStatus.Ready
                         || (x.Status == ScanEventStatus.Failed && x.FetchAttempts < MaxFetchAttempts))
                        && x.RaceEndsAt < now.AddMinutes(-1))
            .OrderBy(x => x.RaceEndsAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var e in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await FetchEventAsync(e, ct);
            }
            catch (Exception ex)
            {
                e.Status = ScanEventStatus.Failed;
                e.FetchAttempts++;
                e.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                _log.LogWarning(ex, "Scanner: falha ao baixar evento {Event} (tentativa {Attempt}).",
                    e.EventId, e.FetchAttempts);
            }
        }
    }

    private async Task FetchEventAsync(ScanEvent e, CancellationToken ct)
    {
        var overview = await _raceos.GetOverviewAsync(e.EventType, e.EventId, ct);
        var total = overview?.TotalSplits ?? MaxSplits;
        e.TotalSplits = overview?.TotalSplits;

        // Recomeço limpo: apaga splits já gravados deste evento (caso de retry após falha parcial).
        var old = await _db.RaceSplits.Where(s => s.ScanEventId == e.Id).ToListAsync(ct);
        if (old.Count > 0) _db.RaceSplits.RemoveRange(old);

        var splitsSaved = 0;
        for (var n = 1; n <= Math.Min(total, MaxSplits); n++)
        {
            var result = await _raceos.GetResultAsync(e.EventId, n, ct);
            if (result is null) break; // 404 => acabou

            if (e.Title is null) e.Title = result.Title;
            e.Track ??= result.Track;
            e.Scene ??= result.TrackScene;

            var split = new RaceSplit { ScanEventId = e.Id, SplitNo = n };
            var bySteam = result.Drivers
                .Where(d => !string.IsNullOrEmpty(d.SteamId))
                .GroupBy(d => d.SteamId!)
                .ToDictionary(g => g.Key, g => g.First());

            var rows = result.Race?.Races.FirstOrDefault();
            split.MostLapsCompleted = rows?.MostLapsCompleted ?? 0;

            foreach (var r in rows?.Results ?? new())
            {
                bySteam.TryGetValue(r.SteamId ?? "", out var meta);
                split.Results.Add(new RaceResult
                {
                    SteamId = r.SteamId,
                    RaceOsPlayerId = meta?.NkId ?? r.RaceControlId,
                    Name = r.Name ?? meta?.Name ?? "—",
                    Nationality = meta?.Nationality,
                    Badge = meta?.Badge,
                    CarClass = r.CarClass,
                    CarNumber = r.CarNumber,
                    CarType = r.CarType,
                    VehName = r.VehName,
                    TeamName = r.TeamName,
                    GridPos = ParseInt(r.GridPos),
                    Position = ParseInt(r.Position),
                    ClassGridPos = ParseInt(r.ClassGridPos),
                    ClassPosition = ParseInt(r.ClassPosition),
                    Laps = r.Laps,
                    BestLapTime = ParseDouble(r.BestLapTime),
                    FinishTime = ParseDouble(r.FinishTime),
                    FinishStatus = r.FinishStatus,
                    Points = r.Points,
                    DriverRankAdjustment = meta?.DriverRankAdjustment,
                    SafetyRankAdjustment = meta?.SafetyRankAdjustment,
                    JokerPlayed = meta?.JokerPlayed ?? false,
                });
            }

            split.DriverCount = split.Results.Count;
            _db.RaceSplits.Add(split);
            splitsSaved++;

            await UpsertDriversAsync(result.Drivers, ct);
        }

        e.Status = ScanEventStatus.Fetched;
        e.FetchedAt = DateTime.UtcNow;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Scanner: evento {Event} baixado ({Splits} splits).", e.EventId, splitsSaved);
    }

    private async Task UpsertDriversAsync(IEnumerable<ResultDriver> drivers, CancellationToken ct)
    {
        foreach (var d in drivers)
        {
            if (string.IsNullOrEmpty(d.SteamId)) continue;
            var drv = await _db.Drivers.FirstOrDefaultAsync(x => x.SteamId == d.SteamId, ct);
            if (drv is null)
            {
                drv = new Driver { SteamId = d.SteamId! };
                _db.Drivers.Add(drv);
            }
            drv.RaceOsPlayerId = d.NkId ?? d.RaceControlId ?? drv.RaceOsPlayerId;
            drv.Name = d.Name ?? drv.Name ?? "—";
            drv.Nationality = d.Nationality ?? drv.Nationality;
            drv.Badge = d.Badge ?? drv.Badge;
            drv.DriverRankTitle = d.DriverRank?.Rank ?? drv.DriverRankTitle;
            drv.DriverRankTier = d.DriverRank?.Tier ?? drv.DriverRankTier;
            drv.SafetyRankTitle = d.SafetyRank?.Rank ?? drv.SafetyRankTitle;
            drv.SafetyRankTier = d.SafetyRank?.Tier ?? drv.SafetyRankTier;
            drv.LastSeenAt = DateTime.UtcNow;
            drv.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static int? ParseInt(string? s) =>
        int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string? s) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
}
