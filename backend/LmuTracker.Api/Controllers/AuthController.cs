using LmuTracker.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LmuTracker.Api.Controllers;

[Route("api/auth")]
[EnableRateLimiting("auth")] // login e cadastro são a superfície de força-bruta
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
        => Ok(await _auth.RegisterAsync(request, ct));

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    [Authorize]
    [DisableRateLimiting] // chamado a cada carregamento de página; não é alvo de força-bruta
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var me = await _auth.GetMeAsync(UserId, ct);
        return me is null ? Unauthorized() : Ok(me);
    }
}
