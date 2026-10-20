using LmuTracker.Application.Lmu;
using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Rankings (melhor volta por piloto), páginas de pista e de carro, e pilotos seguidos. As agregações públicas
/// ficam em cache curto (são iguais para todo mundo); "seguindo" é por usuário e não tem cache.
/// </summary>
public sealed class ExtrasService : IExtrasService
{
    private const string FinishedNormally = "Finished Normally";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LeaderboardFor = TimeSpan.FromSeconds(60);
    private const int MaxTake = 100;

    private readonly AppDbContext _db;
    private readonly SnapshotCache _cache;

    public ExtrasService(AppDbContext db, SnapshotCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>Variantes do RaceOS contam como a mesma classe (LMP2 / LMP2_ELMS, GT3 / LMGT3).</summary>
    internal static IQueryable<RaceResult> OfClass(IQueryable<RaceResult> q, string? key) => (key ?? "").ToLowerInvariant() switch
    {
        "" => q,
        "hyper" => q.Where(r => r.CarClass!.StartsWith("Hyper")),
        "lmp2" => q.Where(r => r.CarClass!.StartsWith("LMP2")),
        "lmp3" => q.Where(r => r.CarClass!.StartsWith("LMP3")),
        "gt3" => q.Where(r => r.CarClass!.Contains("GT3")),
        var other => q.Where(r => r.CarClass == other),
    };

    private static IQueryable<RaceResult> OfSetup(IQueryable<RaceResult> q, string? setup) => setup switch
    {
        "fixed" => q.Where(r => (r.RaceSplit.ScanEvent.Title ?? "").Contains("Fixed")),
        "open" => q.Where(r => !(r.RaceSplit.ScanEvent.Title ?? "").Contains("Fixed")),
        _ => q,
    };

    private static async Task<Dictionary<string, Guid>> DriverIdsAsync(AppDbContext db, IEnumerable<string?> steamIds,
        CancellationToken ct)
    {
        var list = steamIds.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Drivers.AsNoTracking().Where(d => list.Contains(d.SteamId))
            .ToDictionaryAsync(d => d.SteamId, d => d.Id, ct);
    }

    // ---------------------------------------------------------------- rankings

    private sealed record LapEntry(string SteamId, double Lap);

    /// <summary>Resultados do recorte. Carro dividido fica de fora: a volta dele vem dos turnos (ver CrewLapsAsync).</summary>
    private static IQueryable<RaceResult> LapsOf(AppDbContext db, string? track, string? carClass, string? car, string? setup,
        string? country)
    {
        var q = Cut(db, track, carClass, car, setup).Where(r => r.BestLapTime != null && r.SteamId != null && !r.Crew.Any());
        if (country is not null) q = q.Where(r => r.Nationality == country);
        return q;
    }

    private static IQueryable<RaceResult> Cut(AppDbContext db, string? track, string? carClass, string? car, string? setup)
    {
        var q = db.RaceResults.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(track)) q = q.Where(r => r.RaceSplit.ScanEvent.Track == track);
        if (!string.IsNullOrWhiteSpace(car)) q = q.Where(r => r.CarType == car);
        return OfSetup(OfClass(q, carClass), setup);
    }

    /// <summary>A melhor volta de cada piloto em cada carro dividido do recorte (a dos turnos dele), com o país.</summary>
    private static async Task<List<(CrewCredit.Credit C, string? Nationality)>> CrewLapsAsync(AppDbContext db, string? track,
        string? carClass, string? car, string? setup, string? country, CancellationToken ct)
    {
        var credits = (await CrewCredit.OfAsync(db, Cut(db, track, carClass, car, setup).Where(r => r.Crew.Any()), true, ct))
            .Where(c => c.BestLap is not null).ToList();
        var steam = credits.Select(c => c.SteamId).Distinct().ToList();
        var nat = await db.Drivers.AsNoTracking().Where(d => steam.Contains(d.SteamId))
            .ToDictionaryAsync(d => d.SteamId, d => d.Nationality, ct);
        return credits.Select(c => (c, nat.GetValueOrDefault(c.SteamId)))
            .Where(x => country is null || x.Item2 == country).ToList();
    }

