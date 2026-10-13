using LmuTracker.Application.Lmu;
using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

public sealed class LmuQueryService : ILmuQueryService
{
    private const int RacesPageSize = 25;
    private const int DriverRacesPageSize = 20;
    private const string FinishedNormally = "Finished Normally";

    private readonly AppDbContext _db;

    public LmuQueryService(AppDbContext db)
    {
        _db = db;
    }

    // ---------------------------------------------------------------- painel

    public async Task<OverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var eventsTracked = await _db.ScanEvents.CountAsync(ct);
        var racesFetched = await _db.ScanEvents.CountAsync(e => e.Status == ScanEventStatus.Fetched, ct);
        var resultsStored = await _db.RaceResults.CountAsync(ct);
        var driversIndexed = await _db.Drivers.CountAsync(ct);

        var lastSeen = await _db.ScanEvents.MaxAsync(e => (DateTime?)e.FirstSeenAt, ct);
        var lastFetched = await _db.ScanEvents.MaxAsync(e => e.FetchedAt, ct);
        DateTime? lastActivity = lastSeen > lastFetched || lastFetched is null ? lastSeen : lastFetched;

        // "Largada" exibida = a oficial do evento (igual ao cronograma do jogo); RaceStartsAt é só o início
        // da sessão de corrida e fica de reserva para eventos antigos, gravados antes desse campo existir.
        var upcoming = await _db.ScanEvents
            .Where(e => e.RaceEndsAt > now)
            .OrderBy(e => e.EventStartsAt ?? e.RaceStartsAt)
            .Take(12)
            .Select(e => new UpcomingRaceDto(
                e.EventId, e.Title, e.Track, e.Tier, e.EventType, e.EventStartsAt ?? e.RaceStartsAt, e.RaceEndsAt))
            .ToListAsync(ct);

        var recent = await _db.ScanEvents
            .Where(e => e.Status == ScanEventStatus.Fetched)
            .OrderByDescending(e => e.EventStartsAt ?? e.RaceStartsAt)
            .Take(10)
            .Select(e => new RecentRaceDto(
                e.EventId, e.Title, e.Track, e.Tier, e.EventStartsAt ?? e.RaceStartsAt,
                e.Splits.Count(),
                e.Splits.Sum(s => s.DriverCount)))
            .ToListAsync(ct);

