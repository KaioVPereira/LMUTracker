namespace LmuTracker.Application.Lmu;

public interface ILmuQueryService
{
    Task<OverviewDto> GetOverviewAsync(CancellationToken ct = default);
    Task<MyDriverDto> GetMyDriverAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Corridas com resultado, da mais recente para a mais antiga. <paramref name="tier"/> null = todas.</summary>
    Task<PagedDto<RaceListItemDto>> ListRacesAsync(string? tier, int page, CancellationToken ct = default);
    Task<RaceDetailDto?> GetRaceAsync(string eventId, CancellationToken ct = default);
    Task<SplitResultsDto?> GetSplitAsync(string eventId, int splitNo, CancellationToken ct = default);
    Task<ResultDetailDto?> GetResultAsync(Guid resultId, CancellationToken ct = default);
    Task<DriverProfileDto?> GetDriverAsync(Guid driverId, Guid currentUserId, int page, CancellationToken ct = default);
}
