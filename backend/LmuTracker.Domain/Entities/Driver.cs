using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>
/// Índice de pilotos conhecidos, chaveado pelo SteamID. Alimentado pela varredura: toda vez que um piloto
/// aparece numa corrida, guardamos/atualizamos aqui o último retrato público dele (rank, tier, badge).
/// É o que permite, quando alguém vincula a conta, mostrar o perfil mesmo sem o token pessoal dele —
/// só com a visão pública (sem o Elo exato, que só vem do próprio piloto autenticado).
/// </summary>
public class Driver : Entity
{
    /// <summary>SteamID64 — chave natural do piloto.</summary>
    public string SteamId { get; set; } = null!;

    public string? RaceOsPlayerId { get; set; }
    public string Name { get; set; } = null!;
    public string? Nationality { get; set; }
    public string? Badge { get; set; }

    // ---------- Último retrato público do rating ----------
    public string? DriverRankTitle { get; set; }   // Bronze/Silver/Gold/Platinum
    public int? DriverRankTier { get; set; }
    public string? SafetyRankTitle { get; set; }
    public int? SafetyRankTier { get; set; }

    /// <summary>Quando esse retrato foi visto pela última vez numa corrida.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
