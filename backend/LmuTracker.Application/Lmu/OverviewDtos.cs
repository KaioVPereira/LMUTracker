namespace LmuTracker.Application.Lmu;

/// <summary>Corrida da agenda que ainda vai acontecer (ou está acontecendo).</summary>
public record UpcomingRaceDto(
    string EventId, string? Title, string? Track, string? Tier, string EventType,
    DateTime StartsAt, DateTime EndsAt);

/// <summary>Corrida já encerrada cujos resultados foram baixados.</summary>
public record RecentRaceDto(
    string EventId, string? Title, string? Track, string? Tier,
    DateTime StartsAt, int Splits, int Drivers);

/// <summary>Visão geral do LMU para o dashboard: só agregados e dados de evento, sem dados de pilotos.</summary>
public record OverviewDto(
    int EventsTracked,
    int RacesFetched,
    int ResultsStored,
    int DriversIndexed,
    DateTime? LastActivityAt,
    IReadOnlyList<UpcomingRaceDto> Upcoming,
    IReadOnlyList<RecentRaceDto> Recent);
