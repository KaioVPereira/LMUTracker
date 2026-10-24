using LmuTracker.Application.Lmu;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Quem está inscrito nas corridas em andamento, por piloto do Pitwall — o "Correndo agora" do perfil, dos cards de
/// quem você segue e da busca. Sai das listas de inscritos que o scanner guarda (ScanEvent.LiveDetail); a ligação com o
/// perfil é pelo nome (o RaceOS não manda SteamID nessa lista), só quando o nome é único no índice. Montado de uma vez
/// para todos e guardado no SnapshotCache (1 min), então cada perfil é só uma consulta ao dicionário.
/// </summary>
public sealed class LiveRosterService : ILiveRosterService
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(1);
    private readonly SnapshotCache _cache;

    public LiveRosterService(SnapshotCache cache)
    {
        _cache = cache;
    }

    public async Task<LiveSpotDto?> ForDriverAsync(Guid driverId, CancellationToken ct = default)
        => (await AllAsync(ct)).GetValueOrDefault(driverId);

    public Task<IReadOnlyDictionary<Guid, LiveSpotDto>> AllAsync(CancellationToken ct = default)
        => _cache.GetAsync("live:roster", FreshFor, BuildAsync, ct);

    private static async Task<IReadOnlyDictionary<Guid, LiveSpotDto>> BuildAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var events = await db.ScanEvents.AsNoTracking()
            .Where(e => e.LiveDetail != null && e.RaceEndsAt > now && (e.EventStartsAt ?? e.RaceStartsAt) <= now)
            .OrderBy(e => e.RaceEndsAt) // quem estiver em dois eventos fica com o que termina antes
            .Select(e => new { e.EventId, e.Title, e.Track, Starts = e.EventStartsAt ?? e.RaceStartsAt, e.RaceEndsAt, e.LiveDetail })
            .ToListAsync(ct);

        // Nome (como veio do RaceOS) → corrida e equipe. Em evento de equipe, quem conta são os pilotos dela.
        var spots = new Dictionary<string, LiveSpotDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in events)
        {
            var (_, team, entries) = LiveDetail.Read(e.LiveDetail);
            foreach (var x in entries)
            {
                var names = team ? x.Members : x.Members.Prepend(x.Name);
                foreach (var name in names)
                    spots.TryAdd(name, new LiveSpotDto(e.EventId, e.Title, e.Track, team ? x.Name : x.Team,
                        DateTime.SpecifyKind(e.Starts, DateTimeKind.Utc), DateTime.SpecifyKind(e.RaceEndsAt, DateTimeKind.Utc)));
            }
        }

        var result = new Dictionary<Guid, LiveSpotDto>();
        foreach (var chunk in spots.Keys.Chunk(500))
        {
            var found = await db.Drivers.AsNoTracking().Where(d => chunk.Contains(d.Name))
                .Select(d => new { d.Id, d.Name }).ToListAsync(ct);
            foreach (var g in found.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1))
                result[g.First().Id] = spots[g.Key];
        }
        return result;
    }
}
