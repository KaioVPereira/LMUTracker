namespace LmuTracker.Infrastructure.RaceOs;

/// <summary>
/// Fornece o ticket de sessão Steam (hex) da conta de serviço para o LMU (appid 2399420). O RaceOS
/// troca esse ticket por um accessToken. Há duas formas: o jogo aberto (API local) ou, no servidor,
/// login headless via SteamKit2.
/// </summary>
public interface ISteamTicketProvider
{
    Task<string> GetTicketAsync(CancellationToken ct = default);
}
