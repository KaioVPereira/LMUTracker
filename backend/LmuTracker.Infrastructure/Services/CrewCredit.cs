using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Carros divididos nas agregações. A linha de resultado é do carro e leva o SteamID de quem terminou, então as
/// consultas agrupadas por SteamId deixam de fora quem guiou os outros turnos e dão a melhor volta do carro a quem
/// terminou. Aqui ficam as duas correções: os créditos dos turnos (para somar a quem guiou) e o dono de cada volta.
/// </summary>
internal static class CrewCredit
{
    /// <summary>Um piloto num carro dividido: o resultado do carro + as voltas e a melhor volta dos turnos dele.</summary>
    public sealed record Credit(
        string SteamId, string Name, Guid ResultId, bool IsOwner, int Laps, double? BestLap, int? DrAdj, int? SrAdj,
        int? ClassPosition, int? ClassGridPos, int? QualiClassPosition, string? FinishStatus,
        string? CarClass, string? CarType, string EventId, int SplitNo, DateTime At);

    /// <summary>
    /// Os pilotos dos carros divididos entre os <paramref name="results"/> (já filtrados por pista, classe, período…).
    /// <paramref name="includeOwner"/> = incluir também quem terminou (o dono da linha).
    /// </summary>
    public static async Task<List<Credit>> OfAsync(AppDbContext db, IQueryable<RaceResult> results, bool includeOwner,
        CancellationToken ct)
    {
        var raw = await (
                from c in db.RaceCrewStints.AsNoTracking()
                join r in results on c.RaceResultId equals r.Id
                where c.SteamId != null && (includeOwner || c.SteamId != r.SteamId)
                select new
                {
                    SteamId = c.SteamId!, c.Name, c.RaceResultId, Owner = c.SteamId == r.SteamId,
                    Laps = c.EndLap - c.StartLap + 1, c.BestLapTime, c.DriverRankAdjustment, c.SafetyRankAdjustment,
                    r.ClassPosition, r.ClassGridPos, r.QualiClassPosition, r.FinishStatus, r.CarClass, r.CarType,
                    r.RaceSplit.ScanEvent.EventId, r.RaceSplit.SplitNo,
                    At = r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                })
            .ToListAsync(ct);
        // Um piloto pode ter vários turnos no mesmo carro: um crédito só, com as voltas somadas.
        return raw.GroupBy(x => (x.SteamId, x.RaceResultId))
            .Select(g =>
            {
                var f = g.First();
                return new Credit(f.SteamId, f.Name, f.RaceResultId, f.Owner, g.Sum(x => x.Laps), g.Min(x => x.BestLapTime),
                    f.DriverRankAdjustment, f.SafetyRankAdjustment, f.ClassPosition, f.ClassGridPos, f.QualiClassPosition,
                    f.FinishStatus, f.CarClass, f.CarType, f.EventId, f.SplitNo, f.At);
            })
            .ToList();
    }

    /// <summary>
    /// Quem fez a melhor volta de cada carro dividido entre <paramref name="resultIds"/>: o turno com a volta mais
    /// rápida. Carro solo não aparece (a volta é de quem está na linha).
    /// </summary>
    public static async Task<Dictionary<Guid, (string? SteamId, string Name)>> LapOwnersAsync(AppDbContext db,
        IEnumerable<Guid> resultIds, CancellationToken ct)
    {
        var ids = resultIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var stints = await db.RaceCrewStints.AsNoTracking()
            .Where(c => ids.Contains(c.RaceResultId) && c.BestLapTime != null)
            .Select(c => new { c.RaceResultId, c.SteamId, c.Name, c.BestLapTime })
            .ToListAsync(ct);
        return stints.GroupBy(s => s.RaceResultId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.BestLapTime).Select(s => (s.SteamId, s.Name)).First());
    }
}
