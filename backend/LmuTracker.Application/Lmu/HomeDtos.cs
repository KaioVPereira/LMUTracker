namespace LmuTracker.Application.Lmu;

// Tela inicial: destaques que são iguais para todo mundo (vêm de cache curto). Pilotos pelo id interno.

/// <summary>Melhor volta de uma pista numa classe (setup fixo e livre são recordes separados).</summary>
public record LapRecordDto(
    string Track, string? CarClass, bool FixedSetup, double LapTime,
    Guid? DriverId, string DriverName, string? Nationality, string? Car,
    string EventId, DateTime SetAt, int SplitNo);

/// <summary>Vencedor de uma classe no split 1 de uma corrida.</summary>
public record TopSplitWinnerDto(
    string EventId, string? Title, string? Track, string? Tier, DateTime StartsAt,
    string? CarClass, Guid? DriverId, string DriverName, string? Nationality, string? Car,
    int Laps, int SplitDrivers, double? GapToSecond);

/// <summary>Quem mais ganhou Driver Rank no período: soma das setas de DR, com o retrato do rank no início e no fim.</summary>
public record RankClimberDto(
    Guid? DriverId, string Name, string? Nationality, int Races, int DriverRankGain,
    string? FromTitle, int? FromTier, int? FromProgress, string? ToTitle, int? ToTier, int? ToProgress);

/// <summary>Menor diferença entre 1º e 2º de uma classe no split 1.</summary>
public record CloseFinishDto(
    string EventId, string? Title, string? Track, DateTime StartsAt, string? CarClass, double Gap,
    string WinnerName, Guid? WinnerId, string SecondName, Guid? SecondId);

public record ActiveDriverDto(Guid? DriverId, string Name, string? Nationality, int Races);

public record ClassShareDto(string CarClass, int Starts);

public record HomeDto(
    IReadOnlyList<LapRecordDto> Records,
    IReadOnlyList<TopSplitWinnerDto> Winners,
    IReadOnlyList<RankClimberDto> Climbers,
    IReadOnlyList<CloseFinishDto> CloseFinishes,
    IReadOnlyList<ActiveDriverDto> MostActive,
    IReadOnlyList<ClassShareDto> ClassShare,
    DateTime GeneratedAt);

public interface IHomeService
{
    Task<HomeDto> GetAsync(CancellationToken ct = default);
}
