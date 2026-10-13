namespace LmuTracker.Infrastructure.RaceOs;

/// <summary>Configuração do login Steam headless (conta de serviço) usado no servidor.</summary>
public class SteamKitOptions
{
    public const string Section = "RaceOs:SteamKit";

    /// <summary>Login (account name) da conta de serviço. A senha só é usada no bootstrap interativo.</summary>
    public string Username { get; set; } = "";

    /// <summary>appid do Le Mans Ultimate.</summary>
    public uint AppId { get; set; } = 2399420;

    /// <summary>
    /// Identity do ticket Web API. O LMU-Assister usa string vazia e o RaceOS aceita; deixo configurável
    /// caso precise ajustar.
    /// </summary>
    public string WebApiIdentity { get; set; } = "";

    /// <summary>
    /// Arquivo onde fica a sessão (refresh token + guard data) depois do bootstrap. É SENSÍVEL — num
    /// servidor, restringir permissões e manter fora do versionamento (usar secret store de preferência).
    /// </summary>
    public string SessionStorePath { get; set; } = "steam-session.json";
}
