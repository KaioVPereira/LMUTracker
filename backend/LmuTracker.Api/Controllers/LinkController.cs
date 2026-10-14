using LmuTracker.Application.Auth;
using LmuTracker.Application.Linking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LmuTracker.Api.Controllers;

[Authorize]
[Route("api/link")]
[EnableRateLimiting("auth")]
public class LinkController : ApiControllerBase
{
    private readonly ISteamLinkService _steam;

    public LinkController(ISteamLinkService steam)
    {
        _steam = steam;
    }

    /// <summary>URL do "Entrar com a Steam"; o front redireciona o navegador para ela.</summary>
    [HttpGet("steam/start")]
    public ActionResult<SteamLinkStartDto> Start([FromQuery] bool app = false) => Ok(new SteamLinkStartDto(_steam.BuildLoginUrl(app)));

    /// <summary>Recebe os parâmetros openid.* da página de retorno e conclui o vínculo.</summary>
    [HttpPost("steam/verify")]
    public async Task<ActionResult<UserDto>> Verify(SteamLinkVerifyRequest request, CancellationToken ct)
        => Ok(await _steam.VerifyAndLinkAsync(UserId, request.Params ?? new(), ct));

    [HttpDelete("steam")]
    public async Task<ActionResult<UserDto>> Unlink(CancellationToken ct)
        => Ok(await _steam.UnlinkAsync(UserId, ct));
}
