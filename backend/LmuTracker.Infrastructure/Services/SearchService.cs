using System.Globalization;
using System.Text;
using LmuTracker.Application.Lmu;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Busca da paleta Ctrl+K: pilotos, pistas, carros e corridas. Sem acento e sem caixa ("portimao" acha
/// "Portimão"). Pistas e carros são poucos e mudam devagar — ficam em cache e são filtrados em memória.
/// </summary>
public sealed class SearchService : ISearchService
{
    private const int MinLength = 2;
    private const int PerGroup = 6;
    private const int RacesPerGroup = 5;
    private const string AccentInsensitive = "Latin1_General_CI_AI";
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public SearchService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<SearchResultsDto> SearchAsync(string? query, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim();
        if (q.Length < MinLength)
            return new SearchResultsDto(q, [], [], [], []);
        if (q.Length > 64) q = q[..64];

        var folded = Fold(q);
        var like = $"%{EscapeLike(q)}%";

        var drivers = await SearchDriversAsync(q, folded, like, ct);

        var tracks = (await TracksAsync(ct))
            .Where(t => Fold(t.Track).Contains(folded))
            .OrderByDescending(t => Fold(t.Track).StartsWith(folded))
            .ThenByDescending(t => t.Races)
            .Take(PerGroup)
            .ToList();

        // Carro casa pelo nome ou pela classe ("gt3", "hyper").
        var cars = (await CarsAsync(ct))
            .Where(c => Fold(c.Car).Contains(folded) || Fold(c.CarClass ?? "").Contains(folded))
            .OrderByDescending(c => Fold(c.Car).StartsWith(folded))
            .ThenByDescending(c => c.Starts)
            .Take(PerGroup)
            .ToList();

        var races = await _db.ScanEvents.AsNoTracking()
            .Where(e => e.Splits.Any()
                        && (EF.Functions.Like(EF.Functions.Collate(e.Title!, AccentInsensitive), like, "\\")
                            || EF.Functions.Like(EF.Functions.Collate(e.Track!, AccentInsensitive), like, "\\")))
            .OrderByDescending(e => e.EventStartsAt ?? e.RaceStartsAt)
            .Take(RacesPerGroup)
            .Select(e => new SearchRaceDto(e.EventId, e.Title, e.Track, e.Tier, e.EventStartsAt ?? e.RaceStartsAt, e.Classes))
            .ToListAsync(ct);

        return new SearchResultsDto(q, drivers, tracks, cars, races);
    }

    private async Task<List<SearchDriverDto>> SearchDriversAsync(string q, string folded, string like, CancellationToken ct)
    {
        // Candidatos pelo nome (os vistos mais recentemente primeiro); a ordem final põe quem começa com o termo
        // e quem tem mais corridas no topo.
        var candidates = await _db.Drivers.AsNoTracking()
            .Where(d => EF.Functions.Like(EF.Functions.Collate(d.Name, AccentInsensitive), like, "\\"))
            .OrderByDescending(d => d.LastSeenAt)
            .Take(40)
            .Select(d => new { d.Id, d.SteamId, d.Name, d.Nationality, d.DriverRankTitle, d.DriverRankTier })
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];

        var steamIds = candidates.Select(c => c.SteamId).ToList();
        var counts = await _db.RaceResults.AsNoTracking()
            .Where(r => r.SteamId != null && steamIds.Contains(r.SteamId))
            .GroupBy(r => r.SteamId!)
            .Select(g => new { SteamId = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.SteamId, x => x.N, ct);

        return candidates
            .Select(c => new SearchDriverDto(c.Id, c.Name, c.Nationality, c.DriverRankTitle, c.DriverRankTier,
                counts.GetValueOrDefault(c.SteamId)))
            .OrderByDescending(d => Fold(d.Name).StartsWith(folded))
            .ThenByDescending(d => d.Races)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Take(PerGroup)
            .ToList();
    }

    private Task<List<SearchTrackDto>> TracksAsync(CancellationToken ct) =>
        _cache.GetOrCreateAsync("search:tracks", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CatalogTtl;
            return await _db.ScanEvents.AsNoTracking()
                .Where(e => e.Track != null && e.Splits.Any())
                .GroupBy(e => e.Track!)
                .Select(g => new SearchTrackDto(g.Key, g.Count()))
                .ToListAsync(ct);
        })!;

    private Task<List<SearchCarDto>> CarsAsync(CancellationToken ct) =>
        _cache.GetOrCreateAsync("search:cars", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CatalogTtl;
            return await _db.RaceResults.AsNoTracking()
                .Where(r => r.CarType != null && r.CarType != "")
                .GroupBy(r => new { r.CarType, r.CarClass })
                .Select(g => new SearchCarDto(g.Key.CarType!, g.Key.CarClass, g.Count()))
                .ToListAsync(ct);
        })!;

    /// <summary>Minúsculas e sem acento: "Portimão" -> "portimao".</summary>
    private static string Fold(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>O termo vira literal no LIKE: %, _ e [ do usuário não são curingas.</summary>
    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
