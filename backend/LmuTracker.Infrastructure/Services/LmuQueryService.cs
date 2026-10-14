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
        var racesFetched = await _db.ScanEvents.CountAsync(e => e.Splits.Any(), ct);
        var resultsStored = await _db.RaceResults.CountAsync(ct);
        var driversIndexed = await _db.Drivers.CountAsync(ct);

        var lastSeen = await _db.ScanEvents.MaxAsync(e => (DateTime?)e.FirstSeenAt, ct);
        var lastFetched = await _db.ScanEvents.MaxAsync(e => e.FetchedAt, ct);
        DateTime? lastActivity = lastSeen > lastFetched || lastFetched is null ? lastSeen : lastFetched;

        // "Largada" exibida = a oficial do evento (igual ao cronograma do jogo); RaceStartsAt é só o início
        // da sessão de corrida e fica de reserva para eventos antigos, gravados antes desse campo existir.
        // Sessões, clima e regras vêm da configuração guardada pelo scanner (poucos KB por corrida, lidos aqui).
        var upcoming = (await _db.ScanEvents
                .Where(e => e.RaceEndsAt > now)
                .OrderBy(e => e.EventStartsAt ?? e.RaceStartsAt)
                .Take(12)
                .Select(e => new
                {
                    e.EventId, e.Title, e.Track, e.Tier, e.EventType, Starts = e.EventStartsAt ?? e.RaceStartsAt,
                    e.RaceEndsAt, e.ConfigData,
                })
                .ToListAsync(ct))
            .Select(e => new UpcomingRaceDto(e.EventId, e.Title, e.Track, e.Tier, e.EventType, e.Starts, e.RaceEndsAt,
                EventConfigReader.Read(e.ConfigData)))
            .ToList();

        var recent = await _db.ScanEvents
            .Where(e => e.Splits.Any())
            .OrderByDescending(e => e.EventStartsAt ?? e.RaceStartsAt)
            .Take(10)
            .Select(e => new RecentRaceDto(
                e.EventId, e.Title, e.Track, e.Tier, e.EventStartsAt ?? e.RaceStartsAt,
                e.Splits.Count(),
                e.Splits.Sum(s => s.DriverCount),
                e.Classes))
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

    public async Task<PagedDto<RaceListItemDto>> ListRacesAsync(
        string? tier, string? track, string? car, int page, CancellationToken ct = default,
        string? carClass = null, DateTime? from = null, DateTime? to = null)
    {
        page = Math.Max(1, page);
        // Corrida com resultado gravado aparece mesmo se voltou à fila para ser baixada de novo (reprocessamento):
        // os splits antigos só são trocados quando o novo download termina.
        var q = _db.ScanEvents.Where(e => e.Splits.Any());
        if (tier == "weekly") q = q.Where(e => e.EventType == "weekly");
        else if (!string.IsNullOrWhiteSpace(tier)) q = q.Where(e => e.Tier == tier);
        if (!string.IsNullOrWhiteSpace(track)) q = q.Where(e => e.Track == track);
        if (!string.IsNullOrWhiteSpace(car)) q = q.Where(e => e.Splits.Any(s => s.Results.Any(r => r.CarType == car)));
        // Classe: as variantes do RaceOS (LMP2 / LMP2_ELMS, GT3 / LMGT3) contam como a mesma.
        if (!string.IsNullOrWhiteSpace(carClass))
        {
            var cls = carClass.ToUpperInvariant();
            q = cls switch
            {
                "HYPER" => q.Where(e => e.Classes!.Contains("Hyper")),
                "LMP2" => q.Where(e => e.Classes!.Contains("LMP2")),
                "LMP3" => q.Where(e => e.Classes!.Contains("LMP3")),
                "GT3" => q.Where(e => e.Classes!.Contains("GT3")),
                _ => q.Where(e => e.Classes!.Contains(carClass)),
            };
        }
        if (from is { } f) q = q.Where(e => (e.EventStartsAt ?? e.RaceStartsAt) >= f);
        if (to is { } t) q = q.Where(e => (e.EventStartsAt ?? e.RaceStartsAt) < t);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(e => e.EventStartsAt ?? e.RaceStartsAt)
            .Skip((page - 1) * RacesPageSize)
            .Take(RacesPageSize)
            .Select(e => new RaceListItemDto(
                e.EventId, e.Title, e.Track, e.Tier, e.EventType, e.EventStartsAt ?? e.RaceStartsAt,
                e.Status.ToString(), e.Splits.Count(), e.Splits.Sum(s => s.DriverCount), e.Classes))
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
            e.EventStartsAt ?? e.RaceStartsAt, e.RaceEndsAt, e.Status.ToString(), e.TotalSplits, e.Classes, splits,
            EventConfigReader.Read(e.ConfigData));
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

    /// <summary>
    /// Volta a volta de todo o split: posição geral (do RaceOS) e na classe (a ordem das posições gerais dentro da
    /// classe, entre os carros que completaram aquela volta).
    /// </summary>
    public async Task<SplitLapsDto?> GetSplitLapsAsync(string eventId, int splitNo, CancellationToken ct = default)
    {
        var split = await _db.RaceSplits.AsNoTracking()
            .Where(s => s.ScanEvent.EventId == eventId && s.SplitNo == splitNo)
            .Select(s => new { s.Id, s.MostLapsCompleted })
            .FirstOrDefaultAsync(ct);
        if (split is null) return null;

        var rows = await (
                from r in _db.RaceResults.AsNoTracking()
                where r.RaceSplitId == split.Id
                join drv in _db.Drivers on r.SteamId equals drv.SteamId into dj
                from drv in dj.DefaultIfEmpty()
                select new
                {
                    r.Id, DriverId = drv == null ? (Guid?)null : drv.Id, r.Name, r.CarClass, r.CarNumber, r.CarType,
                    r.GridPos, r.ClassGridPos, r.Position, r.ClassPosition, r.FinishStatus, r.BestLapTime, r.LapsData,
                })
            .ToListAsync(ct);

        var decoded = rows.Select(r => (Row: r, Laps: LapCodec.Decode(r.LapsData))).ToList();

        // Posição na classe por volta: a ordem, dentro da classe, das posições gerais que o RaceOS dá para aquela
        // volta (o tempo acumulado "et" não serve na largada: não marca o fim da 1ª volta igual para todos).
        var classPos = new Dictionary<(Guid, int), int>();
        foreach (var byClass in decoded.GroupBy(d => d.Row.CarClass ?? ""))
        {
            var lapsByNumber = byClass
                .SelectMany(d => d.Laps.Where(l => l.Position is not null).Select(l => (d.Row.Id, l.Lap, Value: (double)l.Position!.Value)))
                .GroupBy(x => x.Lap);
            foreach (var lap in lapsByNumber)
            {
                var k = 1;
                foreach (var x in lap.OrderBy(x => x.Value)) classPos[(x.Id, lap.Key)] = k++;
            }
        }

        var drivers = decoded
            .OrderBy(d => d.Row.Position ?? int.MaxValue).ThenBy(d => d.Row.Name)
            .Select(d => new SplitDriverLapsDto(
                d.Row.Id, d.Row.DriverId, d.Row.Name, d.Row.CarClass, d.Row.CarNumber, d.Row.CarType,
                d.Row.GridPos, d.Row.ClassGridPos, d.Row.Position, d.Row.ClassPosition, d.Row.FinishStatus, d.Row.BestLapTime,
                d.Laps.Select(l => new SplitLapDto(l.Lap, l.Position,
                    classPos.TryGetValue((d.Row.Id, l.Lap), out var cp) ? cp : null,
                    l.LapTime, l.Elapsed, l.Pit, l.RearCompound ?? l.FrontCompound, l.Fuel)).ToList()))
            .ToList();
        return new SplitLapsDto(eventId, splitNo, split.MostLapsCompleted, drivers);
    }

    /// <summary>Penalidades e decisões dos comissários do split, do resultado cru (só corridas a partir do scanner v3).</summary>
    public async Task<SplitEventsDto?> GetSplitEventsAsync(string eventId, int splitNo, CancellationToken ct = default)
    {
        var split = await _db.RaceSplits.AsNoTracking()
            .Where(s => s.ScanEvent.EventId == eventId && s.SplitNo == splitNo)
            .Select(s => new { s.ExtraData })
            .FirstOrDefaultAsync(ct);
        if (split is null) return null;
        var events = SplitEventsReader.Read(split.ExtraData);
        return new SplitEventsDto(eventId, splitNo, events is not null, events ?? []);
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
        var followed = currentUserId != Guid.Empty
                       && await _db.Follows.AnyAsync(f => f.UserId == currentUserId && f.DriverId == d.Id, ct);

        return new DriverProfileDto(
            d.Id, d.Name, d.Nationality, d.Badge,
            d.DriverRankTitle, d.DriverRankTier, d.DriverRankProgress,
            d.SafetyRankTitle, d.SafetyRankTier, d.SafetyRankProgress,
            d.LastSeenAt, mySteam is not null && mySteam == d.SteamId, stats,
            new PagedDto<DriverRaceDto>(races, page, DriverRacesPageSize, total), followed);
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
            r.DriverRankAdjustment, r.SafetyRankAdjustment, r.LapsData != null, r.Incidents, r.Penalties);
}
