using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LmuTracker.Infrastructure.RaceOs;

/// <summary>
/// Pega o ticket na API local do jogo (<c>GET 127.0.0.1:{porta}/rest/profile/getAuthSessionTicket</c>).
/// Exige o LMU aberto e logado na conta de serviço. É o modo de arranque; no servidor, trocar por SteamKit2.
/// </summary>
public sealed class LocalGameTicketProvider : ISteamTicketProvider
{
    private readonly IHttpClientFactory _factory;
    private readonly RaceOsOptions _opt;

    public LocalGameTicketProvider(IHttpClientFactory factory, IOptions<RaceOsOptions> opt)
    {
        _factory = factory;
        _opt = opt.Value;
    }

    public async Task<string> GetTicketAsync(CancellationToken ct = default)
    {
        var http = _factory.CreateClient("raceos-local");
        var url = $"http://127.0.0.1:{_opt.GamePort}/rest/profile/getAuthSessionTicket";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Ticket Steam indisponível (HTTP {(int)resp.StatusCode}). O LMU está aberto e logado na conta de serviço?");

        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
        if (doc.RootElement.TryGetProperty("authSessionTicket", out var t) && t.GetString() is { Length: > 0 } ticket)
            return ticket;

        throw new InvalidOperationException("Resposta do jogo não trouxe authSessionTicket.");
    }
}
