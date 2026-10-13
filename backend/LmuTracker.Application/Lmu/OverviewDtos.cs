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
    DateTime StartsAt, DateTime EndsAt, EventInfoDto? Info);

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