        return new OverviewDto(eventsTracked, racesFetched, resultsStored, driversIndexed,
            lastActivity, upcoming, recent);
    }

    public async Task<MyDriverDto> GetMyDriverAsync(Guid userId, CancellationToken ct = default)
    {
        var steamId = await _db.Users.Where(u => u.Id == userId).Select(u => u.SteamId).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(steamId))
            return new MyDriverDto(false, false, null, null, null, null, null, null, null, null, null, null, 0,
                Array.Empty<MyResultDto>());

        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.SteamId == steamId, ct);
        var mine = _db.RaceResults.Where(r => r.SteamId == steamId);
        var racesFound = await mine.CountAsync(ct);

        var recent = await mine
            .OrderByDescending(r => r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt)
            .Take(15)
            .Select(r => new MyResultDto(
                r.Id, r.RaceSplit.ScanEvent.EventId, r.RaceSplit.ScanEvent.Title, r.RaceSplit.ScanEvent.Track,
                r.RaceSplit.ScanEvent.Tier, r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                r.RaceSplit.SplitNo,
                r.CarClass, r.VehName, r.GridPos, r.ClassGridPos, r.Position, r.ClassPosition, r.Laps,
                r.BestLapTime, r.FinishStatus, r.DriverRankAdjustment, r.SafetyRankAdjustment))
            .ToListAsync(ct);

        return new MyDriverDto(
            Linked: true,
            Found: driver is not null || racesFound > 0,
            SteamId: steamId,
            DriverId: driver?.Id,
            Name: driver?.Name,
            Nationality: driver?.Nationality,
            Badge: driver?.Badge,
            DriverRankTitle: driver?.DriverRankTitle,
            DriverRankTier: driver?.DriverRankTier,
            SafetyRankTitle: driver?.SafetyRankTitle,
            SafetyRankTier: driver?.SafetyRankTier,
            LastSeenAt: driver?.LastSeenAt,
            RacesFound: racesFound,
            Recent: recent);
    }

    // ---------------------------------------------------------------- corridas

    public async Task<PagedDto<RaceListItemDto>> ListRacesAsync(string? tier, int page, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var q = _db.ScanEvents.Where(e => e.Status == ScanEventStatus.Fetched && e.Splits.Any());
        if (tier == "weekly") q = q.Where(e => e.EventType == "weekly");
        else if (!string.IsNullOrWhiteSpace(tier)) q = q.Where(e => e.Tier == tier);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(e => e.EventStartsAt ?? e.RaceStartsAt)
            .Skip((page - 1) * RacesPageSize)
            .Take(RacesPageSize)
            .Select(e => new RaceListItemDto(
                e.EventId, e.Title, e.Track, e.Tier, e.EventType, e.EventStartsAt ?? e.RaceStartsAt,
                e.Status.ToString(), e.Splits.Count(), e.Splits.Sum(s => s.DriverCount)))
            .ToListAsync(ct);

        return new PagedDto<RaceListItemDto>(items, page, RacesPageSize, total);
    }

    public async Task<RaceDetailDto?> GetRaceAsync(string eventId, CancellationToken ct = default)
    {
        var e = await _db.ScanEvents.AsNoTracking().FirstOrDefaultAsync(x => x.EventId == eventId, ct);
        if (e is null) return null;

        var splits = await _db.RaceSplits
            .Where(s => s.ScanEventId == e.Id)
            .OrderBy(s => s.SplitNo)
            .Select(s => new SplitSummaryDto(
                s.SplitNo, s.DriverCount, s.MostLapsCompleted,
                s.Results.Where(r => r.Position == 1).Select(r => r.Name).FirstOrDefault(),
                s.Results.Min(r => r.BestLapTime)))
            .ToListAsync(ct);

        return new RaceDetailDto(e.EventId, e.Title, e.Track, e.Tier, e.EventType,
            e.EventStartsAt ?? e.RaceStartsAt, e.RaceEndsAt, e.Status.ToString(), e.TotalSplits, splits);
    }

    public async Task<SplitResultsDto?> GetSplitAsync(string eventId, int splitNo, CancellationToken ct = default)
    {
        var exists = await _db.RaceSplits.AnyAsync(
            s => s.ScanEvent.EventId == eventId && s.SplitNo == splitNo, ct);
        if (!exists) return null;

        var rows = await RowsQuery(_db.RaceResults.Where(r =>
                r.RaceSplit.ScanEvent.EventId == eventId && r.RaceSplit.SplitNo == splitNo))
            .ToListAsync(ct);

        // Classificados primeiro, na ordem de chegada; quem ficou sem posição vai para o fim.
        var ordered = rows.OrderBy(r => r.Position ?? int.MaxValue).ThenBy(r => r.Name).ToList();
        return new SplitResultsDto(eventId, splitNo, ordered);
    }

    public async Task<ResultDetailDto?> GetResultAsync(Guid resultId, CancellationToken ct = default)
    {
        var info = await _db.RaceResults
            .Where(r => r.Id == resultId)
            .Select(r => new
            {
                r.LapsData,
                r.CarClass,
                r.RaceSplitId,
                r.RaceSplit.SplitNo,
                r.RaceSplit.DriverCount,
                Event = r.RaceSplit.ScanEvent,
            })
            .FirstOrDefaultAsync(ct);
        if (info is null) return null;

        var row = await RowsQuery(_db.RaceResults.Where(r => r.Id == resultId)).FirstAsync(ct);
        var classFastest = await _db.RaceResults
            .Where(r => r.RaceSplitId == info.RaceSplitId && r.CarClass == info.CarClass)
            .MinAsync(r => r.BestLapTime, ct);

        var e = info.Event;
        return new ResultDetailDto(e.EventId, e.Title, e.Track, e.Tier, e.EventType, e.EventStartsAt ?? e.RaceStartsAt,
            info.SplitNo, info.DriverCount, classFastest, row, LapCodec.Decode(info.LapsData));
    }

    // ---------------------------------------------------------------- pilotos

    public async Task<DriverProfileDto?> GetDriverAsync(
        Guid driverId, Guid currentUserId, int page, CancellationToken ct = default)
    {
        var d = await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == driverId, ct);
        if (d is null) return null;
        page = Math.Max(1, page);

        var mine = _db.RaceResults.Where(r => r.SteamId == d.SteamId);

        var stats = await mine
            .GroupBy(_ => 1)
            .Select(g => new DriverStatsDto(
                g.Count(),
                g.Count(r => r.ClassPosition == 1),
                g.Count(r => r.ClassPosition != null && r.ClassPosition <= 3),
                g.Count(r => r.ClassPosition != null && r.ClassPosition <= 5),
                g.Count(r => r.ClassGridPos == 1),
                g.Count(r => r.FinishStatus != null && r.FinishStatus != FinishedNormally),
                g.Min(r => r.ClassPosition),
                g.Sum(r => r.Laps)))
            .FirstOrDefaultAsync(ct) ?? new DriverStatsDto(0, 0, 0, 0, 0, 0, null, 0);

        var total = stats.Races;
        var races = await mine
            .OrderByDescending(r => r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt)
            .Skip((page - 1) * DriverRacesPageSize)
            .Take(DriverRacesPageSize)
            .Select(r => new DriverRaceDto(
                r.Id, r.RaceSplit.ScanEvent.EventId, r.RaceSplit.ScanEvent.Title, r.RaceSplit.ScanEvent.Track,
                r.RaceSplit.ScanEvent.Tier, r.RaceSplit.ScanEvent.EventType,
                r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                r.RaceSplit.SplitNo, r.CarClass, r.VehName, r.ClassGridPos, r.ClassPosition, r.Position,
                r.BestLapTime, r.FinishStatus,
                r.DriverRankTitle, r.DriverRankTier, r.SafetyRankTitle, r.SafetyRankTier,
                r.DriverRankAdjustment, r.SafetyRankAdjustment))
            .ToListAsync(ct);

        var mySteam = await _db.Users.Where(u => u.Id == currentUserId).Select(u => u.SteamId).FirstOrDefaultAsync(ct);

        return new DriverProfileDto(
            d.Id, d.Name, d.Nationality, d.Badge,
            d.DriverRankTitle, d.DriverRankTier, d.DriverRankProgress,
            d.SafetyRankTitle, d.SafetyRankTier, d.SafetyRankProgress,
            d.LastSeenAt, mySteam is not null && mySteam == d.SteamId, stats,
            new PagedDto<DriverRaceDto>(races, page, DriverRacesPageSize, total));
    }

    /// <summary>Linha de classificação com o id interno do piloto (nunca o SteamID).</summary>
    private IQueryable<ResultRowDto> RowsQuery(IQueryable<RaceResult> results) =>
        from r in results
        join drv in _db.Drivers on r.SteamId equals drv.SteamId into dj
        from drv in dj.DefaultIfEmpty()
        select new ResultRowDto(
            r.Id, drv == null ? null : drv.Id, r.Name, r.Nationality, r.Badge,
            r.CarClass, r.CarNumber, r.VehName, r.TeamName,
            r.GridPos, r.ClassGridPos, r.Position, r.ClassPosition, r.Laps,
            r.BestLapTime, r.FinishTime, r.FinishStatus, r.PitStops,
            r.QualiPosition, r.QualiClassPosition, r.QualiBestLapTime,
            r.DriverRankTitle, r.DriverRankTier, r.SafetyRankTitle, r.SafetyRankTier,
            r.DriverRankAdjustment, r.SafetyRankAdjustment, r.LapsData != null);
}
