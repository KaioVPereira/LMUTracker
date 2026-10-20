using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>
/// Um turno de pilotagem num carro dividido (corridas de equipe com troca de piloto). O RaceOS manda UMA linha de
/// resultado por carro, com o piloto que terminou; quem mais guiou só aparece nos revezamentos ("swaps") da linha.
/// Só é gravado quando o carro teve mais de um piloto — corrida solo não tem linha aqui.
/// </summary>
public class RaceCrewStint : Entity
{
    public Guid RaceResultId { get; set; }
    public RaceResult RaceResult { get; set; } = null!;

    /// <summary>Ordem do turno no carro (0 = largada).</summary>
    public int Seq { get; set; }

    public int StartLap { get; set; }
    public int EndLap { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>SteamID do piloto do turno (casado pelo nome no array drivers do split). Null quando não deu para casar.</summary>
    public string? SteamId { get; set; }

    // Variação de rating do piloto do turno nesta corrida (do array drivers; null nas corridas preenchidas depois).
    public int? DriverRankAdjustment { get; set; }
    public int? SafetyRankAdjustment { get; set; }
}
