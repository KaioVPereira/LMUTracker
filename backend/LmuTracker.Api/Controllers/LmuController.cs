using LmuTracker.Application.Lmu;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LmuTracker.Api.Controllers;

[Authorize]
[Route("api/lmu")]
public class LmuController : ApiControllerBase
{
    private readonly ILmuQueryService _lmu;
    private readonly ISearchService _search;
    private readonly IHomeService _home;
    private readonly IDriverStatsService _stats;

    public LmuController(ILmuQueryService lmu, ISearchService search, IHomeService home, IDriverStatsService stats)
    {
        _lmu = lmu;
        _search = search;
        _home = home;
        _stats = stats;
    }

    /// <summary>Estatísticas do perfil: resumo, evolução do rank, pistas, carros, rivais e conquistas.</summary>
    [HttpGet("drivers/{driverId:guid}/stats")]
    public async Task<ActionResult<DriverProfileStatsDto>> DriverStats(Guid driverId, CancellationToken ct)
        => await _stats.GetAsync(driverId, ct) is { } s ? Ok(s) : NotFound();

    /// <summary>Comparação direta: as corridas que os dois disputaram no mesmo split.</summary>
    [HttpGet("drivers/{driverId:guid}/vs/{otherId:guid}")]
    public async Task<ActionResult<HeadToHeadDto>> HeadToHead(Guid driverId, Guid otherId, CancellationToken ct)
        => await _stats.HeadToHeadAsync(driverId, otherId, ct) is { } h ? Ok(h) : NotFound();

    /// <summary>Destaques da tela inicial (recordes, vencedores, saldo de DR, chegadas apertadas). Cache de 60 s.</summary>
    [HttpGet("home")]
    public async Task<ActionResult<HomeDto>> Home(CancellationToken ct)
        => Ok(await _home.GetAsync(ct));

    /// <summary>Pistas com corrida guardada (filtro da lista de corridas).</summary>
    [HttpGet("tracks")]
    public async Task<ActionResult<IReadOnlyList<SearchTrackDto>>> Tracks(CancellationToken ct)
        => Ok(await _search.ListTracksAsync(ct));

    /// <summary>Busca global (paleta Ctrl+K): pilotos, pistas, carros e corridas.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<SearchResultsDto>> Search([FromQuery] string? q, CancellationToken ct)
        => Ok(await _search.SearchAsync(q, ct));

    /// <summary>Informações gerais do LMU para o dashboard (agenda e o que o scanner já juntou).</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<OverviewDto>> Overview(CancellationToken ct)
        => Ok(await _lmu.GetOverviewAsync(ct));

    /// <summary>Painel do piloto vinculado (só dados do próprio usuário).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<MyDriverDto>> Me(CancellationToken ct)
        => Ok(await _lmu.GetMyDriverAsync(UserId, ct));

    /// <summary>
    /// Corridas com resultado, mais recentes primeiro. tier: beginner|intermediate|advanced|weekly;
    /// track: nome da pista; car: modelo do carro.
    /// </summary>
    [HttpGet("races")]
    public async Task<ActionResult<PagedDto<RaceListItemDto>>> Races(
        [FromQuery] string? tier, [FromQuery] string? track, [FromQuery] string? car,
        [FromQuery(Name = "class")] string? carClass, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await _lmu.ListRacesAsync(tier, track, car, page, ct, carClass,
            from?.ToUniversalTime(), to?.ToUniversalTime()));

    /// <summary>Volta a volta do split inteiro (posições, ritmo, pits e pneus).</summary>
    [HttpGet("races/{eventId}/splits/{splitNo:int}/laps")]
    public async Task<ActionResult<SplitLapsDto>> SplitLaps(string eventId, int splitNo, CancellationToken ct)
        => await _lmu.GetSplitLapsAsync(eventId, splitNo, ct) is { } laps ? Ok(laps) : NotFound();

    /// <summary>Penalidades e decisões dos comissários do split.</summary>
    [HttpGet("races/{eventId}/splits/{splitNo:int}/events")]
    public async Task<ActionResult<SplitEventsDto>> SplitEvents(string eventId, int splitNo, CancellationToken ct)
        => await _lmu.GetSplitEventsAsync(eventId, splitNo, ct) is { } ev ? Ok(ev) : NotFound();

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
