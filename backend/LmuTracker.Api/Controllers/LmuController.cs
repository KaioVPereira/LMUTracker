using LmuTracker.Application.Lmu;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace LmuTracker.Api.Controllers;

/// <summary>
/// Leitura pública (perfis, corridas, pistas, rankings — sem login); "meu piloto" e "seguir" exigem login.
/// As leituras públicas têm cache de saída de 30 s para quem não está logado (requisição sem Authorization).
/// </summary>
[Route("api/lmu")]
public class LmuController : ApiControllerBase
{
    private readonly ILmuQueryService _lmu;
    private readonly ISearchService _search;
    private readonly IHomeService _home;
    private readonly IDriverStatsService _stats;
    private readonly IExtrasService _extras;
    private readonly IRankingsService _rankings;

    public LmuController(ILmuQueryService lmu, ISearchService search, IHomeService home, IDriverStatsService stats,
        IExtrasService extras, IRankingsService rankings)
    {
        _rankings = rankings;
        _lmu = lmu;
        _search = search;
        _home = home;
        _stats = stats;
        _extras = extras;
    }

    /// <summary>Ranking: melhor volta de cada piloto por pista, classe, carro e tipo de setup (fixed|open).</summary>
    [HttpGet("leaderboard")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<LeaderboardDto>> Leaderboard(
        [FromQuery] string? track, [FromQuery(Name = "class")] string? carClass, [FromQuery] string? car,
        [FromQuery] string? setup, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _extras.LeaderboardAsync(track, carClass, car, setup, take, ct));

    /// <summary>
    /// Rankings de pilotos: dr | sr | wins | risers | clean | active, por classe e período (7d|30d|all).
    /// <paramref name="me"/> = id do piloto a destacar (devolve a linha dele mesmo fora do topo).
    /// </summary>
    [HttpGet("rankings/drivers/{kind}")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<RankingDto>> DriverRanking(
        string kind, [FromQuery(Name = "class")] string? carClass, [FromQuery] string? period,
        [FromQuery] Guid? me, [FromQuery] int take = 50, CancellationToken ct = default)
        => await _rankings.DriversAsync(kind, carClass, period, take, me, ct) is { } r ? Ok(r) : NotFound();

    /// <summary>Ranking de carros: vitórias na classe, taxa de vitória e pódios, por classe e período.</summary>
    [HttpGet("rankings/cars")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<CarRankingDto>> CarRanking(
        [FromQuery(Name = "class")] string? carClass, [FromQuery] string? period, CancellationToken ct = default)
        => Ok(await _rankings.CarsAsync(carClass, period, ct));

    /// <summary>Página da pista: recordes por classe, carros mais rápidos, números gerais.</summary>
    [HttpGet("tracks/{track}")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<TrackDetailDto>> Track(string track, CancellationToken ct)
        => await _extras.TrackAsync(track, ct) is { } t ? Ok(t) : NotFound();

    [HttpGet("cars")]
    [OutputCache(Duration = 60)]
    public async Task<ActionResult<IReadOnlyList<CarListItemDto>>> Cars(CancellationToken ct)
        => Ok(await _extras.CarsAsync(ct));

    /// <summary>Página do carro: números gerais, pistas, melhores pilotos.</summary>
    [HttpGet("cars/{car}")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<CarDetailDto>> Car(string car, CancellationToken ct)
        => await _extras.CarAsync(car, ct) is { } c ? Ok(c) : NotFound();

    /// <summary>Pilotos que o usuário segue, com a corrida mais recente de cada um.</summary>
    [Authorize]
    [HttpGet("follows")]
    public async Task<ActionResult<IReadOnlyList<FollowedDriverDto>>> Follows(CancellationToken ct)
        => Ok(await _extras.FollowsAsync(UserId, ct));

    [Authorize]
    [HttpGet("follows/ids")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> FollowIds(CancellationToken ct)
        => Ok(await _extras.FollowIdsAsync(UserId, ct));

    [Authorize]
    [HttpPut("follows/{driverId:guid}")]
    public async Task<IActionResult> Follow(Guid driverId, CancellationToken ct)
        => await _extras.FollowAsync(UserId, driverId, ct) ? NoContent() : NotFound();

    [Authorize]
    [HttpDelete("follows/{driverId:guid}")]
    public async Task<IActionResult> Unfollow(Guid driverId, CancellationToken ct)
    {
        await _extras.UnfollowAsync(UserId, driverId, ct);
        return NoContent();
    }

    /// <summary>Estatísticas do perfil: resumo, evolução do rank, pistas, carros, rivais e conquistas.</summary>
    [HttpGet("drivers/{driverId:guid}/stats")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<DriverProfileStatsDto>> DriverStats(Guid driverId, CancellationToken ct)
        => await _stats.GetAsync(driverId, ct) is { } s ? Ok(s) : NotFound();

    /// <summary>Comparação direta: as corridas que os dois disputaram no mesmo split.</summary>
    [HttpGet("drivers/{driverId:guid}/vs/{otherId:guid}")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<HeadToHeadDto>> HeadToHead(Guid driverId, Guid otherId, CancellationToken ct)
        => await _stats.HeadToHeadAsync(driverId, otherId, ct) is { } h ? Ok(h) : NotFound();

    /// <summary>Destaques da tela inicial (recordes, vencedores, saldo de DR, chegadas apertadas). Cache de 60 s.</summary>
    [HttpGet("home")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<HomeDto>> Home(CancellationToken ct)
        => Ok(await _home.GetAsync(ct));

    /// <summary>Pistas com corrida guardada (filtro da lista de corridas).</summary>
    [HttpGet("tracks")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<IReadOnlyList<SearchTrackDto>>> Tracks(CancellationToken ct)
        => Ok(await _search.ListTracksAsync(ct));

    /// <summary>Busca global (paleta Ctrl+K): pilotos, pistas, carros e corridas.</summary>
    [HttpGet("search")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<SearchResultsDto>> Search([FromQuery] string? q, CancellationToken ct)
        => Ok(await _search.SearchAsync(q, ct));

    /// <summary>Informações gerais do LMU para o dashboard (agenda e o que o scanner já juntou).</summary>
    [HttpGet("overview")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<OverviewDto>> Overview(CancellationToken ct)
        => Ok(await _lmu.GetOverviewAsync(ct));

    /// <summary>Painel do piloto vinculado (só dados do próprio usuário).</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MyDriverDto>> Me(CancellationToken ct)
        => Ok(await _lmu.GetMyDriverAsync(UserId, ct));

    /// <summary>
    /// Corridas com resultado, mais recentes primeiro. tier: beginner|intermediate|advanced|weekly;
    /// track: nome da pista; car: modelo do carro.
    /// </summary>
    [HttpGet("races")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<PagedDto<RaceListItemDto>>> Races(
        [FromQuery] string? tier, [FromQuery] string? track, [FromQuery] string? car,
        [FromQuery(Name = "class")] string? carClass, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, CancellationToken ct = default)
        => Ok(await _lmu.ListRacesAsync(tier, track, car, page, ct, carClass,
            from?.ToUniversalTime(), to?.ToUniversalTime()));

    /// <summary>Volta a volta do split inteiro (posições, ritmo, pits e pneus).</summary>
    [HttpGet("races/{eventId}/splits/{splitNo:int}/laps")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<SplitLapsDto>> SplitLaps(string eventId, int splitNo, CancellationToken ct)
        => await _lmu.GetSplitLapsAsync(eventId, splitNo, ct) is { } laps ? Ok(laps) : NotFound();

    /// <summary>Penalidades e decisões dos comissários do split.</summary>
    [HttpGet("races/{eventId}/splits/{splitNo:int}/events")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<SplitEventsDto>> SplitEvents(string eventId, int splitNo, CancellationToken ct)
        => await _lmu.GetSplitEventsAsync(eventId, splitNo, ct) is { } ev ? Ok(ev) : NotFound();

    [HttpGet("races/{eventId}")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<RaceDetailDto>> Race(string eventId, CancellationToken ct)
        => await _lmu.GetRaceAsync(eventId, ct) is { } race ? Ok(race) : NotFound();

    [HttpGet("races/{eventId}/splits/{splitNo:int}")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<SplitResultsDto>> Split(string eventId, int splitNo, CancellationToken ct)
        => await _lmu.GetSplitAsync(eventId, splitNo, ct) is { } split ? Ok(split) : NotFound();

    /// <summary>Um piloto numa corrida: linha de resultado + volta a volta.</summary>
    [HttpGet("results/{resultId:guid}")]
    [OutputCache(Duration = 30)]
    public async Task<ActionResult<ResultDetailDto>> Result(Guid resultId, CancellationToken ct)
        => await _lmu.GetResultAsync(resultId, ct) is { } result ? Ok(result) : NotFound();

    [HttpGet("drivers/{driverId:guid}")]
    public async Task<ActionResult<DriverProfileDto>> Driver(
        Guid driverId, [FromQuery] int page = 1, CancellationToken ct = default)
        => await _lmu.GetDriverAsync(driverId, UserId, page, ct) is { } driver ? Ok(driver) : NotFound();
}
