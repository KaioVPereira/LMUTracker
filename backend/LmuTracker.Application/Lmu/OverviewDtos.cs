namespace LmuTracker.Application.Lmu;

/// <summary>Uma sessão do evento. Minutos; horário no jogo em minutos desde 0h; céu 0 (limpo) … 10 (tempestade).</summary>
public record SessionInfoDto(
    string Kind, double Minutes, double WaitMinutes, double TimeOfDay, int? Sky, int? TemperatureC, int? RainChance);

/// <summary>O que a configuração do evento diz (só existe para corridas vistas na agenda a partir do scanner v3).</summary>
public record EventInfoDto(
    IReadOnlyList<SessionInfoDto> Sessions, bool? FixedSetup, int? MaxPlayers,
    double? TyreWear = null, double? FuelUse = null, int? Damage = null, int? TireSets = null,
    string? TrackLimits = null, bool? TyreWarmers = null);

/// <summary>Corrida da agenda que ainda vai acontecer (ou está acontecendo).</summary>
public record UpcomingRaceDto(
    string EventId, string? Title, string? Track, string? Tier, string EventType,
    DateTime StartsAt, DateTime EndsAt, EventInfoDto? Info, LiveStatusDto? Live = null);

public record ClassCountDto(string Class, int Count);

/// <summary>
/// Estado do evento em andamento segundo o RaceOS (event/overview, relido pelo scanner a cada ~10 min). Só agregados:
/// status, splits abertos, inscritos por classe, tamanho de cada split, servidores no ar. Não há posições — a
/// cronometragem só existe nos servidores da corrida.
/// </summary>
public record LiveStatusDto(
    string? Status, int? Splits, int? Registered, IReadOnlyList<ClassCountDto> ByClass, IReadOnlyList<int> SplitSizes,
    int? Servers, int? ServersRunning, bool? Full, DateTime? LastUpdatesAt, DateTime CheckedAt);

/// <summary>Corrida já encerrada cujos resultados foram baixados.</summary>
public record RecentRaceDto(
    string EventId, string? Title, string? Track, string? Tier,
    DateTime StartsAt, int Splits, int Drivers, string? Classes);

/// <summary>Visão geral do LMU para o dashboard: só agregados e dados de evento, sem dados de pilotos.</summary>
public record OverviewDto(
    int EventsTracked,
    int RacesFetched,
    int ResultsStored,
    int DriversIndexed,
    DateTime? LastActivityAt,
    IReadOnlyList<UpcomingRaceDto> Upcoming,
    IReadOnlyList<RecentRaceDto> Recent);

/// <summary>
/// Um inscrito no evento em andamento (piloto ou carro de equipe), como o RaceOS manda: DR/SR atuais (rank e tier),
/// carro e pintura. DriverId liga ao perfil do Pitwall (pelo SteamID, ou pelo nome quando é único no índice); sem rank
/// do RaceOS, vale o último visto pelo Pitwall.
/// </summary>
public record LiveEntryDto(
    string Name, string? Nationality, string? CarClass, string? Car, string? CarNumber, string? Team,
    IReadOnlyList<LiveMemberDto> Members, Guid? DriverId,
    string? DriverRankTitle, int? DriverRankTier, string? SafetyRankTitle, int? SafetyRankTier,
    string? Livery = null, string? Badge = null);

/// <summary>
/// Corrida em andamento em que o piloto está inscrito ("Correndo agora"). Team = equipe, em evento de equipe.
/// </summary>
public record LiveSpotDto(string EventId, string? Title, string? Track, string? Team, DateTime StartsAt, DateTime EndsAt);

/// <summary>Quem está inscrito nas corridas em andamento, por piloto do Pitwall (ver LiveRosterService).</summary>
public interface ILiveRosterService
{
    Task<LiveSpotDto?> ForDriverAsync(Guid driverId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, LiveSpotDto>> AllAsync(CancellationToken ct = default);
}

/// <summary>Piloto de uma equipe inscrita. DriverId/rank vêm do índice do Pitwall (pelo nome, quando é único).</summary>
public record LiveMemberDto(string Name, Guid? DriverId, string? DriverRankTitle, int? DriverRankTier);

/// <summary>Pilotos de um split (SplitNo null = inscrição ainda sem split definido).</summary>
public record LiveSplitDto(int? SplitNo, IReadOnlyList<LiveEntryDto> Entries);

/// <summary>
/// Página do evento em andamento: agenda/estado (o mesmo da home), pilotos por split e se o resultado já saiu.
/// EntriesSource: "splits" (pilotos já distribuídos), "entries" (lista de inscritos) ou null (ainda sem lista).
/// </summary>
public record LiveEventDto(
    UpcomingRaceDto Race, bool HasResult, string? EntriesSource, DateTime? EntriesAt, IReadOnlyList<LiveSplitDto> Splits,
    bool TeamEvent = false);
