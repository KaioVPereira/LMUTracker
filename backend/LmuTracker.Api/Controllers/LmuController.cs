using LmuTracker.Application.Lmu;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LmuTracker.Api.Controllers;

[Authorize]
[Route("api/lmu")]
public class LmuController : ApiControllerBase
{
    private readonly ILmuQueryService _lmu;

    public LmuController(ILmuQueryService lmu)
    {
        _lmu = lmu;
    }

    /// <summary>Informações gerais do LMU para o dashboard (agenda e o que o scanner já juntou).</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<OverviewDto>> Overview(CancellationToken ct)
        => Ok(await _lmu.GetOverviewAsync(ct));

    /// <summary>Painel do piloto vinculado (só dados do próprio usuário).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<MyDriverDto>> Me(CancellationToken ct)
        => Ok(await _lmu.GetMyDriverAsync(UserId, ct));
}
