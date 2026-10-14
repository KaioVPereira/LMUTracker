namespace LmuTracker.Application.Lmu;

// Rankings, páginas de pista e de carro, e pilotos seguidos. Pilotos pelo id interno, nunca pelo SteamID.

/// <summary>Uma linha do ranking: a melhor volta de cada piloto no recorte (pista, classe, carro, setup).</summary>
public record LeaderboardRowDto(
    int Rank, Guid? DriverId, string Name, string? Nationality, string? Car, string? CarClass,
    double BestLap, double? GapToFirst, string EventId, DateTime SetAt, int SplitNo);

public record LeaderboardDto(
    string? Track, string? CarClass, string? Car, string Setup, int Drivers, IReadOnlyList<LeaderboardRowDto> Rows);

public record TrackRecordDto(
    string? CarClass, bool FixedSetup, double BestLap, Guid? DriverId, string DriverName, string? Car,
    DateTime SetAt, string EventId);

/// <summary>Ritmo de um carro numa pista: melhor volta e média das 10 melhores voltas (uma por piloto).</summary>
public record CarPaceDto(string Car, string? CarClass, int Starts, double? BestLap, double? Top10Avg);

public record TrackDetailDto(
    string Track, int Races, int Starts, int Drivers, DateTime? FirstRace, DateTime? LastRace,
    IReadOnlyList<TrackRecordDto> Records, IReadOnlyList<CarPaceDto> Cars, IReadOnlyList<ClassShareDto> Classes);

public record CarTrackDto(string Track, int Starts, int Wins, double? BestLap, Guid? BestDriverId, string? BestDriverName);

public record CarDriverDto(Guid? DriverId, string Name, string? Nationality, int Starts, int Wins, int Podiums);

public record CarDetailDto(
    string Car, string? CarClass, int Starts, int Drivers, int Wins, int Podiums, int Dnfs, double? AvgClassFinish,
    IReadOnlyList<CarTrackDto> Tracks, IReadOnlyList<CarDriverDto> TopDrivers);

public record CarListItemDto(string Car, string? CarClass, int Starts);

public record LastRaceDto(
    Guid ResultId, string EventId, string? Title, string? Track, DateTime StartsAt, int SplitNo,
    string? CarClass, int? ClassPosition, string? FinishStatus);

public record FollowedDriverDto(
    Guid DriverId, string Name, string? Nationality, string? DriverRankTitle, int? DriverRankTier,
    string? SafetyRankTitle, int? SafetyRankTier, DateTime FollowedAt, int RacesLast7Days, LastRaceDto? LastRace);

public interface IExtrasService
{
    /// <summary><paramref name="setup"/>: "fixed", "open" ou null (todos). Classe: hyper|lmp2|lmp3|gt3.</summary>
    Task<LeaderboardDto> LeaderboardAsync(string? track, string? carClass, string? car, string? setup, int take,
        CancellationToken ct = default);
    Task<TrackDetailDto?> TrackAsync(string track, CancellationToken ct = default);
    Task<CarDetailDto?> CarAsync(string car, CancellationToken ct = default);
    Task<IReadOnlyList<CarListItemDto>> CarsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<FollowedDriverDto>> FollowsAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> FollowIdsAsync(Guid userId, CancellationToken ct = default);
    Task<bool> FollowAsync(Guid userId, Guid driverId, CancellationToken ct = default);
    Task UnfollowAsync(Guid userId, Guid driverId, CancellationToken ct = default);
}
