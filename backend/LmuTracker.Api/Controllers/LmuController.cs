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

    /// <summary>Corridas com resultado, mais recentes primeiro. tier: beginner|intermediate|advanced|weekly.</summary>
    [HttpGet("races")]
    public async Task<ActionResult<PagedDto<RaceListItemDto>>> Races(
        [FromQuery] string? tier, [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await _lmu.ListRacesAsync(tier, page, ct));

    [HttpGet("races/{eventId}")]
    public async Task<ActionResult<RaceDetailDto>> Race(string eventId, CancellationToken ct)
        => await _lmu.GetRaceAsync(eventId, ct) is { } race ? Ok(race) : NotFound();

    [HttpGet("races/{eventId}/splits/{splitNo:int}")]
    public async Task<ActionResult<SplitResultsDto>> Split(string eventId, int splitNo, CancellationToken ct)
        => await _lmu.GetSplitAsync(eventId, splitNo, ct) is { } split ? Ok(split) : NotFound();

    /// <summary>Um piloto numa corrida: linha de resultado + volta a volta.</summary>
    [HttpGet("results/{resultId:guid}")]
    public async Task<ActionResult<ResultDetailDto>> Result(Guid resultId, CancellationToken ct)
        => await _lmu.GetResultAsync(resultId, ct) is { } result ? Ok(result) : NotFound();

    [HttpGet("drivers/{driverId:guid}")]
    public async Task<ActionResult<DriverProfileDto>> Driver(
        Guid driverId, [FromQuery] int page = 1, CancellationToken ct = default)
        => await _lmu.GetDriverAsync(driverId, UserId, page, ct) is { } driver ? Ok(driver) : NotFound();
}
