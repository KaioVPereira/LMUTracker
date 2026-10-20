using LmuTracker.Application.Lmu;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Contas de um piloto num carro dividido: a linha de resultado é do CARRO (voltas, melhor volta, DR/SR de quem
/// terminou); o que é de cada piloto sai das voltas dos turnos dele.
/// </summary>
internal static class CrewMath
{
    public sealed record Stint(string? SteamId, string Name, int StartLap, int EndLap, int? DrAdj, int? SrAdj);

    /// <summary>As voltas que o piloto guiou (todas, quando o carro não foi dividido ou o piloto não está nos turnos).</summary>
    public static List<LapDto> LapsOf(IReadOnlyList<LapDto> laps, IReadOnlyCollection<Stint> stints, string steamId)
    {
        var mine = stints.Where(s => s.SteamId == steamId).ToList();
        if (mine.Count == 0) return laps.ToList();
        return laps.Where(l => mine.Any(s => l.Lap >= s.StartLap && l.Lap <= s.EndLap)).ToList();
    }

    /// <summary>Melhor volta válida (sem pit) entre as voltas dadas.</summary>
    public static double? BestLap(IEnumerable<LapDto> laps) =>
        laps.Where(l => !l.Pit && l.LapTime is > 0).Select(l => l.LapTime).Min();

    /// <summary>Os outros pilotos do carro (na ordem dos turnos, sem repetir).</summary>
    public static List<string> Others(IEnumerable<Stint> stints, string steamId) =>
        stints.Where(s => s.SteamId != steamId).Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
