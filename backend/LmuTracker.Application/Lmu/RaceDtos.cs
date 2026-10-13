namespace LmuTracker.Application.Lmu;

// Telas de corrida, split, piloto na corrida e perfil. Pilotos são identificados pelo id interno (Driver.Id),
// nunca pelo SteamID — não expomos o SteamID de terceiros.

public record PagedDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>Linha da lista de corridas.</summary>
public record RaceListItemDto(
    string EventId, string? Title, string? Track, string? Tier, string EventType,
    DateTime StartsAt, string Status, int Splits, int Drivers, string? Classes);

public record SplitSummaryDto(int SplitNo, int Drivers, int MostLapsCompleted, string? WinnerName, double? FastestLap);

/// <summary>Cabeçalho da corrida + resumo de cada split.</summary>
public record RaceDetailDto(
    string EventId, string? Title, string? Track, string? Tier, string EventType,
    DateTime StartsAt, DateTime RaceEndsAt, string Status, int? TotalSplits, string? Classes,
    IReadOnlyList<SplitSummaryDto> Splits);

/// <summary>Linha de classificação de um piloto num split.</summary>
public record ResultRowDto(
    Guid ResultId, Guid? DriverId, string Name, string? Nationality, string? Badge,
    string? CarClass, string? CarNumber, string? VehName, string? TeamName,
    int? GridPos, int? ClassGridPos, int? Position, int? ClassPosition, int Laps,
    double? BestLapTime, double? FinishTime, string? FinishStatus, int PitStops,
    int? QualiPosition, int? QualiClassPosition, double? QualiBestLapTime,
    string? DriverRankTitle, int? DriverRankTier, string? SafetyRankTitle, int? SafetyRankTier,
    int? DriverRankAdjustment, int? SafetyRankAdjustment, bool HasLaps);

public record SplitResultsDto(string EventId, int SplitNo, IReadOnlyList<ResultRowDto> Rows);

/// <summary>Uma volta. Combustível e desgaste são frações (1.0 = cheio/novo).</summary>
public record LapDto(
    int Lap, int? Position, double? LapTime, double? S1, double? S2, double? S3, double? Elapsed,
    double? Fuel, string? FrontCompound, string? RearCompound,
    double? WearFL, double? WearFR, double? WearRL, double? WearRR, bool Pit);

/// <summary>O piloto numa corrida: a linha de resultado + o volta a volta.</summary>
public record ResultDetailDto(
    string EventId, string? Title, string? Track, string? Tier, string EventType, DateTime StartsAt,
    int SplitNo, int SplitDrivers, double? ClassFastestLap, ResultRowDto Row, IReadOnlyList<LapDto> Laps);

/// <summary>Uma corrida no histórico de um piloto.</summary>
public record DriverRaceDto(
    Guid ResultId, string EventId, string? Title, string? Track, string? Tier, string EventType,
    DateTime StartsAt, int SplitNo, string? CarClass, string? VehName,
    int? ClassGridPos, int? ClassPosition, int? Position, double? BestLapTime, string? FinishStatus,
    string? DriverRankTitle, int? DriverRankTier, string? SafetyRankTitle, int? SafetyRankTier,
    int? DriverRankAdjustment, int? SafetyRankAdjustment);

public record DriverStatsDto(
    int Races, int Wins, int Podiums, int Top5, int Poles, int Dnfs, int? BestFinish, int LapsCompleted);

/// <summary>Perfil público de um piloto, a partir das corridas que o scanner guardou.</summary>
public record DriverProfileDto(
    Guid DriverId, string Name, string? Nationality, string? Badge,
    string? DriverRankTitle, int? DriverRankTier, int? DriverRankProgress,
    string? SafetyRankTitle, int? SafetyRankTier, int? SafetyRankProgress,
    DateTime LastSeenAt, bool IsMe, DriverStatsDto Stats, PagedDto<DriverRaceDto> Races);
