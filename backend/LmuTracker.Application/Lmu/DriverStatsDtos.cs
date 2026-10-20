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

// ---------- comparação por pista e classe ----------

/// <summary>A melhor volta do piloto no recorte, com os setores (null quando a corrida não tem o volta a volta).</summary>
public record CompareLapDto(double LapTime, double? S1, double? S2, double? S3, Guid ResultId, string EventId,
    DateTime At, string? Car, int Lap);

/// <summary>Uma corrida do piloto no recorte (pista + classe).</summary>
public record CompareRaceDto(
    Guid ResultId, string EventId, string? Title, DateTime StartsAt, int SplitNo, string? Car,
    int? ClassGridPos, int? ClassPosition, string? FinishStatus, double? BestLap, double? Pace, int? Incidents);

/// <summary>Um lado do comparativo. Pace = mediana das voltas limpas (média entre as corridas).</summary>
public record CompareSideDto(
    Guid DriverId, string Name, string? Nationality, string? DriverRankTitle, int? DriverRankTier,
    string? SafetyRankTitle, int? SafetyRankTier,
    int Starts, int Wins, int Podiums, int Dnfs, double? AvgClassFinish, int? BestClassFinish, double? AvgClassGrid,
    double? AvgPositionsGained, double? IncidentsPerRace,
    CompareLapDto? BestLap, double? BestS1, double? BestS2, double? BestS3, double? Pace, double? Consistency,
    string? MainCar, IReadOnlyList<CompareRaceDto> Races);

public record CompareDto(string Track, string CarClass, CompareSideDto Me, CompareSideDto Them,
    IReadOnlyList<HeadToHeadRaceDto> Shared);

/// <summary>Sugestão de adversário para o comparativo: quem se destaca na pista e classe.</summary>
public record CompareSuggestionDto(Guid DriverId, string Name, string? Nationality, double Value, int Races);

public record CompareSuggestionsDto(
    string Track, string CarClass,
    IReadOnlyList<CompareSuggestionDto> MostWins,
    IReadOnlyList<CompareSuggestionDto> Fastest,
    IReadOnlyList<CompareSuggestionDto> MostRaces);

public interface IDriverStatsService
{
    Task<DriverProfileStatsDto?> GetAsync(Guid driverId, CancellationToken ct = default);
    Task<HeadToHeadDto?> HeadToHeadAsync(Guid driverId, Guid otherId, CancellationToken ct = default);
    /// <summary>Os dois numa pista e classe (classe: hyper | lmp2 | lmp3 | gt3).</summary>
    Task<CompareDto?> CompareAsync(Guid driverId, Guid otherId, string track, string carClass, CancellationToken ct = default);
    Task<CompareSuggestionsDto> SuggestAsync(string track, string carClass, CancellationToken ct = default);
}
