using LmuTracker.Application.Lmu;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Rankings de pilotos (Driver Rank, Safety Rank, vitórias, em alta, mais limpos, mais ativos) e de carros.
/// Uma consulta agrupada por piloto por recorte (classe + período), no <see cref="SnapshotCache"/>; cada aba é só
/// uma ordenação diferente dessa lista — o que também permite devolver a posição de quem está logado fora do topo
/// e paginar (skip/take) sem voltar ao banco.
/// </summary>
public sealed class RankingsService : IRankingsService
{
    private const string FinishedNormally = "Finished Normally";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);
    private static readonly string[] Titles = { "Bronze", "Silver", "Gold", "Platinum", "Diamond" };

    /// <summary>Mínimo de corridas no período para entrar nas abas de média (mais limpos) e em alta.</summary>
    private const int MinCleanStarts = 5;
    private const int MinRiserStarts = 3;
    private const int MinCarStarts = 10;

    private readonly SnapshotCache _cache;

    public RankingsService(SnapshotCache cache) => _cache = cache;

    private sealed record DriverInfo(
        Guid Id, string Name, string? Nationality, string? DrTitle, int? DrTier, int? DrProgress,
        string? SrTitle, int? SrTier, int? SrProgress, DateTime LastSeenAt);

    private sealed record PeriodStats(
        string SteamId, string Name, string? Nationality, int Starts, int Wins, int Podiums,
        int IncidentStarts, int Incidents, int Penalties, int DrGain, int SrGain, int Laps,
        int Poles, int GainStarts, int PositionsGained);

    private static string NormPeriod(string? p) => p is "7d" or "30d" or "all" ? p : "30d";

    /// <summary>Código de nacionalidade como o jogo grava ("BR", "GB"…); vazio ou inválido = sem filtro.</summary>
    internal static string? NormCountry(string? c) =>
        c?.Trim() is { Length: >= 2 and <= 3 } t && t.All(char.IsLetter) ? t.ToUpperInvariant() : null;

    private static DateTime? From(string period) => period switch
    {
        "7d" => DateTime.UtcNow.AddDays(-7),
        "30d" => DateTime.UtcNow.AddDays(-30),
        _ => null,
    };

    /// <summary>Retrato atual de todos os pilotos conhecidos, pelo SteamID.</summary>
    private Task<Dictionary<string, DriverInfo>> DriversIndexAsync(CancellationToken ct) =>
        _cache.GetAsync("rk:drivers", CacheFor, (db, ct) => db.Drivers.AsNoTracking()
            .Select(d => new { d.SteamId, Info = new DriverInfo(d.Id, d.Name, d.Nationality, d.DriverRankTitle, d.DriverRankTier,
                d.DriverRankProgress, d.SafetyRankTitle, d.SafetyRankTier, d.SafetyRankProgress, d.LastSeenAt) })
            .ToDictionaryAsync(x => x.SteamId, x => x.Info, ct), ct);

    /// <summary>Números de cada piloto no recorte (classe + período), numa consulta só.</summary>
    private Task<List<PeriodStats>> StatsAsync(string? carClass, string period, CancellationToken ct) =>
        _cache.GetAsync($"rk:stats:{carClass}|{period}", CacheFor, (db, ct) =>
        {
            var q = ExtrasService.OfClass(db.RaceResults.AsNoTracking().Where(r => r.SteamId != null), carClass);
            if (From(period) is { } from)
                q = q.Where(r => (r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt) >= from);
            return q
                .GroupBy(r => r.SteamId!)
                .Select(g => new PeriodStats(
                    g.Key, g.Max(r => r.Name)!, g.Max(r => r.Nationality),
                    g.Count(),
                    g.Count(r => r.ClassPosition == 1 && r.FinishStatus == FinishedNormally),
                    g.Count(r => r.ClassPosition >= 1 && r.ClassPosition <= 3 && r.FinishStatus == FinishedNormally),
                    g.Count(r => r.Incidents != null),
                    g.Sum(r => r.Incidents ?? 0),
                    g.Sum(r => r.Penalties ?? 0),
                    g.Sum(r => r.DriverRankAdjustment ?? 0),
                    g.Sum(r => r.SafetyRankAdjustment ?? 0),
                    g.Sum(r => r.Laps),
                    g.Count(r => r.QualiClassPosition == 1),
                    g.Count(r => r.FinishStatus == FinishedNormally && r.ClassGridPos != null && r.ClassPosition != null),
                    g.Sum(r => r.FinishStatus == FinishedNormally && r.ClassGridPos != null && r.ClassPosition != null
                        ? r.ClassGridPos!.Value - r.ClassPosition!.Value : 0)))
                .ToListAsync(ct);
        }, ct);

    private static int TitleIndex(string? t) => Array.FindIndex(Titles, x => x.Equals(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>Bronze 1 = 0…100, Bronze 2 = 100…200, …, Silver 1 = 300… (mesma escala do perfil).</summary>
    private static int Score(string? title, int? tier, int? progress) =>
        TitleIndex(title) is var i and >= 0 && tier is { } t
            ? i * 300 + (Math.Clamp(t, 1, 3) - 1) * 100 + Math.Clamp(progress ?? 0, 0, 100)
            : -1;

    public async Task<RankingDto?> DriversAsync(string kind, string? carClass, string? period, string? country, int skip, int take,
        Guid? me, CancellationToken ct = default)
    {
        country = NormCountry(country);
        kind = kind.ToLowerInvariant();
        if (kind is not ("dr" or "sr" or "wins" or "risers" or "clean" or "active" or "poles" or "gains")) return null;
        period = NormPeriod(period);
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(skip, 0);
        if (kind is "dr" or "sr") carClass = null; // rank é do piloto, não da classe

        var drivers = await DriversIndexAsync(ct);
        var stats = await StatsAsync(carClass, period, ct);
        var bySteam = stats.ToDictionary(s => s.SteamId);

        // Lista completa já ordenada: (SteamId, valor, extra).
        List<(string SteamId, double Value, double? Extra)> ordered;
        var minStarts = 0;
        switch (kind)
        {
            case "dr":
            case "sr":
            {
                var from = From(period);
                var sr = kind == "sr";
                ordered = drivers
                    .Where(d => from is null || d.Value.LastSeenAt >= from)
                    .Select(d => (d.Key,
                        Score: sr ? Score(d.Value.SrTitle, d.Value.SrTier, d.Value.SrProgress) : Score(d.Value.DrTitle, d.Value.DrTier, d.Value.DrProgress),
                        Title: TitleIndex(sr ? d.Value.SrTitle : d.Value.DrTitle),
                        Tier: (sr ? d.Value.SrTier : d.Value.DrTier) ?? 0))
                    .Where(x => x.Score >= 0)
                    // "Silver 3 com 100%" empata na escala com "Gold 1 com 0%" (e "Gold 1 com 100%" com "Gold 2 com 0%"):
                    // no empate, o rank mais alto vem antes.
                    .OrderByDescending(x => x.Score).ThenByDescending(x => x.Title).ThenByDescending(x => x.Tier)
                    .ThenByDescending(x => bySteam.TryGetValue(x.Key, out var s) ? s.Starts : 0)
                    .Select(x => (x.Key, (double)x.Score, (double?)null))
                    .ToList();
                break;
            }
            case "wins":
                ordered = stats.Where(s => s.Wins > 0)
                    .OrderByDescending(s => s.Wins).ThenByDescending(s => s.Podiums).ThenBy(s => s.Starts)
                    .Select(s => (s.SteamId, (double)s.Wins, (double?)Math.Round((double)s.Wins / s.Starts, 3)))
                    .ToList();
                break;
            case "risers":
                minStarts = MinRiserStarts;
                ordered = stats.Where(s => s.Starts >= MinRiserStarts && s.DrGain > 0)
                    .OrderByDescending(s => s.DrGain).ThenByDescending(s => s.SrGain)
                    .Select(s => (s.SteamId, (double)s.DrGain, (double?)s.SrGain))
                    .ToList();
                break;
            case "clean":
                minStarts = MinCleanStarts;
                ordered = stats.Where(s => s.IncidentStarts >= MinCleanStarts)
                    .Select(s => (s, Inc: (double)s.Incidents / s.IncidentStarts))
                    .OrderBy(x => x.Inc).ThenByDescending(x => x.s.IncidentStarts)
                    .Select(x => (x.s.SteamId, Math.Round(x.Inc, 2), (double?)Math.Round((double)x.s.Penalties / x.s.IncidentStarts, 2)))
                    .ToList();
                break;
            case "poles":
                ordered = stats.Where(s => s.Poles > 0)
                    .OrderByDescending(s => s.Poles).ThenBy(s => s.Starts)
                    .Select(s => (s.SteamId, (double)s.Poles, (double?)Math.Round((double)s.Poles / s.Starts, 3)))
                    .ToList();
                break;
            case "gains":
                minStarts = MinRiserStarts;
                ordered = stats.Where(s => s.GainStarts >= MinRiserStarts)
                    .OrderByDescending(s => s.PositionsGained).ThenBy(s => s.GainStarts)
                    .Select(s => (s.SteamId, (double)s.PositionsGained, (double?)Math.Round((double)s.PositionsGained / s.GainStarts, 1)))
                    .ToList();
                break;
            default: // active
                ordered = stats
                    .OrderByDescending(s => s.Starts).ThenByDescending(s => s.Laps)
                    .Select(s => (s.SteamId, (double)s.Starts, (double?)s.Laps))
                    .ToList();
                break;
        }

        // País: a lista inteira é filtrada antes de numerar, então a posição é dentro do país.
        if (country is not null)
            ordered = ordered.Where(o => NationalityOf(o.SteamId) == country).ToList();

        string? NationalityOf(string steamId) =>
            NormCountry((drivers.TryGetValue(steamId, out var d) ? d.Nationality : null)
                        ?? (bySteam.TryGetValue(steamId, out var s) ? s.Nationality : null));

        RankingRowDto Row(int i)
        {
            var (steamId, value, extra) = ordered[i];
            drivers.TryGetValue(steamId, out var d);
            bySteam.TryGetValue(steamId, out var s);
            return new RankingRowDto(i + 1, d?.Id, d?.Name ?? s?.Name ?? "—", d?.Nationality ?? s?.Nationality,
                d?.DrTitle, d?.DrTier, d?.DrProgress, d?.SrTitle, d?.SrTier, d?.SrProgress,
                s?.Starts ?? 0, s?.Wins ?? 0, s?.Podiums ?? 0, value, extra);
        }

        var rows = Enumerable.Range(skip, Math.Max(0, Math.Min(take, ordered.Count - skip))).Select(Row).ToList();
        RankingRowDto? mine = null;
        if (me is { } meId && drivers.FirstOrDefault(d => d.Value.Id == meId).Key is { } mySteam)
        {
            var at = ordered.FindIndex(o => o.SteamId == mySteam);
            if (at >= 0) mine = Row(at);
        }
        return new RankingDto(kind, carClass, country, period, minStarts, ordered.Count, rows, mine);
    }

    public async Task<CarRankingDto> CarsAsync(string? carClass, string? period, string? country, CancellationToken ct = default)
    {
        period = NormPeriod(period);
        country = NormCountry(country);
        return await _cache.GetAsync($"rk:cars:{carClass}|{period}|{country}", CacheFor, async (db, ct) =>
        {
            var q = ExtrasService.OfClass(db.RaceResults.AsNoTracking().Where(r => r.CarType != null), carClass);
            if (country is not null) q = q.Where(r => r.Nationality == country);
            if (From(period) is { } from)
                q = q.Where(r => (r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt) >= from);
            var cars = await q
                .GroupBy(r => r.CarType!)
                .Select(g => new
                {
                    Car = g.Key,
                    Class = g.Max(r => r.CarClass),
                    Starts = g.Count(),
                    Wins = g.Count(r => r.ClassPosition == 1 && r.FinishStatus == FinishedNormally),
                    Podiums = g.Count(r => r.ClassPosition >= 1 && r.ClassPosition <= 3 && r.FinishStatus == FinishedNormally),
                    Drivers = g.Select(r => r.SteamId).Distinct().Count(),
                    Avg = g.Where(r => r.FinishStatus == FinishedNormally).Average(r => (double?)r.ClassPosition),
                })
                .ToListAsync(ct);
            var rows = cars.Where(c => c.Starts >= MinCarStarts)
                .OrderByDescending(c => c.Wins).ThenByDescending(c => (double)c.Wins / c.Starts).ThenByDescending(c => c.Podiums)
                .Select((c, i) => new CarRankingRowDto(i + 1, c.Car, c.Class, c.Starts, c.Wins, c.Podiums, c.Drivers,
                    Math.Round((double)c.Wins / c.Starts, 3), c.Avg is null ? null : Math.Round(c.Avg.Value, 1)))
                .ToList();
            return new CarRankingDto(carClass, country, period, MinCarStarts, rows.Count, rows);
        }, ct);
    }

    public async Task<IReadOnlyList<CountryDto>> CountriesAsync(CancellationToken ct = default) =>
        (await DriversIndexAsync(ct)).Values
            .Select(d => NormCountry(d.Nationality))
            .Where(c => c is not null)
            .GroupBy(c => c!)
            .Select(g => new CountryDto(g.Key, g.Count()))
            .OrderByDescending(c => c.Drivers)
            .ToList();
}
