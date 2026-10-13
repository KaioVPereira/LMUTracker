namespace LmuTracker.Application.Lmu;

// Perfil do piloto: estatísticas agregadas das corridas que o Pitwall guardou. Pilotos pelo id interno.

/// <summary>
/// Rank numa escala contínua para o gráfico: Bronze 1 = 0…100, Bronze 2 = 100…200, …, Silver 1 = 300…
/// (título × 300 + (tier − 1) × 100 + progresso). Null quando o RaceOS não mandou o rank.
/// </summary>
public record RankPointDto(
    DateTime At, string EventId, string? Title, string? Track,
    string? DriverRankTitle, int? DriverRankTier, int? DriverRankProgress, int? DriverScore, int? DriverRankAdjustment,
    string? SafetyRankTitle, int? SafetyRankTier, int? SafetyRankProgress, int? SafetyScore, int? SafetyRankAdjustment);

public record ProfileSummaryDto(
    int Races, int Wins, int Podiums, int Top5, int Poles, int Dnfs, int Finished,
    double? AvgPositionsGained, double? AvgClassFinish, double? AvgQualiClassPosition,
    int RacesWithIncidents, double? IncidentsPerRace, int Penalties, int CleanRaces, int LapsCompleted,
    double? PaceConsistency, int TopSplitRaces);

public record TrackStatDto(
    string Track, string? CarClass, int Races, int Wins, int Podiums, double? AvgClassFinish, int? BestClassFinish,
    double? BestLap, double? IncidentsPerRace);

public record CarStatDto(
    string Car, string? CarClass, int Races, int Wins, int Podiums, int Dnfs, double? AvgClassFinish, int? BestClassFinish);

public record RivalDto(
    Guid? DriverId, string Name, string? Nationality, int SharedRaces, int AheadOfThem, int BehindThem, DateTime LastRaced);

public record AchievementDto(string Key, string Title, string Description, bool Achieved, DateTime? At, string? EventId,
    int? Progress, int? Goal);

public record DriverProfileStatsDto(
    Guid DriverId,
    ProfileSummaryDto Summary,
    IReadOnlyList<RankPointDto> RankHistory,
    IReadOnlyList<TrackStatDto> Tracks,
    IReadOnlyList<CarStatDto> Cars,
    IReadOnlyList<RivalDto> Rivals,
    IReadOnlyList<AchievementDto> Achievements,
    DateTime GeneratedAt);

/// <summary>Uma corrida que os dois disputaram no mesmo split.</summary>
public record HeadToHeadRaceDto(
    string EventId, string? Title, string? Track, DateTime StartsAt, int SplitNo,
    string? MyClass, int? MyPosition, int? MyClassPosition, double? MyBestLap, string? MyStatus,
    string? TheirClass, int? TheirPosition, int? TheirClassPosition, double? TheirBestLap, string? TheirStatus);

public record HeadToHeadSideDto(
    Guid DriverId, string Name, string? Nationality, string? DriverRankTitle, int? DriverRankTier,
    string? SafetyRankTitle, int? SafetyRankTier, int Races, int Wins, int Podiums);

public record HeadToHeadDto(
    HeadToHeadSideDto Me, HeadToHeadSideDto Them,
    int Shared, int MeAhead, int ThemAhead, int SameClassRaces, double? AvgBestLapGap,
    IReadOnlyList<HeadToHeadRaceDto> Races);

public interface IDriverStatsService
{
    Task<DriverProfileStatsDto?> GetAsync(Guid driverId, CancellationToken ct = default);
    Task<HeadToHeadDto?> HeadToHeadAsync(Guid driverId, Guid otherId, CancellationToken ct = default);
}