    public async Task<LeaderboardDto> LeaderboardAsync(string? track, string? carClass, string? car, string? setup, string? country,
        int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        skip = Math.Max(skip, 0);
        setup = setup is "fixed" or "open" ? setup : null;
        country = RankingsService.NormCountry(country);
        var filters = $"{track}|{carClass}|{car}|{setup}|{country}";

        // 1) A melhor volta de cada piloto no recorte, já ordenada (GroupBy + Min traduz para SQL) — a lista inteira
        // fica em cache, então trocar de página não volta a agregar; 2) os detalhes da corrida em que cada piloto
        // da página fez essa volta.
        // Carros divididos entram pelos turnos: cada piloto com a melhor volta que ele mesmo fez.
        var crew = await _cache.GetAsync($"lb:crew:{filters}", LeaderboardFor, (db, ct) =>
            CrewLapsAsync(db, track, carClass, car, setup, country, ct), ct);
        var order = await _cache.GetAsync($"lb:order:{filters}", LeaderboardFor, async (db, ct) =>
            (await LapsOf(db, track, carClass, car, setup, country)
                .GroupBy(r => r.SteamId!)
                .Select(g => new { SteamId = g.Key, Lap = g.Min(r => r.BestLapTime!.Value) })
                .ToListAsync(ct))
            .Select(x => new LapEntry(x.SteamId, x.Lap))
            .Concat(crew.Select(x => new LapEntry(x.C.SteamId, x.C.BestLap!.Value)))
            .GroupBy(x => x.SteamId)
            .Select(g => new LapEntry(g.Key, g.Min(x => x.Lap)))
            .OrderBy(x => x.Lap).ThenBy(x => x.SteamId, StringComparer.Ordinal)
            .ToList(), ct);

        return await _cache.GetAsync($"lb:{filters}|{skip}|{take}", LeaderboardFor, async (db, ct) =>
        {
            var page = order.Skip(skip).Take(take).ToList();
            var pageIds = page.Select(t => t.SteamId).ToList();
            var detail = (await LapsOf(db, track, carClass, car, setup, country)
                    .Where(r => pageIds.Contains(r.SteamId!))
                    .Select(r => new
                    {
                        r.SteamId, r.Name, r.Nationality, r.CarType, r.CarClass, Lap = r.BestLapTime!.Value,
                        r.RaceSplit.ScanEvent.EventId, r.RaceSplit.SplitNo,
                        At = r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                    })
                    .ToListAsync(ct))
                .Concat(crew.Where(x => pageIds.Contains(x.C.SteamId)).Select(x => new
                {
                    SteamId = (string?)x.C.SteamId, Name = x.C.Name, Nationality = x.Nationality, CarType = x.C.CarType,
                    CarClass = x.C.CarClass, Lap = x.C.BestLap!.Value, EventId = x.C.EventId, SplitNo = x.C.SplitNo, At = x.C.At,
                }))
                .GroupBy(x => x.SteamId!)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Lap).ThenBy(x => x.At).First());
            var ids = await DriverIdsAsync(db, pageIds, ct);
            var first = order.FirstOrDefault()?.Lap;
            var rows = page.Select((t, i) => (t.SteamId, Rank: skip + i + 1))
                .Where(x => detail.ContainsKey(x.SteamId))
                .Select(x =>
                {
                    var b = detail[x.SteamId];
                    return new LeaderboardRowDto(x.Rank, ids.TryGetValue(x.SteamId, out var id) ? id : null,
                        b.Name, b.Nationality, b.CarType, b.CarClass, b.Lap,
                        first is null || x.Rank == 1 ? null : Math.Round(b.Lap - first.Value, 3), b.EventId, b.At, b.SplitNo);
                })
                .ToList();
            return new LeaderboardDto(track, carClass, car, setup ?? "all", country, order.Count, rows);
        }, ct);
    }

    // ---------------------------------------------------------------- pista

    public async Task<TrackDetailDto?> TrackAsync(string track, CancellationToken ct = default) =>
        await _cache.GetAsync<TrackDetailDto?>($"track:{track}", CacheFor, async (db, ct) =>
        {
            var results = db.RaceResults.AsNoTracking().Where(r => r.RaceSplit.ScanEvent.Track == track);
            var starts = await results.CountAsync(ct);
            if (starts == 0) return null;

            var events = db.ScanEvents.AsNoTracking().Where(e => e.Track == track && e.Splits.Any());
            var races = await events.CountAsync(ct);
            var first = await events.MinAsync(e => (DateTime?)(e.EventStartsAt ?? e.RaceStartsAt), ct);
            var last = await events.MaxAsync(e => (DateTime?)(e.EventStartsAt ?? e.RaceStartsAt), ct);
            var drivers = await results.Where(r => r.SteamId != null).Select(r => r.SteamId).Distinct().CountAsync(ct);

            var records = await results
                .Where(r => r.BestLapTime != null)
                .GroupBy(r => new { r.CarClass, Fixed = (r.RaceSplit.ScanEvent.Title ?? "").Contains("Fixed") })
                .Select(g => g.OrderBy(r => r.BestLapTime).Select(r => new
                {
                    g.Key.CarClass, g.Key.Fixed, Lap = r.BestLapTime!.Value, r.Id, r.SteamId, r.Name, r.CarType,
                    At = r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt, r.RaceSplit.ScanEvent.EventId,
                }).First())
                .ToListAsync(ct);

            // Ritmo por carro: a melhor volta de cada piloto com aquele carro; média das 10 melhores.
            var perDriver = await results
                .Where(r => r.BestLapTime != null && r.SteamId != null && r.CarType != null)
                .GroupBy(r => new { r.CarType, r.CarClass, r.SteamId })
                .Select(g => new { g.Key.CarType, g.Key.CarClass, Best = g.Min(r => r.BestLapTime!.Value) })
                .ToListAsync(ct);
            var carStarts = await results.Where(r => r.CarType != null)
                .GroupBy(r => new { r.CarType, r.CarClass })
                .Select(g => new { g.Key.CarType, g.Key.CarClass, N = g.Count() })
                .ToListAsync(ct);
            var cars = carStarts.Select(c =>
                {
                    var laps = perDriver.Where(p => p.CarType == c.CarType && p.CarClass == c.CarClass).Select(p => p.Best).OrderBy(x => x).ToList();
                    return new CarPaceDto(c.CarType!, c.CarClass, c.N, laps.Count > 0 ? laps[0] : null,
                        laps.Count > 0 ? Math.Round(laps.Take(10).Average(), 3) : null);
                })
                .OrderBy(c => c.CarClass).ThenBy(c => c.Top10Avg ?? double.MaxValue)
                .ToList();

            var classes = (await results.Where(r => r.CarClass != null)
                    .GroupBy(r => r.CarClass!).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct))
                .OrderByDescending(x => x.N).Select(x => new ClassShareDto(x.Key, x.N)).ToList();

            // Carro dividido: o recorde é de quem fez a volta, não de quem terminou o carro.
            var owners = await CrewCredit.LapOwnersAsync(db, records.Select(r => r.Id), ct);
            records = records.Select(r => owners.TryGetValue(r.Id, out var o) ? r with { SteamId = o.SteamId, Name = o.Name } : r).ToList();
            var ids = await DriverIdsAsync(db, records.Select(r => r.SteamId), ct);
            return new TrackDetailDto(track, races, starts, drivers, first, last,
                records.OrderBy(r => r.CarClass).ThenBy(r => r.Fixed)
                    .Select(r => new TrackRecordDto(r.CarClass, r.Fixed, r.Lap,
                        r.SteamId is not null && ids.TryGetValue(r.SteamId, out var id) ? id : null, r.Name, r.CarType, r.At, r.EventId))
                    .ToList(),
                cars, classes);
        }, ct);

    // ---------------------------------------------------------------- carro

    public async Task<CarDetailDto?> CarAsync(string car, CancellationToken ct = default) =>
        await _cache.GetAsync<CarDetailDto?>($"car:{car}", CacheFor, async (db, ct) =>
        {
            var results = db.RaceResults.AsNoTracking().Where(r => r.CarType == car);
            var stats = await results.GroupBy(_ => 1).Select(g => new
            {
                Starts = g.Count(),
                Wins = g.Count(r => r.ClassPosition == 1),
                Podiums = g.Count(r => r.ClassPosition >= 1 && r.ClassPosition <= 3),
                Dnfs = g.Count(r => r.FinishStatus != null && r.FinishStatus != FinishedNormally && r.FinishStatus != "None"),
                Avg = g.Where(r => r.FinishStatus == FinishedNormally).Average(r => (double?)r.ClassPosition),
                Class = g.Max(r => r.CarClass),
            }).FirstOrDefaultAsync(ct);
            if (stats is null) return null;
            var drivers = await results.Where(r => r.SteamId != null).Select(r => r.SteamId).Distinct().CountAsync(ct);

            var tracks = await results.Where(r => r.RaceSplit.ScanEvent.Track != null)
                .GroupBy(r => r.RaceSplit.ScanEvent.Track!)
                .Select(g => new
                {
                    Track = g.Key,
                    Starts = g.Count(),
                    Wins = g.Count(r => r.ClassPosition == 1),
                    Best = g.Where(r => r.BestLapTime != null).OrderBy(r => r.BestLapTime)
                        .Select(r => new { Lap = r.BestLapTime, r.Id, r.SteamId, r.Name }).FirstOrDefault(),
                })
                .ToListAsync(ct);

            var perDriver = await results.Where(r => r.SteamId != null)
                .GroupBy(r => r.SteamId!)
                .Select(g => new
                {
                    SteamId = g.Key, Name = g.Max(r => r.Name), Nat = g.Max(r => r.Nationality), Starts = g.Count(),
                    Wins = g.Count(r => r.ClassPosition == 1), Podiums = g.Count(r => r.ClassPosition >= 1 && r.ClassPosition <= 3),
                })
                .ToListAsync(ct);
            // Carros divididos: o resultado conta também para quem guiou um turno sem ter terminado.
            var credits = await CrewCredit.OfAsync(db, results, includeOwner: false, ct);
            var top = perDriver
                .Concat(credits.Select(c => new
                {
                    SteamId = c.SteamId, Name = (string?)c.Name, Nat = (string?)null, Starts = 1,
                    Wins = c.ClassPosition == 1 ? 1 : 0, Podiums = c.ClassPosition is >= 1 and <= 3 ? 1 : 0,
                }))
                .GroupBy(x => x.SteamId)
                .Select(g => new
                {
                    SteamId = g.Key, Name = g.Max(x => x.Name), Nat = g.Max(x => x.Nat), Starts = g.Sum(x => x.Starts),
                    Wins = g.Sum(x => x.Wins), Podiums = g.Sum(x => x.Podiums),
                })
                .OrderByDescending(x => x.Wins).ThenByDescending(x => x.Podiums).ThenByDescending(x => x.Starts)
                .Take(10)
                .ToList();

            var owners = await CrewCredit.LapOwnersAsync(db, tracks.Where(t => t.Best is not null).Select(t => t.Best!.Id), ct);
            tracks = tracks.Select(t => t.Best is not null && owners.TryGetValue(t.Best.Id, out var o)
                ? t with { Best = t.Best with { SteamId = o.SteamId, Name = o.Name } } : t).ToList();
            var ids = await DriverIdsAsync(db, tracks.Select(t => t.Best?.SteamId).Concat(top.Select(t => t.SteamId)), ct);
            Guid? Id(string? s) => s is not null && ids.TryGetValue(s, out var id) ? id : null;
            return new CarDetailDto(car, stats.Class, stats.Starts, drivers, stats.Wins, stats.Podiums, stats.Dnfs,
                stats.Avg is null ? null : Math.Round(stats.Avg.Value, 2),
                tracks.OrderByDescending(t => t.Starts)
                    .Select(t => new CarTrackDto(t.Track, t.Starts, t.Wins, t.Best?.Lap, Id(t.Best?.SteamId), t.Best?.Name)).ToList(),
                top.Select(t => new CarDriverDto(Id(t.SteamId), t.Name ?? "—", t.Nat, t.Starts, t.Wins, t.Podiums)).ToList());
        }, ct);

    public async Task<IReadOnlyList<CarListItemDto>> CarsAsync(CancellationToken ct = default) =>
        await _cache.GetAsync<IReadOnlyList<CarListItemDto>>("cars", TimeSpan.FromMinutes(10), async (db, ct) =>
        {
            return (await db.RaceResults.AsNoTracking()
                    .Where(r => r.CarType != null && r.CarType != "")
                    .GroupBy(r => new { r.CarType, r.CarClass })
                    .Select(g => new { g.Key.CarType, g.Key.CarClass, N = g.Count() })
                    .ToListAsync(ct))
                .OrderBy(x => x.CarClass).ThenByDescending(x => x.N)
                .Select(x => new CarListItemDto(x.CarType!, x.CarClass, x.N))
                .ToList();
        }, ct);

    // ---------------------------------------------------------------- seguindo

    public async Task<IReadOnlyList<Guid>> FollowIdsAsync(Guid userId, CancellationToken ct = default) =>
        await _db.Follows.AsNoTracking().Where(f => f.UserId == userId).Select(f => f.DriverId).ToListAsync(ct);

    public async Task<IReadOnlyList<FollowedDriverDto>> FollowsAsync(Guid userId, CancellationToken ct = default)
    {
        var follows = await (
                from f in _db.Follows.AsNoTracking()
                where f.UserId == userId
                join d in _db.Drivers on f.DriverId equals d.Id
                select new { f.CreatedAt, d })
            .ToListAsync(ct);
        if (follows.Count == 0) return [];

        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var steamIds = follows.Select(f => f.d.SteamId).ToList();
        var last = await _db.RaceResults.AsNoTracking()
            .Where(r => steamIds.Contains(r.SteamId!))
            .GroupBy(r => r.SteamId!)
            .Select(g => new
            {
                SteamId = g.Key,
                Week = g.Count(r => (r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt) >= weekAgo),
                Last = g.OrderByDescending(r => r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt)
                    .Select(r => new LastRaceDto(r.Id, r.RaceSplit.ScanEvent.EventId, r.RaceSplit.ScanEvent.Title,
                        r.RaceSplit.ScanEvent.Track, r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                        r.RaceSplit.SplitNo, r.CarClass, r.ClassPosition, r.FinishStatus))
                    .FirstOrDefault(),
            })
            .ToDictionaryAsync(x => x.SteamId, ct);

        return follows
            .Select(f =>
            {
                last.TryGetValue(f.d.SteamId, out var l);
                return new FollowedDriverDto(f.d.Id, f.d.Name, f.d.Nationality, f.d.DriverRankTitle, f.d.DriverRankTier,
                    f.d.SafetyRankTitle, f.d.SafetyRankTier, f.CreatedAt, l?.Week ?? 0, l?.Last);
            })
            .OrderByDescending(f => f.LastRace?.StartsAt ?? DateTime.MinValue)
            .ToList();
    }

    public async Task<bool> FollowAsync(Guid userId, Guid driverId, CancellationToken ct = default)
    {
        if (!await _db.Drivers.AnyAsync(d => d.Id == driverId, ct)) return false;
        if (await _db.Follows.AnyAsync(f => f.UserId == userId && f.DriverId == driverId, ct)) return true;
        _db.Follows.Add(new Follow { UserId = userId, DriverId = driverId });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois cliques ao mesmo tempo: o índice único segurou o segundo, e o resultado é o mesmo.
        }
        return true;
    }

    public async Task UnfollowAsync(Guid userId, Guid driverId, CancellationToken ct = default) =>
        await _db.Follows.Where(f => f.UserId == userId && f.DriverId == driverId).ExecuteDeleteAsync(ct);
}
