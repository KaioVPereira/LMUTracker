using LmuTracker.Application.Lmu;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Destaques da tela inicial. É a mesma resposta para todo mundo e cada bloco agrega milhares de resultados —
/// fica 60 s em cache, então o custo não cresce com o número de pessoas olhando.
/// </summary>
public sealed class HomeService : IHomeService
{
    private const string FinishedNormally = "Finished Normally";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public HomeService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<HomeDto> GetAsync(CancellationToken ct = default) =>
        (await _cache.GetOrCreateAsync("home", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return await BuildAsync(ct);
        }))!;

    private async Task<HomeDto> BuildAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var weekAgo = now - Week;
        var dayAgo = now - Day;

        // Todo resultado gravado conta — inclusive de corridas que voltaram à fila para ganhar detalhe novo.
        var results = _db.RaceResults.AsNoTracking();

        // ---------- recordes: melhor volta por pista, classe e tipo de setup (fixo × livre) ----------
        var records = await results
            .Where(r => r.BestLapTime != null && r.RaceSplit.ScanEvent.Track != null
                        && (r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt) >= weekAgo)
            .GroupBy(r => new
            {
                Track = r.RaceSplit.ScanEvent.Track!,
                r.CarClass,
                Fixed = (r.RaceSplit.ScanEvent.Title ?? "").Contains("Fixed"),
            })
            .Select(g => g.OrderBy(r => r.BestLapTime)
                .Select(r => new
                {
                    g.Key.Track, g.Key.CarClass, g.Key.Fixed, Lap = r.BestLapTime!.Value,
                    r.SteamId, r.Name, r.Nationality, r.CarType, r.RaceSplit.ScanEvent.EventId,
                    SetAt = r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                    r.RaceSplit.SplitNo,
                })
                .First())
            .ToListAsync(ct);

        // ---------- vencedores do split 1, por classe ----------
        var winnersRaw = await results
            .Where(r => r.RaceSplit.SplitNo == 1 && r.ClassPosition == 1 && r.FinishStatus == FinishedNormally)
            .OrderByDescending(r => r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt)
            .ThenBy(r => r.CarClass)
            .Take(12)
            .Select(r => new
            {
                r.RaceSplit.ScanEvent.EventId, r.RaceSplit.ScanEvent.Title, r.RaceSplit.ScanEvent.Track,
                r.RaceSplit.ScanEvent.Tier,
                StartsAt = r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                r.CarClass, r.SteamId, r.Name, r.Nationality, r.CarType, r.Laps, r.RaceSplit.DriverCount,
                Gap = r.RaceSplit.Results
                    .Where(x => x.CarClass == r.CarClass && x.ClassPosition == 2 && x.Laps == r.Laps
                                && x.FinishStatus == FinishedNormally)
                    .Select(x => x.FinishTime - r.FinishTime)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        // ---------- chegada mais apertada (últimas 24 h) ----------
        var closeRaw = await results
            .Where(r => r.RaceSplit.SplitNo == 1 && r.ClassPosition == 1 && r.FinishStatus == FinishedNormally
                        && r.FinishTime != null
                        && (r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt) >= dayAgo)
            .Select(r => new
            {
                r.RaceSplit.ScanEvent.EventId, r.RaceSplit.ScanEvent.Title, r.RaceSplit.ScanEvent.Track,
                StartsAt = r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                r.CarClass, WinnerSteam = r.SteamId, WinnerName = r.Name,
                Second = r.RaceSplit.Results
                    .Where(x => x.CarClass == r.CarClass && x.ClassPosition == 2 && x.Laps == r.Laps
                                && x.FinishStatus == FinishedNormally && x.FinishTime != null)
                    .Select(x => new { x.SteamId, x.Name, Gap = x.FinishTime!.Value - r.FinishTime!.Value })
                    .FirstOrDefault(),
            })
            .Where(x => x.Second != null && x.Second.Gap > 0)
            .OrderBy(x => x.Second!.Gap)
            .Take(3)
            .ToListAsync(ct);

        // ---------- maior saldo de DR na semana (mín. 3 corridas) ----------
        var weekly = results.Where(r => r.SteamId != null
                                        && (r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt) >= weekAgo);
        var climbersRaw = await weekly
            .Where(r => r.DriverRankAdjustment != null)
            .GroupBy(r => r.SteamId!)
            .Where(g => g.Count() >= 3)
            .Select(g => new { SteamId = g.Key, Gain = g.Sum(r => r.DriverRankAdjustment!.Value), Races = g.Count() })
            .Where(x => x.Gain > 0)
            .OrderByDescending(x => x.Gain).ThenByDescending(x => x.Races)
            .Take(5)
            .ToListAsync(ct);
        var climberIds = climbersRaw.Select(c => c.SteamId).ToList();
        var climberHistory = (await weekly
                .Where(r => climberIds.Contains(r.SteamId!))
                .Select(r => new
                {
                    r.SteamId, r.Name, r.Nationality, r.DriverRankTitle, r.DriverRankTier, r.DriverRankProgress,
                    At = r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                })
                .ToListAsync(ct))
            .GroupBy(r => r.SteamId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.At).ToList());

        // ---------- mais ativos e largadas por classe (últimas 24 h) ----------
        var daily = results.Where(r => (r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt) >= dayAgo);
        var activeRaw = await daily
            .Where(r => r.SteamId != null)
            .GroupBy(r => r.SteamId!)
            .Select(g => new { SteamId = g.Key, Races = g.Count(), Name = g.Max(r => r.Name), Nat = g.Max(r => r.Nationality) })
            .OrderByDescending(x => x.Races)
            .Take(5)
            .ToListAsync(ct);
        var share = (await daily
                .Where(r => r.CarClass != null && r.CarClass != "")
                .GroupBy(r => r.CarClass!)
                .Select(g => new { CarClass = g.Key, Starts = g.Count() })
                .OrderByDescending(x => x.Starts)
                .ToListAsync(ct))
            .Select(x => new ClassShareDto(x.CarClass, x.Starts))
            .ToList();

        // ---------- SteamID -> id interno (o site nunca expõe o SteamID) ----------
        var steamIds = records.Select(r => r.SteamId)
            .Concat(winnersRaw.Select(w => w.SteamId))
            .Concat(closeRaw.SelectMany(c => new[] { c.WinnerSteam, c.Second!.SteamId }))
            .Concat(climberIds)
            .Concat(activeRaw.Select(a => a.SteamId))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();
        var ids = await _db.Drivers.AsNoTracking()
            .Where(d => steamIds.Contains(d.SteamId))
            .ToDictionaryAsync(d => d.SteamId, d => d.Id, ct);
        Guid? Id(string? steam) => steam is not null && ids.TryGetValue(steam, out var id) ? id : null;

        return new HomeDto(
            Records: records
                .OrderByDescending(r => r.SetAt)
                .Select(r => new LapRecordDto(r.Track, r.CarClass, r.Fixed, r.Lap, Id(r.SteamId), r.Name, r.Nationality,
                    r.CarType, r.EventId, r.SetAt, r.SplitNo))
                .ToList(),
            Winners: winnersRaw
                .Select(w => new TopSplitWinnerDto(w.EventId, w.Title, w.Track, w.Tier, w.StartsAt, w.CarClass, Id(w.SteamId),
                    w.Name, w.Nationality, w.CarType, w.Laps, w.DriverCount, w.Gap))
                .ToList(),
            Climbers: climbersRaw
                .Select(c =>
                {
                    var h = climberHistory.GetValueOrDefault(c.SteamId) ?? [];
                    var first = h.FirstOrDefault();
                    var last = h.LastOrDefault();
                    return new RankClimberDto(Id(c.SteamId), last?.Name ?? "—", last?.Nationality, c.Races, c.Gain,
                        first?.DriverRankTitle, first?.DriverRankTier, first?.DriverRankProgress,
                        last?.DriverRankTitle, last?.DriverRankTier, last?.DriverRankProgress);
                })
                .ToList(),
            CloseFinishes: closeRaw
                .Select(c => new CloseFinishDto(c.EventId, c.Title, c.Track, c.StartsAt, c.CarClass, c.Second!.Gap,
                    c.WinnerName, Id(c.WinnerSteam), c.Second.Name, Id(c.Second.SteamId)))
                .ToList(),
            MostActive: activeRaw.Select(a => new ActiveDriverDto(Id(a.SteamId), a.Name ?? "—", a.Nat, a.Races)).ToList(),
            ClassShare: share,
            GeneratedAt: now);
    }
}
