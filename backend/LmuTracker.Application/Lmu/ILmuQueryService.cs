namespace LmuTracker.Application.Lmu;

public interface ILmuQueryService
{
    Task<OverviewDto> GetOverviewAsync(CancellationToken ct = default);
}
