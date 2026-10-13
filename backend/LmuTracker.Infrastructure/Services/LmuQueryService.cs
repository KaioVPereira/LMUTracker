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

    public async Task<MyDriverDto> GetMyDriverAsync(Guid userId, CancellationToken ct = default)
    {
        var steamId = await _db.Users.Where(u => u.Id == userId).Select(u => u.SteamId).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(steamId))
            return new MyDriverDto(false, false, null, null, null, null, null, null, null, null, null, 0,
                Array.Empty<MyResultDto>());

        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.SteamId == steamId, ct);
        var mine = _db.RaceResults.Where(r => r.SteamId == steamId);
        var racesFound = await mine.CountAsync(ct);

        var recent = await mine
            .OrderByDescending(r => r.RaceSplit.ScanEvent.RaceStartsAt)
            .Take(15)
            .Select(r => new MyResultDto(
                r.RaceSplit.ScanEvent.EventId, r.RaceSplit.ScanEvent.Title, r.RaceSplit.ScanEvent.Track,
                r.RaceSplit.ScanEvent.Tier, r.RaceSplit.ScanEvent.RaceStartsAt, r.RaceSplit.SplitNo,
                r.CarClass, r.VehName, r.GridPos, r.ClassGridPos, r.Position, r.ClassPosition, r.Laps,
                r.BestLapTime, r.FinishStatus, r.DriverRankAdjustment, r.SafetyRankAdjustment))
            .ToListAsync(ct);

        return new MyDriverDto(
            Linked: true,
            Found: driver is not null || racesFound > 0,
            SteamId: steamId,
            Name: driver?.Name,
            Nationality: driver?.Nationality,
            Badge: driver?.Badge,
            DriverRankTitle: driver?.DriverRankTitle,
            DriverRankTier: driver?.DriverRankTier,
            SafetyRankTitle: driver?.SafetyRankTitle,
            SafetyRankTier: driver?.SafetyRankTier,
            LastSeenAt: driver?.LastSeenAt,
            RacesFound: racesFound,
            Recent: recent);
    }
}
