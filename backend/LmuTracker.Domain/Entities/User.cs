using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>
/// Conta do site (login e senha, igual ao GameTracker). O vínculo com o Le Mans Ultimate é opcional:
/// enquanto <see cref="SteamId"/> for null, a pessoa está logada mas ainda não vinculou a conta —
/// e o painel mostra só as informações gerais do jogo com o chamado para vincular.
/// </summary>
public class User : Entity
{
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string DisplayName { get; set; } = null!;

    // ---------- Vínculo com o LMU (via Steam) ----------

    /// <summary>SteamID64 do piloto, provado pelo login Steam (OpenID). Null = conta ainda não vinculada.</summary>
    public string? SteamId { get; set; }

    /// <summary>Id do piloto no RaceOS, descoberto quando ele aparece em alguma corrida varrida. Null até lá.</summary>
    public string? RaceOsPlayerId { get; set; }

    /// <summary>Quando o vínculo Steam foi concluído. Null enquanto não vinculou.</summary>
    public DateTime? LinkedAt { get; set; }
}
