using LmuTracker.Application.Auth;

namespace LmuTracker.Application.Linking;

public record SteamLinkStartDto(string Url);

/// <summary>Os parâmetros <c>openid.*</c> que a Steam devolveu na URL de retorno, repassados pelo front.</summary>
public record SteamLinkVerifyRequest(Dictionary<string, string> Params);

/// <summary>Vínculo da conta do site com a Steam via OpenID (prova de posse do SteamID, nada além disso).</summary>
public interface ISteamLinkService
{
    /// <summary>URL do "Entrar com a Steam" que devolve o navegador para a página de retorno do site.</summary>
    /// <param name="fromApp">true = volta para o app Android (https://localhost) em vez do site.</param>
    string BuildLoginUrl(bool fromApp = false);

    /// <summary>Valida a resposta OpenID com a própria Steam e grava o SteamID no usuário.</summary>
    Task<UserDto> VerifyAndLinkAsync(Guid userId, IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default);

    Task<UserDto> UnlinkAsync(Guid userId, CancellationToken ct = default);
}
