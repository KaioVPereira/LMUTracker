using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Worker;

/// <summary>
/// Confere a previsão de split (LiveSplits) contra o resultado real, nas corridas que têm a lista de inscritos guardada
/// (com a posição de elo) e já foram baixadas. Imprime só contagens e taxas de acerto — nenhum nome.
/// Uso: dotnet run --project LmuTracker.Worker -- check-splits
/// </summary>
public static class SplitCheck
{
    public static async Task RunAsync(AppDbContext db)
    {
        var events = await db.ScanEvents.AsNoTracking()
            .Where(e => e.LiveDetail != null && e.Splits.Any())
            .Select(e => new { e.Id, e.Title, e.LiveDetail, e.LiveData })
            .ToListAsync();
        Console.WriteLine($"{events.Count} corridas com inscritos guardados e resultado.");

        var totals = Enum.GetValues<LiveSplits.Strategy>().ToDictionary(s => s, _ => (Hit: 0, Near: 0, N: 0));
        foreach (var e in events)
        {
            var (_, team, entries) = LiveDetail.Read(e.LiveDetail);
            if (entries.All(x => x.EloPos is null))
            {
                Console.WriteLine($"- {e.Title}: lista sem posição de elo (guardada antes da previsão), pulada.");
                continue;
            }
            var rows = await db.RaceResults.AsNoTracking()
                .Where(r => r.RaceSplit.ScanEventId == e.Id)
                .Select(r => new { r.Name, r.TeamName, r.RaceSplit.SplitNo })
                .ToListAsync();
            var splits = rows.Count == 0 ? 0 : rows.Max(r => r.SplitNo);
            // Split real de cada inscrito: pelo nome (individual) ou pelo nome da equipe.
            var real = rows
                .GroupBy(r => (team ? r.TeamName : r.Name) ?? "", StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key != "" && g.Select(r => r.SplitNo).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().SplitNo, StringComparer.OrdinalIgnoreCase);

            var line = $"- {e.Title}{(team ? " (equipe)" : "")}: {entries.Count} inscritos, {rows.Count} no resultado, {splits} splits";
            foreach (var strategy in Enum.GetValues<LiveSplits.Strategy>())
            {
                var predicted = LiveSplits.Predict(entries, splits, strategy);
                int hit = 0, near = 0, n = 0;
                for (var i = 0; i < entries.Count; i++)
                {
                    if (predicted[i] is not { } p || !real.TryGetValue(entries[i].Name, out var r)) continue;
                    n++;
                    if (p == r) hit++;
                    if (Math.Abs(p - r) <= 1) near++;
                }
                var t = totals[strategy];
                totals[strategy] = (t.Hit + hit, t.Near + near, t.N + n);
                line += $" | {strategy}: {Pct(hit, n)} exato, {Pct(near, n)} ±1 ({n} comparados)";
            }
            Console.WriteLine(line);
        }
        Console.WriteLine();
        foreach (var (strategy, t) in totals)
            Console.WriteLine($"TOTAL {strategy}: {Pct(t.Hit, t.N)} exato, {Pct(t.Near, t.N)} ±1 ({t.N} comparados)");
    }

    private static string Pct(int a, int n) => n == 0 ? "—" : $"{100.0 * a / n:0.#}%";
}
