using LmuTracker.Infrastructure.Services;

namespace LmuTracker.Api.Background;

/// <summary>
/// Mantém os destaques da tela inicial calculados: na subida da Api e a cada minuto. Assim nem o primeiro
/// visitante depois de um deploy (nem quem chega com o site parado há horas) espera as agregações.
/// </summary>
public sealed class HomeWarmer : BackgroundService
{
    private readonly SnapshotCache _cache;

    public HomeWarmer(SnapshotCache cache) => _cache = cache;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(HomeService.CacheFor);
        do _cache.Warm(HomeService.CacheKey, HomeService.CacheFor, HomeService.BuildAsync);
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
