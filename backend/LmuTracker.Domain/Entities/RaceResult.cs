using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>
/// Resultado de um piloto num split: a linha de classificação mais o que o RaceOS reporta de variação
/// de Driver Rank e Safety Rank naquela corrida. É por aqui que um usuário vinculado é reencontrado:
/// casa-se <see cref="SteamId"/> com o SteamID provado no login.
/// </summary>
public class RaceResult : Entity
{
    public Guid RaceSplitId { get; set; }
    public RaceSplit RaceSplit { get; set; } = null!;

    // ---------- Identidade do piloto ----------
    public string? SteamId { get; set; }
    public string? RaceOsPlayerId { get; set; }
    public string Name { get; set; } = null!;
    public string? Nationality { get; set; }
    public string? Badge { get; set; }

    // ---------- Carro / equipe ----------
    public string? CarClass { get; set; }
    public string? CarNumber { get; set; }
    public string? CarType { get; set; }
    public string? VehName { get; set; }
    public string? TeamName { get; set; }

    // ---------- Classificação ----------
    public int? GridPos { get; set; }
    public int? Position { get; set; }
    public int? ClassGridPos { get; set; }
    public int? ClassPosition { get; set; }
    public int Laps { get; set; }

    /// <summary>Melhor volta em segundos (null quando não marcou volta válida).</summary>
    public double? BestLapTime { get; set; }

    /// <summary>Tempo total em segundos (null quando não terminou).</summary>
    public double? FinishTime { get; set; }

    public string? FinishStatus { get; set; }
    public int Points { get; set; }

    // ---------- Variação de rating nesta corrida (o que a visão pública entrega de terceiros) ----------
    public int? DriverRankAdjustment { get; set; }
    public int? SafetyRankAdjustment { get; set; }
    public bool JokerPlayed { get; set; }
}
