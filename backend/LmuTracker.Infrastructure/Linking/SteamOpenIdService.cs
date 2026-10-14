using System.Text.RegularExpressions;
using LmuTracker.Application.Auth;
using LmuTracker.Application.Common;
using LmuTracker.Application.Linking;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LmuTracker.Infrastructure.Linking;

/// <summary>
/// "Entrar com a Steam" (OpenID 2.0). Só prova que a pessoa é dona do SteamID — não dá acesso a nada do
/// jogo. Com o SteamID provado, o site acha o piloto nas corridas que o scanner já guardou.
/// </summary>
public sealed partial class SteamOpenIdService : ISteamLinkService
{
    private const string Endpoint = "https://steamcommunity.com/openid/login";
    private const string Ns = "http://specs.openid.net/auth/2.0";
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";

    [GeneratedRegex(@"^https://steamcommunity\.com/openid/id/(7656\d{13})$")]
    private static partial Regex ClaimedIdRegex();

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly SiteOptions _site;
    private readonly ILogger<SteamOpenIdService> _log;

    public SteamOpenIdService(
        AppDbContext db, IHttpClientFactory http, IOptions<SiteOptions> site, ILogger<SteamOpenIdService> log)
    {
        _db = db;
        _http = http;
        _site = site.Value;
        _log = log;
    }

    public string BuildLoginUrl(bool fromApp = false)
    {
        var q = new Dictionary<string, string>
        {
            ["openid.ns"] = Ns,
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = fromApp ? _site.AppSteamReturnUrl : _site.SteamReturnUrl,
            ["openid.realm"] = fromApp ? _site.AppRealm : _site.Realm,
            ["openid.identity"] = IdentifierSelect,
            ["openid.claimed_id"] = IdentifierSelect,
        };
        return Endpoint + "?" + string.Join("&", q.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<UserDto> VerifyAndLinkAsync(
        Guid userId, IReadOnlyDictionary<string, string> p, CancellationToken ct = default)
    {
        string Get(string k) => p.TryGetValue(k, out var v) ? v : "";

        // 1) A resposta tem que ser um id_res da Steam, voltando para o NOSSO endereço de retorno
        //    (impede reaproveitar uma resposta emitida para outro site).
        if (Get("openid.mode") != "id_res")
            throw new AppException("O login na Steam foi cancelado ou não foi concluído.");
        if (Get("openid.op_endpoint") != Endpoint)
            throw new AppException("Resposta de login que não veio da Steam.");
        var returnTo = Get("openid.return_to");
        if (!returnTo.StartsWith(_site.SteamReturnUrl, StringComparison.Ordinal)
            && !returnTo.StartsWith(_site.AppSteamReturnUrl, StringComparison.Ordinal))
            throw new AppException("Resposta de login emitida para outro endereço.");

        var claimed = Get("openid.claimed_id");
        var m = ClaimedIdRegex().Match(claimed);
        if (!m.Success || Get("openid.identity") != claimed)
            throw new AppException("A Steam não devolveu um SteamID válido.");
        var steamId = m.Groups[1].Value;

        // 2) Confirma a assinatura com a própria Steam.
        if (!await CheckAuthenticationAsync(p, ct))
            throw new AppException("A Steam não confirmou este login. Tente vincular de novo.");

        // 3) Grava o vínculo (um SteamID só pode estar em um usuário).
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new AppException("Usuário não encontrado.", 404);

        if (await _db.Users.AnyAsync(u => u.SteamId == steamId && u.Id != userId, ct))
            throw new AppException("Esta conta Steam já está vinculada a outro usuário.", 409);

        user.SteamId = steamId;
        user.LinkedAt = DateTime.UtcNow;
        user.RaceOsPlayerId = await _db.Drivers
            .Where(d => d.SteamId == steamId)
            .Select(d => d.RaceOsPlayerId)
            .FirstOrDefaultAsync(ct);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Usuário {User} vinculou a Steam.", userId);
        return await UserDtoFactory.BuildAsync(_db, user, ct);
    }

    public async Task<UserDto> UnlinkAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new AppException("Usuário não encontrado.", 404);

        user.SteamId = null;
        user.RaceOsPlayerId = null;
        user.LinkedAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await UserDtoFactory.BuildAsync(_db, user, ct);
    }

    private async Task<bool> CheckAuthenticationAsync(IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        var form = p.Where(kv => kv.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        form["openid.mode"] = "check_authentication";

        using var resp = await _http.CreateClient("steam-openid")
            .PostAsync(Endpoint, new FormUrlEncodedContent(form), ct);
        if (!resp.IsSuccessStatusCode) return false;

        var body = await resp.Content.ReadAsStringAsync(ct);
        return body.Split('\n').Any(l => l.Trim() == "is_valid:true");
    }

}
