using LmuTracker.Application.Lmu;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LmuTracker.Infrastructure.Services;

/// <summary>
/// Estatísticas do perfil. Um piloto tem no máximo algumas centenas de corridas guardadas: carrega todas uma vez
/// (com o volta a volta, para a consistência de ritmo) e calcula em memória. Cache de 2 min por piloto.
/// </summary>
public sealed class DriverStatsService : IDriverStatsService
{
    private const string FinishedNormally = "Finished Normally";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);
    private static readonly string[] Titles = { "Bronze", "Silver", "Gold", "Platinum", "Diamond" };

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public DriverStatsService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private sealed record Row(
        Guid ResultId, Guid SplitId, int SplitNo, string EventId, string? Title, string? Track, DateTime StartsAt,
        string? CarClass, string? Car, int? Position, int? ClassPosition, int? ClassGridPos, int? QualiClassPosition,
        string? FinishStatus, double? BestLap, int Laps, int? Incidents, int? Penalties,
        string? DrTitle, int? DrTier, int? DrProgress, int? DrAdj,
        string? SrTitle, int? SrTier, int? SrProgress, int? SrAdj, byte[]? LapsData,
        string? RowSteamId = null, List<CrewMath.Stint>? Crew = null);

    /// <summary>
    /// As corridas do piloto, inclusive os carros divididos em que ele guiou um turno — nesses, voltas e melhor volta
    /// são só as dos turnos dele, e o rank/saldo da linha (de quem terminou) só vale quando foi ele quem terminou.
    /// </summary>
    private async Task<List<Row>> RowsOfAsync(string steamId, CancellationToken ct, string? track = null)
    {
        var q = LmuQueryService.RacesOf(_db, steamId).AsNoTracking();
        if (track is not null) q = q.Where(r => r.RaceSplit.ScanEvent.Track == track);
        var rows = await q
            .Select(r => new Row(
                r.Id, r.RaceSplitId, r.RaceSplit.SplitNo, r.RaceSplit.ScanEvent.EventId, r.RaceSplit.ScanEvent.Title,
                r.RaceSplit.ScanEvent.Track, r.RaceSplit.ScanEvent.EventStartsAt ?? r.RaceSplit.ScanEvent.RaceStartsAt,
                r.CarClass, r.CarType, r.Position, r.ClassPosition, r.ClassGridPos, r.QualiClassPosition,
                r.FinishStatus, r.BestLapTime, r.Laps, r.Incidents, r.Penalties,
                r.DriverRankTitle, r.DriverRankTier, r.DriverRankProgress, r.DriverRankAdjustment,
                r.SafetyRankTitle, r.SafetyRankTier, r.SafetyRankProgress, r.SafetyRankAdjustment, r.LapsData,
                r.SteamId,
                r.Crew.OrderBy(c => c.Seq).Select(c => new CrewMath.Stint(c.SteamId, c.Name, c.StartLap, c.EndLap,
                    c.DriverRankAdjustment, c.SafetyRankAdjustment)).ToList()))
            .ToListAsync(ct);
        return rows.Select(r => ForDriver(r, steamId)).ToList();
    }

    private static Row ForDriver(Row r, string steamId)
    {
        if (r.Crew is not { Count: > 0 } crew) return r;
        var mine = CrewMath.LapsOf(LapCodec.Decode(r.LapsData), crew, steamId);
        r = r with { BestLap = CrewMath.BestLap(mine) ?? r.BestLap, Laps = mine.Count, LapsData = LapCodec.Encode(mine) };
        if (r.RowSteamId == steamId) return r;
        // Rank do colega não é o dele: sem retrato de rank nesta corrida (o saldo do turno, quando conhecido, fica).
        var stint = crew.FirstOrDefault(c => c.SteamId == steamId);
        return r with
        {
            DrTitle = null, DrTier = null, DrProgress = null, DrAdj = stint?.DrAdj,
            SrTitle = null, SrTier = null, SrProgress = null, SrAdj = stint?.SrAdj,
        };
    }

    public async Task<DriverProfileStatsDto?> GetAsync(Guid driverId, CancellationToken ct = default) =>
        await _cache.GetOrCreateAsync($"driver-stats:{driverId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return await BuildAsync(driverId, ct);
        });

    private async Task<DriverProfileStatsDto?> BuildAsync(Guid driverId, CancellationToken ct)
    {
        var steamId = await _db.Drivers.Where(d => d.Id == driverId).Select(d => d.SteamId).FirstOrDefaultAsync(ct);
        if (steamId is null) return null;

        var rows = (await RowsOfAsync(steamId, ct)).OrderBy(r => r.StartsAt).ToList();
        var finished = rows.Where(r => r.FinishStatus == FinishedNormally).ToList();

        // Volta mais rápida da classe em cada split do piloto (para "hat-trick": pole, vitória e volta mais rápida).
        var splitIds = rows.Select(r => r.SplitId).Distinct().ToList();
        var classBest = (await _db.RaceResults.AsNoTracking()
                .Where(r => splitIds.Contains(r.RaceSplitId) && r.BestLapTime != null)
                .GroupBy(r => new { r.RaceSplitId, r.CarClass })
                .Select(g => new { g.Key.RaceSplitId, g.Key.CarClass, Best = g.Min(r => r.BestLapTime) })
                .ToListAsync(ct))
            .ToDictionary(x => (x.RaceSplitId, x.CarClass), x => x.Best);
        bool FastestInClass(Row r) => r.BestLap is { } b && classBest.TryGetValue((r.SplitId, r.CarClass), out var best)
                                       && best is { } bb && Math.Abs(b - bb) < 0.0005;

        // ---------- resumo ----------
        var gained = finished.Where(r => r.ClassGridPos is not null && r.ClassPosition is not null)
            .Select(r => (double)(r.ClassGridPos!.Value - r.ClassPosition!.Value)).ToList();
        var withInc = rows.Where(r => r.Incidents is not null).ToList();
        var paces = rows.Select(r => PaceCv(r.LapsData)).Where(v => v is not null).Select(v => v!.Value).OrderBy(v => v).ToList();
        var summary = new ProfileSummaryDto(
            Races: rows.Count,
            Wins: rows.Count(r => r.ClassPosition == 1),
            Podiums: rows.Count(r => r.ClassPosition is >= 1 and <= 3),
            Top5: rows.Count(r => r.ClassPosition is >= 1 and <= 5),
            Poles: rows.Count(r => r.ClassGridPos == 1),
            Dnfs: rows.Count(r => r.FinishStatus is not null && r.FinishStatus != FinishedNormally && r.FinishStatus != "None"),
            Finished: finished.Count,
            AvgPositionsGained: gained.Count > 0 ? Math.Round(gained.Average(), 2) : null,
            AvgClassFinish: Avg(finished.Select(r => r.ClassPosition)),
            AvgQualiClassPosition: Avg(rows.Select(r => r.QualiClassPosition)),
            RacesWithIncidents: withInc.Count,
            IncidentsPerRace: withInc.Count > 0 ? Math.Round(withInc.Average(r => r.Incidents!.Value), 2) : null,
            Penalties: rows.Sum(r => r.Penalties ?? 0),
            CleanRaces: withInc.Count(r => r.Incidents == 0 && r.FinishStatus == FinishedNormally),
            LapsCompleted: rows.Sum(r => r.Laps),
            PaceConsistency: paces.Count > 0 ? Math.Round(paces[paces.Count / 2] * 100, 2) : null,
            TopSplitRaces: rows.Count(r => r.SplitNo == 1));

        // ---------- evolução do rank ----------
        var history = rows.Select(r => new RankPointDto(
                r.StartsAt, r.EventId, r.Title, r.Track,
                r.DrTitle, r.DrTier, r.DrProgress, Score(r.DrTitle, r.DrTier, r.DrProgress), r.DrAdj,
                r.SrTitle, r.SrTier, r.SrProgress, Score(r.SrTitle, r.SrTier, r.SrProgress), r.SrAdj))
            .ToList();

        // ---------- pistas (por classe) e carros ----------
        var tracks = rows.Where(r => r.Track is not null)
            .GroupBy(r => (r.Track!, r.CarClass))
            .Select(g =>
            {
                var fin = g.Where(r => r.FinishStatus == FinishedNormally).ToList();
                var inc = g.Where(r => r.Incidents is not null).ToList();
                return new TrackStatDto(g.Key.Item1, g.Key.CarClass, g.Count(), g.Count(r => r.ClassPosition == 1),
                    g.Count(r => r.ClassPosition is >= 1 and <= 3), Avg(fin.Select(r => r.ClassPosition)),
                    fin.Min(r => r.ClassPosition), g.Min(r => r.BestLap),
                    inc.Count > 0 ? Math.Round(inc.Average(r => r.Incidents!.Value), 1) : null);
            })
            .OrderBy(t => t.AvgClassFinish ?? double.MaxValue).ThenByDescending(t => t.Races)
            .ToList();

        var cars = rows.Where(r => !string.IsNullOrEmpty(r.Car))
            .GroupBy(r => (r.Car!, r.CarClass))
            .Select(g =>
            {
                var fin = g.Where(r => r.FinishStatus == FinishedNormally).ToList();
                return new CarStatDto(g.Key.Item1, g.Key.CarClass, g.Count(), g.Count(r => r.ClassPosition == 1),
                    g.Count(r => r.ClassPosition is >= 1 and <= 3),
                    g.Count(r => r.FinishStatus is not null && r.FinishStatus != FinishedNormally && r.FinishStatus != "None"),
                    Avg(fin.Select(r => r.ClassPosition)), fin.Min(r => r.ClassPosition));
            })
            .OrderByDescending(c => c.Races).ThenBy(c => c.AvgClassFinish ?? double.MaxValue)
            .ToList();

        // ---------- rivais: quem mais dividiu split com ele, e quem terminou na frente ----------
        var rivalsRaw = await (
                from me in _db.RaceResults
                where me.SteamId == steamId
                join o in _db.RaceResults on me.RaceSplitId equals o.RaceSplitId
                where o.SteamId != null && o.SteamId != steamId
                select new
                {
                    o.SteamId, o.Name, o.Nationality, MePos = me.Position, OPos = o.Position,
                    At = me.RaceSplit.ScanEvent.EventStartsAt ?? me.RaceSplit.ScanEvent.RaceStartsAt,
                })
            .GroupBy(x => x.SteamId!)
            .Select(g => new
            {
                SteamId = g.Key,
                N = g.Count(),
                Ahead = g.Count(x => x.MePos != null && (x.OPos == null || x.MePos < x.OPos)),
                Behind = g.Count(x => x.OPos != null && (x.MePos == null || x.OPos < x.MePos)),
                Last = g.Max(x => x.At),
                Name = g.Max(x => x.Name),
                Nat = g.Max(x => x.Nationality),
            })
            .Where(x => x.N >= 2)
            .OrderByDescending(x => x.N).ThenByDescending(x => x.Last)
            .Take(8)
            .ToListAsync(ct);
        var rivalSteam = rivalsRaw.Select(r => r.SteamId).ToList();
        var rivalIds = await _db.Drivers.AsNoTracking().Where(d => rivalSteam.Contains(d.SteamId))
            .ToDictionaryAsync(d => d.SteamId, d => d.Id, ct);
        var rivals = rivalsRaw.Select(r => new RivalDto(rivalIds.TryGetValue(r.SteamId, out var id) ? id : null,
            r.Name ?? "—", r.Nat, r.N, r.Ahead, r.Behind, r.Last)).ToList();

        return new DriverProfileStatsDto(driverId, summary, history, tracks, cars, rivals,
            Achievements(rows, FastestInClass), DateTime.UtcNow);
    }

    // ---------------------------------------------------------------- conquistas

    private static List<AchievementDto> Achievements(List<Row> rows, Func<Row, bool> fastestInClass)
    {
        var list = new List<AchievementDto>();
        void First(string key, string title, string desc, Func<Row, bool> when)
        {
            var hit = rows.FirstOrDefault(when);
            list.Add(new AchievementDto(key, title, desc, hit is not null, hit?.StartsAt, hit?.EventId, null, null));
        }
        void Count(string key, string title, string desc, int goal, Func<Row, bool> when)
        {
            var hits = rows.Where(when).ToList();
            var at = hits.Count >= goal ? hits[goal - 1] : null;
            list.Add(new AchievementDto(key, title, desc, at is not null, at?.StartsAt, at?.EventId, Math.Min(hits.Count, goal), goal));
        }

        Count("races-10", "10 corridas", "Largou em 10 corridas oficiais.", 10, _ => true);
        Count("races-50", "50 corridas", "Largou em 50 corridas oficiais.", 50, _ => true);
        Count("races-100", "100 corridas", "Largou em 100 corridas oficiais.", 100, _ => true);
        First("first-podium", "Primeiro pódio", "Terminou entre os 3 primeiros da classe.", r => r.ClassPosition is >= 1 and <= 3);
        First("first-win", "Primeira vitória", "Venceu na classe.", r => r.ClassPosition == 1);
        First("top-split-win", "Rei do split 1", "Venceu na classe no split mais forte.", r => r.ClassPosition == 1 && r.SplitNo == 1);
        First("pole", "Pole position", "Largou em 1º na classe.", r => r.ClassGridPos == 1);
        First("hat-trick", "Hat-trick", "Pole, vitória e volta mais rápida da classe na mesma corrida.",
            r => r.ClassGridPos == 1 && r.ClassPosition == 1 && fastestInClass(r));
        First("clean", "Corrida limpa", "Terminou sem nenhum contato.", r => r.Incidents == 0 && r.FinishStatus == FinishedNormally);
        First("comeback", "Escalada", "Ganhou 10 ou mais posições na classe.",
            r => r.FinishStatus == FinishedNormally && r.ClassGridPos - r.ClassPosition >= 10);

        // Sequência de 3 pódios seguidos.
        var streak = 0;
        Row? streakHit = null;
        foreach (var r in rows)
        {
            streak = r.ClassPosition is >= 1 and <= 3 ? streak + 1 : 0;
            if (streak == 3) { streakHit = r; break; }
        }
        list.Add(new AchievementDto("podium-streak", "Em sequência", "3 pódios seguidos.", streakHit is not null,
            streakHit?.StartsAt, streakHit?.EventId, null, null));

        // 10 corridas no mesmo dia (UTC).
        var marathon = rows.GroupBy(r => r.StartsAt.Date).FirstOrDefault(g => g.Count() >= 10);
        list.Add(new AchievementDto("iron-man", "Maratonista", "10 corridas no mesmo dia.", marathon is not null,
            marathon?.ElementAt(9).StartsAt, marathon?.ElementAt(9).EventId, null, null));

        var tracks = rows.Where(r => r.Track is not null).Select(r => r.Track!).Distinct().Count();
        list.Add(new AchievementDto("globetrotter", "Viajante", "Correu em 5 pistas diferentes.", tracks >= 5, null, null,
            Math.Min(tracks, 5), 5));
        var classes = rows.Where(r => r.CarClass is not null).Select(r => r.CarClass!).Distinct().Count();
        list.Add(new AchievementDto("multiclass", "Versátil", "Correu em 3 classes diferentes.", classes >= 3, null, null,
            Math.Min(classes, 3), 3));
        return list;
    }

    // ---------------------------------------------------------------- comparação direta

    public async Task<HeadToHeadDto?> HeadToHeadAsync(Guid driverId, Guid otherId, CancellationToken ct = default)
    {
        var drivers = await _db.Drivers.AsNoTracking().Where(d => d.Id == driverId || d.Id == otherId).ToListAsync(ct);
        var me = drivers.FirstOrDefault(d => d.Id == driverId);
        var them = drivers.FirstOrDefault(d => d.Id == otherId);
        if (me is null || them is null || driverId == otherId) return null;

        var mine = await RowsOfAsync(me.SteamId, ct);
        var theirs = await RowsOfAsync(them.SteamId, ct);
        var bySplit = theirs.GroupBy(r => r.SplitId).ToDictionary(g => g.Key, g => g.First());

        var shared = mine.Where(r => bySplit.ContainsKey(r.SplitId))
            .Select(r => (Me: r, Them: bySplit[r.SplitId]))
            .OrderByDescending(x => x.Me.StartsAt)
            .ToList();

        static bool Ahead(Row a, Row b) => a.Position is not null && (b.Position is null || a.Position < b.Position);
        var sameClass = shared.Where(x => x.Me.CarClass == x.Them.CarClass && x.Me.BestLap is not null && x.Them.BestLap is not null).ToList();

        HeadToHeadSideDto Side(LmuTracker.Domain.Entities.Driver d, List<Row> rs) => new(
            d.Id, d.Name, d.Nationality, d.DriverRankTitle, d.DriverRankTier, d.SafetyRankTitle, d.SafetyRankTier,
            rs.Count, rs.Count(r => r.ClassPosition == 1), rs.Count(r => r.ClassPosition is >= 1 and <= 3));

        return new HeadToHeadDto(
            Side(me, mine), Side(them, theirs),
            shared.Count, shared.Count(x => Ahead(x.Me, x.Them)), shared.Count(x => Ahead(x.Them, x.Me)),
            sameClass.Count,
            sameClass.Count > 0 ? Math.Round(sameClass.Average(x => x.Me.BestLap!.Value - x.Them.BestLap!.Value), 3) : null,
            shared.Take(50).Select(x => new HeadToHeadRaceDto(
                x.Me.EventId, x.Me.Title, x.Me.Track, x.Me.StartsAt, x.Me.SplitNo,
                x.Me.CarClass, x.Me.Position, x.Me.ClassPosition, x.Me.BestLap, x.Me.FinishStatus,
                x.Them.CarClass, x.Them.Position, x.Them.ClassPosition, x.Them.BestLap, x.Them.FinishStatus)).ToList());
    }

    // ---------------------------------------------------------------- comparação por pista e classe

    /// <summary>Mesma regra do site (lib/classes.ts): as variantes do RaceOS contam como a mesma classe.</summary>
    internal static string ClassKey(string? raw)
    {
        var c = (raw ?? "").ToUpperInvariant();
        if (c.StartsWith("HYPER") || c is "LMH" or "LMDH") return "hyper";
        if (c.StartsWith("LMP2")) return "lmp2";
        if (c.StartsWith("LMP3")) return "lmp3";
        if (c.Contains("GT3")) return "gt3";
        return c.ToLowerInvariant();
    }

    public async Task<CompareDto?> CompareAsync(Guid driverId, Guid otherId, string track, string carClass,
        CancellationToken ct = default)
    {
        var drivers = await _db.Drivers.AsNoTracking().Where(d => d.Id == driverId || d.Id == otherId).ToListAsync(ct);
        var me = drivers.FirstOrDefault(d => d.Id == driverId);
        var them = drivers.FirstOrDefault(d => d.Id == otherId);
        if (me is null || them is null || driverId == otherId || string.IsNullOrWhiteSpace(track)) return null;

        var cls = ClassKey(carClass);
        async Task<List<Row>> Here(string steamId) => (await RowsOfAsync(steamId, ct, track))
            .Where(r => ClassKey(r.CarClass) == cls).OrderByDescending(r => r.StartsAt).ToList();
        var mine = await Here(me.SteamId);
        var theirs = await Here(them.SteamId);

        var bySplit = theirs.GroupBy(r => r.SplitId).ToDictionary(g => g.Key, g => g.First());
        var shared = mine.Where(r => bySplit.ContainsKey(r.SplitId)).Select(r => (Me: r, Them: bySplit[r.SplitId]))
            .Select(x => new HeadToHeadRaceDto(
                x.Me.EventId, x.Me.Title, x.Me.Track, x.Me.StartsAt, x.Me.SplitNo,
                x.Me.CarClass, x.Me.Position, x.Me.ClassPosition, x.Me.BestLap, x.Me.FinishStatus,
                x.Them.CarClass, x.Them.Position, x.Them.ClassPosition, x.Them.BestLap, x.Them.FinishStatus))
            .ToList();

        return new CompareDto(track, cls, CompareSide(me, mine), CompareSide(them, theirs), shared);
    }

    private static CompareSideDto CompareSide(LmuTracker.Domain.Entities.Driver d, List<Row> rows)
    {
        var fin = rows.Where(r => r.FinishStatus == FinishedNormally).ToList();
        var gained = fin.Where(r => r.ClassGridPos is not null && r.ClassPosition is not null)
            .Select(r => (double)(r.ClassGridPos!.Value - r.ClassPosition!.Value)).ToList();
        var inc = rows.Where(r => r.Incidents is not null).ToList();

        CompareLapDto? best = null;
        double? s1 = null, s2 = null, s3 = null;
        var paces = new List<double>();
        var cvs = new List<double>();
        var races = new List<CompareRaceDto>();
        foreach (var r in rows)
        {
            var laps = LapCodec.Decode(r.LapsData);
            var valid = laps.Where(l => !l.Pit && l.LapTime is > 0).ToList();
            foreach (var l in valid)
            {
                s1 = MinOf(s1, l.S1);
                s2 = MinOf(s2, l.S2);
                s3 = MinOf(s3, l.S3);
            }
            var b = valid.OrderBy(l => l.LapTime).FirstOrDefault();
            if (b is not null && (best is null || b.LapTime < best.LapTime))
                best = new CompareLapDto(b.LapTime!.Value, b.S1, b.S2, b.S3, r.ResultId, r.EventId, r.StartsAt, r.Car, b.Lap);
            else if (b is null && r.BestLap is { } bl && (best is null || bl < best.LapTime))
                best = new CompareLapDto(bl, null, null, null, r.ResultId, r.EventId, r.StartsAt, r.Car, 0);

            var pace = CleanMedian(laps);
            if (pace is not null) paces.Add(pace.Value);
            if (PaceCv(laps) is { } cv) cvs.Add(cv);
            races.Add(new CompareRaceDto(r.ResultId, r.EventId, r.Title, r.StartsAt, r.SplitNo, r.Car, r.ClassGridPos,
                r.ClassPosition, r.FinishStatus, b?.LapTime ?? r.BestLap, pace is null ? null : Math.Round(pace.Value, 3), r.Incidents));
        }
        cvs.Sort();

        return new CompareSideDto(
            d.Id, d.Name, d.Nationality, d.DriverRankTitle, d.DriverRankTier, d.SafetyRankTitle, d.SafetyRankTier,
            rows.Count, rows.Count(r => r.ClassPosition == 1), rows.Count(r => r.ClassPosition is >= 1 and <= 3),
            rows.Count(r => r.FinishStatus is not null && r.FinishStatus != FinishedNormally && r.FinishStatus != "None"),
            Avg(fin.Select(r => r.ClassPosition)), fin.Min(r => r.ClassPosition), Avg(rows.Select(r => r.ClassGridPos)),
            gained.Count > 0 ? Math.Round(gained.Average(), 2) : null,
            inc.Count > 0 ? Math.Round(inc.Average(r => r.Incidents!.Value), 2) : null,
            best, s1, s2, s3,
            paces.Count > 0 ? Math.Round(paces.Average(), 3) : null,
            cvs.Count > 0 ? Math.Round(cvs[cvs.Count / 2] * 100, 2) : null,
            rows.Where(r => r.Car is not null).GroupBy(r => r.Car).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(),
            races.Take(60).ToList());
    }

    private static double? MinOf(double? a, double? b) => b is not > 0 ? a : a is null ? b : Math.Min(a.Value, b.Value);

    /// <summary>Mediana das voltas limpas (sem pit, sem a 1ª, até 107% da melhor). Null com menos de 3.</summary>
    private static double? CleanMedian(IReadOnlyList<LapDto> laps)
    {
        var times = laps.Where(l => !l.Pit && l.Lap > 1 && l.LapTime is > 0).Select(l => l.LapTime!.Value).ToList();
        if (times.Count < 3) return null;
        var best = times.Min();
        var clean = times.Where(t => t <= best * 1.07).OrderBy(t => t).ToList();
        if (clean.Count < 3) return null;
        return clean.Count % 2 == 1 ? clean[clean.Count / 2] : (clean[clean.Count / 2 - 1] + clean[clean.Count / 2]) / 2;
    }

    public async Task<CompareSuggestionsDto> SuggestAsync(string track, string carClass, CancellationToken ct = default) =>
        (await _cache.GetOrCreateAsync($"cmp-suggest:{track}|{carClass}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var cls = ClassKey(carClass);
            var here = ExtrasService.OfClass(_db.RaceResults.AsNoTracking().Where(r => r.RaceSplit.ScanEvent.Track == track), cls);
            // Carro dividido: a corrida conta para todos do carro, e a volta é a de cada um (a da linha é a do carro).
            var crew = await CrewCredit.OfAsync(_db, here, includeOwner: true, ct);
            var stats = (await here.Where(r => r.SteamId != null)
                    .GroupBy(r => r.SteamId!)
                    .Select(g => new
                    {
                        SteamId = g.Key,
                        Starts = g.Count(),
                        Wins = g.Count(r => r.ClassPosition == 1),
                        Best = g.Where(r => !r.Crew.Any()).Min(r => r.BestLapTime),
                    })
                    .ToListAsync(ct))
                .Concat(crew.Select(c => new
                {
                    SteamId = c.SteamId,
                    Starts = c.IsOwner ? 0 : 1,
                    Wins = !c.IsOwner && c.ClassPosition == 1 ? 1 : 0,
                    Best = c.BestLap,
                }))
                .GroupBy(x => x.SteamId)
                .Select(g => new { SteamId = g.Key, Starts = g.Sum(x => x.Starts), Wins = g.Sum(x => x.Wins), Best = g.Min(x => x.Best) })
                .ToList();
            var wins = stats.Where(s => s.Wins > 0).OrderByDescending(s => s.Wins).ThenBy(s => s.Starts).Take(6).ToList();
            var fastest = stats.Where(s => s.Best is not null).OrderBy(s => s.Best).Take(6).ToList();
            var active = stats.OrderByDescending(s => s.Starts).Take(6).ToList();

            var steam = wins.Concat(fastest).Concat(active).Select(s => s.SteamId).Distinct().ToList();
            var people = await _db.Drivers.AsNoTracking().Where(d => steam.Contains(d.SteamId))
                .ToDictionaryAsync(d => d.SteamId, ct);
            List<CompareSuggestionDto> Map<T>(IEnumerable<T> list, Func<T, string> sid, Func<T, double> value, Func<T, int> starts) =>
                list.Where(x => people.ContainsKey(sid(x)))
                    .Select(x => people[sid(x)] is var p ? new CompareSuggestionDto(p.Id, p.Name, p.Nationality, value(x), starts(x)) : null!)
                    .ToList();

            return new CompareSuggestionsDto(track, cls,
                Map(wins, s => s.SteamId, s => s.Wins, s => s.Starts),
                Map(fastest, s => s.SteamId, s => s.Best!.Value, s => s.Starts),
                Map(active, s => s.SteamId, s => s.Starts, s => s.Starts));
        }))!;

    // ---------------------------------------------------------------- utilidades

    /// <summary>Bronze 1 = 0…100, Bronze 2 = 100…200, Bronze 3 = 200…300, Silver 1 = 300… (tiers sobem de 1 a 3).</summary>
    private static int? Score(string? title, int? tier, int? progress)
    {
        var i = Array.FindIndex(Titles, t => t.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || tier is null) return null;
        return i * 300 + (Math.Clamp(tier.Value, 1, 3) - 1) * 100 + Math.Clamp(progress ?? 0, 0, 100);
    }

    private static double? Avg(IEnumerable<int?> values)
    {
        var v = values.Where(x => x is not null).Select(x => (double)x!.Value).ToList();
        return v.Count > 0 ? Math.Round(v.Average(), 2) : null;
    }

    /// <summary>
    /// Variação do ritmo numa corrida (desvio-padrão ÷ média) só nas voltas "limpas": sem pit, sem a 1ª e até 107%
    /// da melhor. Menor = mais constante. Null com menos de 5 voltas limpas.
    /// </summary>
    private static double? PaceCv(byte[]? lapsData) => PaceCv(LapCodec.Decode(lapsData));

    private static double? PaceCv(IReadOnlyList<LapDto> laps)
    {
        var times = laps.Where(l => !l.Pit && l.Lap > 1 && l.LapTime is > 0).Select(l => l.LapTime!.Value).ToList();
        if (times.Count < 5) return null;
        var best = times.Min();
        var clean = times.Where(t => t <= best * 1.07).ToList();
        if (clean.Count < 5) return null;
        var mean = clean.Average();
        var sd = Math.Sqrt(clean.Sum(t => (t - mean) * (t - mean)) / clean.Count);
        return sd / mean;
    }
}
