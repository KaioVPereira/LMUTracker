using LmuTracker.Application.Lmu;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Destaques da tela inicial. É a mesma resposta para todo mundo e cada bloco agrega milhares de resultados —
/// fica no <see cref="SnapshotCache"/> (recalculado em segundo plano a cada minuto de uso), então ninguém espera
/// as consultas e o custo não cresce com o número de pessoas olhando.
/// </summary>
public sealed class HomeService : IHomeService
{
    private const string FinishedNormally = "Finished Normally";
    public const string CacheKey = "home";
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private readonly SnapshotCache _cache;

    public HomeService(SnapshotCache cache) => _cache = cache;

    public Task<HomeDto> GetAsync(CancellationToken ct = default) => _cache.GetAsync(CacheKey, CacheFor, BuildAsync, ct);

    public static async Task<HomeDto> BuildAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var weekAgo = now - Week;
        var dayAgo = now - Day;

        // Todo resultado gravado conta — inclusive de corridas que voltaram à fila para ganhar detalhe novo.
        var results = db.RaceResults.AsNoTracking();

        // ---------- recordes: melhor volta por pista, classe e tipo de setup (fixo × livre) ----------
        // Em duas etapas: o tempo mínimo de cada grupo (um GROUP BY só) e depois as linhas com esses tempos.
        // O "First() de cada grupo" direto vira uma subconsulta por grupo, cada uma relendo a semana inteira —
        // era o que estourava o tempo em produção.
        var weekLaps = results.Where(r => r.BestLapTime != null && r.RaceSplit.ScanEvent.Track != null
                                          && (r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt) >= weekAgo);
        var bestByGroup = await weekLaps
            .GroupBy(r => new
            {
                Track = r.RaceSplit.ScanEvent.Track!,
                r.CarClass,
                Fixed = (r.RaceSplit.ScanEvent.Title ?? "").Contains("Fixed"),
            })
            .Select(g => new { g.Key.Track, g.Key.CarClass, g.Key.Fixed, Lap = g.Min(r => r.BestLapTime!.Value) })
            .ToListAsync(ct);
        var bestLaps = bestByGroup.Select(b => b.Lap).Distinct().ToList();
        var candidates = await weekLaps
            .Where(r => bestLaps.Contains(r.BestLapTime!.Value))
            .Select(r => new
            {
                Track = r.RaceSplit.ScanEvent.Track!, r.CarClass, Fixed = (r.RaceSplit.ScanEvent.Title ?? "").Contains("Fixed"),
                Lap = r.BestLapTime!.Value, r.Id, r.SteamId, r.Name, r.Nationality, r.CarType, r.RaceSplit.ScanEvent.EventId,
                SetAt = r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                r.RaceSplit.SplitNo,
            })
            .ToListAsync(ct);
        // Empate no mesmo tempo: fica quem fez primeiro.
        var records = bestByGroup
            .Select(b => candidates
                .Where(c => c.Track == b.Track && c.CarClass == b.CarClass && c.Fixed == b.Fixed && c.Lap == b.Lap)
                .OrderBy(c => c.SetAt)
                .FirstOrDefault())
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();
        // Carro dividido: o recorde é de quem fez a volta, não de quem terminou o carro.
        var lapOwners = await CrewCredit.LapOwnersAsync(db, records.Select(r => r.Id), ct);
        records = records.Select(r => lapOwners.TryGetValue(r.Id, out var o) && o.SteamId != r.SteamId
                ? r with { SteamId = o.SteamId, Name = o.Name, Nationality = null }
                : r)
            .ToList();

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
        // Carros divididos: quem guiou um turno sem ter terminado também conta (com o saldo de DR do turno dele).
        var weekCrew = await CrewCredit.OfAsync(db, results.Where(r =>
            (r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt) >= weekAgo), includeOwner: false, ct);
        var climbersRaw = (await weekly
                .Where(r => r.DriverRankAdjustment != null)
                .GroupBy(r => r.SteamId!)
                .Select(g => new { SteamId = g.Key, Gain = g.Sum(r => r.DriverRankAdjustment!.Value), Races = g.Count() })
                .ToListAsync(ct))
            .Concat(weekCrew.Where(c => c.DrAdj is not null).Select(c => new { SteamId = c.SteamId, Gain = c.DrAdj!.Value, Races = 1 }))
            .GroupBy(x => x.SteamId)
            .Select(g => new { SteamId = g.Key, Gain = g.Sum(x => x.Gain), Races = g.Sum(x => x.Races) })
            .Where(x => x.Races >= 3 && x.Gain > 0)
            .OrderByDescending(x => x.Gain).ThenByDescending(x => x.Races)
            .Take(5)
            .ToList();
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
        var dayCrew = weekCrew.Where(c => c.At >= dayAgo).ToList();
        var activeRaw = (await daily
                .Where(r => r.SteamId != null)
                .GroupBy(r => r.SteamId!)
                .Select(g => new { SteamId = g.Key, Races = g.Count(), Name = g.Max(r => r.Name), Nat = g.Max(r => r.Nationality) })
                .ToListAsync(ct))
            .Concat(dayCrew.Select(c => new { SteamId = c.SteamId, Races = 1, Name = (string?)c.Name, Nat = (string?)null }))
            .GroupBy(x => x.SteamId)
            .Select(g => new { SteamId = g.Key, Races = g.Sum(x => x.Races), Name = g.Max(x => x.Name), Nat = g.Max(x => x.Nat) })
            .OrderByDescending(x => x.Races)
            .Take(5)
            .ToList();
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
        var people = await db.Drivers.AsNoTracking()
            .Where(d => steamIds.Contains(d.SteamId))
            .ToDictionaryAsync(d => d.SteamId, d => new { d.Id, d.Nationality }, ct);
        Guid? Id(string? steam) => steam is not null && people.TryGetValue(steam, out var p) ? p.Id : null;
        string? Nat(string? steam) => steam is not null && people.TryGetValue(steam, out var p) ? p.Nationality : null;

        return new HomeDto(
            Records: records
                .OrderByDescending(r => r.SetAt)
                .Select(r => new LapRecordDto(r.Track, r.CarClass, r.Fixed, r.Lap, Id(r.SteamId), r.Name, r.Nationality ?? Nat(r.SteamId),
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
            MostActive: activeRaw.Select(a => new ActiveDriverDto(Id(a.SteamId), a.Name ?? "—", a.Nat ?? Nat(a.SteamId), a.Races)).ToList(),
            ClassShare: share,
            GeneratedAt: now);
    }
}
