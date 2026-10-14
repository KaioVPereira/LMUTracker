using LmuTracker.Application.Lmu;
using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Rankings (melhor volta por piloto), páginas de pista e de carro, e pilotos seguidos. As agregações públicas
/// ficam em cache curto (são iguais para todo mundo); "seguindo" é por usuário e não tem cache.
/// </summary>
public sealed class ExtrasService : IExtrasService
{
    private const string FinishedNormally = "Finished Normally";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);
    private const int MaxTake = 100;

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public ExtrasService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>Variantes do RaceOS contam como a mesma classe (LMP2 / LMP2_ELMS, GT3 / LMGT3).</summary>
    private static IQueryable<RaceResult> OfClass(IQueryable<RaceResult> q, string? key) => (key ?? "").ToLowerInvariant() switch
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

    private async Task<Dictionary<string, Guid>> DriverIdsAsync(IEnumerable<string?> steamIds, CancellationToken ct)
    {
        var list = steamIds.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.Drivers.AsNoTracking().Where(d => list.Contains(d.SteamId))
            .ToDictionaryAsync(d => d.SteamId, d => d.Id, ct);
    }

    // ---------------------------------------------------------------- rankings

    public async Task<LeaderboardDto> LeaderboardAsync(string? track, string? carClass, string? car, string? setup, int take,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        setup = setup is "fixed" or "open" ? setup : null;
        var key = $"lb:{track}|{carClass}|{car}|{setup}|{take}";
        return (await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            var q = _db.RaceResults.AsNoTracking().Where(r => r.BestLapTime != null && r.SteamId != null);
            if (!string.IsNullOrWhiteSpace(track)) q = q.Where(r => r.RaceSplit.ScanEvent.Track == track);
            if (!string.IsNullOrWhiteSpace(car)) q = q.Where(r => r.CarType == car);
            q = OfSetup(OfClass(q, carClass), setup);

            // 1) A melhor volta de cada piloto no recorte (GroupBy + Min traduz para SQL); 2) os detalhes da corrida
            // em que cada um dos primeiros fez essa volta.
            var top = await q
                .GroupBy(r => r.SteamId!)
                .Select(g => new { SteamId = g.Key, Lap = g.Min(r => r.BestLapTime!.Value) })
                .OrderBy(x => x.Lap)
                .Take(take)
                .ToListAsync(ct);
            var topIds = top.Select(t => t.SteamId).ToList();
            var detail = (await q
                    .Where(r => topIds.Contains(r.SteamId!))
                    .Select(r => new
                    {
                        r.SteamId, r.Name, r.Nationality, r.CarType, r.CarClass, Lap = r.BestLapTime!.Value,
                        r.RaceSplit.ScanEvent.EventId, r.RaceSplit.SplitNo,
                        At = r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                    })
                    .ToListAsync(ct))
                .GroupBy(x => x.SteamId!)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Lap).ThenBy(x => x.At).First());
            var best = top.Where(t => detail.ContainsKey(t.SteamId)).Select(t => detail[t.SteamId]).ToList();
            var drivers = await q.Select(r => r.SteamId).Distinct().CountAsync(ct);
            var ids = await DriverIdsAsync(best.Select(b => b.SteamId), ct);
            var first = best.FirstOrDefault()?.Lap;
            var rows = best.Select((b, i) => new LeaderboardRowDto(i + 1, ids.TryGetValue(b.SteamId!, out var id) ? id : null,
                b.Name, b.Nationality, b.CarType, b.CarClass, b.Lap, first is null || i == 0 ? null : Math.Round(b.Lap - first.Value, 3),
                b.EventId, b.At, b.SplitNo)).ToList();
            return new LeaderboardDto(track, carClass, car, setup ?? "all", drivers, rows);
        }))!;
    }

    // ---------------------------------------------------------------- pista

    public async Task<TrackDetailDto?> TrackAsync(string track, CancellationToken ct = default) =>
        await _cache.GetOrCreateAsync($"track:{track}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var results = _db.RaceResults.AsNoTracking().Where(r => r.RaceSplit.ScanEvent.Track == track);
            var starts = await results.CountAsync(ct);
            if (starts == 0) return null;

            var events = _db.ScanEvents.AsNoTracking().Where(e => e.Track == track && e.Splits.Any());
            var races = await events.CountAsync(ct);
            var first = await events.MinAsync(e => (DateTime?)(e.EventStartsAt ?? e.RaceStartsAt), ct);
            var last = await events.MaxAsync(e => (DateTime?)(e.EventStartsAt ?? e.RaceStartsAt), ct);
            var drivers = await results.Where(r => r.SteamId != null).Select(r => r.SteamId).Distinct().CountAsync(ct);

            var records = await results
                .Where(r => r.BestLapTime != null)
                .GroupBy(r => new { r.CarClass, Fixed = (r.RaceSplit.ScanEvent.Title ?? "").Contains("Fixed") })
                .Select(g => g.OrderBy(r => r.BestLapTime).Select(r => new
                {
                    g.Key.CarClass, g.Key.Fixed, Lap = r.BestLapTime!.Value, r.SteamId, r.Name, r.CarType,
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

            var ids = await DriverIdsAsync(records.Select(r => r.SteamId), ct);
            return new TrackDetailDto(track, races, starts, drivers, first, last,
                records.OrderBy(r => r.CarClass).ThenBy(r => r.Fixed)
                    .Select(r => new TrackRecordDto(r.CarClass, r.Fixed, r.Lap,
                        r.SteamId is not null && ids.TryGetValue(r.SteamId, out var id) ? id : null, r.Name, r.CarType, r.At, r.EventId))
                    .ToList(),
                cars, classes);
        });

    // ---------------------------------------------------------------- carro

    public async Task<CarDetailDto?> CarAsync(string car, CancellationToken ct = default) =>
        await _cache.GetOrCreateAsync($"car:{car}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var results = _db.RaceResults.AsNoTracking().Where(r => r.CarType == car);
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
                        .Select(r => new { Lap = r.BestLapTime, r.SteamId, r.Name }).FirstOrDefault(),
                })
                .ToListAsync(ct);

            var top = await results.Where(r => r.SteamId != null)
                .GroupBy(r => r.SteamId!)
                .Select(g => new
                {
                    SteamId = g.Key, Name = g.Max(r => r.Name), Nat = g.Max(r => r.Nationality), Starts = g.Count(),
                    Wins = g.Count(r => r.ClassPosition == 1), Podiums = g.Count(r => r.ClassPosition >= 1 && r.ClassPosition <= 3),
                })
                .OrderByDescending(x => x.Wins).ThenByDescending(x => x.Podiums).ThenByDescending(x => x.Starts)
                .Take(10)
                .ToListAsync(ct);

            var ids = await DriverIdsAsync(tracks.Select(t => t.Best?.SteamId).Concat(top.Select(t => t.SteamId)), ct);
            Guid? Id(string? s) => s is not null && ids.TryGetValue(s, out var id) ? id : null;
            return new CarDetailDto(car, stats.Class, stats.Starts, drivers, stats.Wins, stats.Podiums, stats.Dnfs,
                stats.Avg is null ? null : Math.Round(stats.Avg.Value, 2),
                tracks.OrderByDescending(t => t.Starts)
                    .Select(t => new CarTrackDto(t.Track, t.Starts, t.Wins, t.Best?.Lap, Id(t.Best?.SteamId), t.Best?.Name)).ToList(),
                top.Select(t => new CarDriverDto(Id(t.SteamId), t.Name ?? "—", t.Nat, t.Starts, t.Wins, t.Podiums)).ToList());
        });

    public async Task<IReadOnlyList<CarListItemDto>> CarsAsync(CancellationToken ct = default) =>
        (await _cache.GetOrCreateAsync("cars", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return (await _db.RaceResults.AsNoTracking()
                    .Where(r => r.CarType != null && r.CarType != "")
                    .GroupBy(r => new { r.CarType, r.CarClass })
                    .Select(g => new { g.Key.CarType, g.Key.CarClass, N = g.Count() })
                    .ToListAsync(ct))
                .OrderBy(x => x.CarClass).ThenByDescending(x => x.N)
                .Select(x => new CarListItemDto(x.CarType!, x.CarClass, x.N))
                .ToList();
        }))!;

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
