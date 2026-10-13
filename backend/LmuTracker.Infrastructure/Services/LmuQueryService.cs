using LmuTracker.Application.Lmu;
using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

public sealed class LmuQueryService : ILmuQueryService
{
    private readonly AppDbContext _db;

    public LmuQueryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<OverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var eventsTracked = await _db.ScanEvents.CountAsync(ct);
        var racesFetched = await _db.ScanEvents.CountAsync(e => e.Status == ScanEventStatus.Fetched, ct);
        var resultsStored = await _db.RaceResults.CountAsync(ct);
        var driversIndexed = await _db.Drivers.CountAsync(ct);

        var lastSeen = await _db.ScanEvents.MaxAsync(e => (DateTime?)e.FirstSeenAt, ct);
        var lastFetched = await _db.ScanEvents.MaxAsync(e => e.FetchedAt, ct);
        DateTime? lastActivity = lastSeen > lastFetched || lastFetched is null ? lastSeen : lastFetched;

        var upcoming = await _db.ScanEvents
            .Where(e => e.RaceEndsAt > now)
            .OrderBy(e => e.RaceStartsAt)
            .Take(12)
            .Select(e => new UpcomingRaceDto(
                e.EventId, e.Title, e.Track, e.Tier, e.EventType, e.RaceStartsAt, e.RaceEndsAt))
            .ToListAsync(ct);

        var recent = await _db.ScanEvents
            .Where(e => e.Status == ScanEventStatus.Fetched)
            .OrderByDescending(e => e.RaceStartsAt)
            .Take(10)
            .Select(e => new RecentRaceDto(
                e.EventId, e.Title, e.Track, e.Tier, e.RaceStartsAt,
                e.Splits.Count(),
                e.Splits.Sum(s => s.DriverCount)))
            .ToListAsync(ct);

        return new OverviewDto(eventsTracked, racesFetched, resultsStored, driversIndexed,
            lastActivity, upcoming, recent);
    }
}
