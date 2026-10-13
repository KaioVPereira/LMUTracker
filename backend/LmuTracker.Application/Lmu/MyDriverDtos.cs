namespace LmuTracker.Application.Lmu;

/// <summary>Uma corrida do piloto vinculado, achada nas corridas varridas.</summary>
public record MyResultDto(
    string EventId, string? Title, string? Track, string? Tier, DateTime StartsAt, int SplitNo,
    string? CarClass, string? VehName, int? GridPos, int? ClassGridPos, int? Position, int? ClassPosition, int Laps,
    double? BestLapTime, string? FinishStatus, int? DriverRankAdjustment, int? SafetyRankAdjustment);

/// <summary>
/// Painel "meu piloto". <see cref="Linked"/> false = ainda não vinculou a Steam. <see cref="Found"/> false =
/// vinculou, mas o scanner ainda não viu esse SteamID em nenhuma corrida.
/// </summary>
public record MyDriverDto(
    bool Linked,
    bool Found,
    string? SteamId,
    string? Name,
    string? Nationality,
    string? Badge,
    string? DriverRankTitle,
    int? DriverRankTier,
    string? SafetyRankTitle,
    int? SafetyRankTier,
    DateTime? LastSeenAt,
    int RacesFound,
    IReadOnlyList<MyResultDto> Recent);
