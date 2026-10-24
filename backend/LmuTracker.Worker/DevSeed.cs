using System.Text.Json;
using System.Text.Json.Nodes;
using LmuTracker.Application.Lmu;
using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Worker;

/// <summary>
/// Dados FICTÍCIOS para testar as telas em desenvolvimento (nomes inventados, SteamIDs numa faixa falsa).
/// Só roda com DOTNET_ENVIRONMENT=Development. Refazer apaga o seed anterior. Login de teste: demo / demo12345.
/// </summary>
public static class DevSeed
{
    private const string SeedSeries = "dev-seed";
    private const string FakeSteamPrefix = "7656119900000";

    /// <summary>
    /// Retrato ao vivo FICTÍCIO: monta um event/overview no formato do jogo (com nomes e host/senha de mentira, para
    /// conferir que não são guardados) e passa pelo mesmo leitor do scanner. Sem eventId = todos os em andamento.
    /// </summary>
    public static async Task LiveAsync(AppDbContext db, string? eventId)
    {
        var now = DateTime.UtcNow;
        var events = await db.ScanEvents
            .Where(e => eventId != null ? e.EventId == eventId
                : e.Status == ScanEventStatus.Pending && e.RaceEndsAt > now && (e.EventStartsAt ?? e.RaceStartsAt) <= now)
            .ToListAsync();
        foreach (var e in events)
        {
            var rnd = new Random(e.EventId.GetHashCode());
            var size = e.EventType == "special" ? 62 : 30;
            var total = e.EventType == "special" ? 1400 + rnd.Next(400) : 60 + rnd.Next(400);
            var hyper = (int)(total * 0.34); var lmp2 = (int)(total * 0.21); var gt3 = total - hyper - lmp2;
            var count = (int)Math.Ceiling(total / (double)size);
            var splits = new JsonArray();
            var servers = new JsonArray();
            var nations = new[] { "BR", "FR", "DE", "IT", "GB", "ES", "PT", "US", "NL", "BE", "PL", "JP" };
            var cars = new Dictionary<string, string[]>
            {
                ["Hyper"] = new[] { "Ferrari 499P", "Toyota GR010 Hybrid", "Porsche 963", "Cadillac V-Series.R", "Peugeot 9X8" },
                ["LMP2_ELMS"] = new[] { "Oreca 07" },
                ["LMGT3"] = new[] { "Porsche 911 GT3 R LMGT3", "BMW M4 LMGT3", "Ferrari 296 LMGT3", "McLaren 720S LMGT3" },
            };
            var g = 0;
            for (var s = 0; s < count; s++)
            {
                var drivers = new JsonArray();
                for (var d = 0; d < Math.Min(size, total - s * size); d++, g++)
                {
                    // Nomes inventados; os 40 primeiros batem com os pilotos do seed-dev (se ele tiver rodado).
                    var cls = (g % 100) < 34 ? "Hyper" : (g % 100) < 55 ? "LMP2_ELMS" : "LMGT3";
                    drivers.Add(new JsonObject
                    {
                        ["driver"] = new JsonObject
                        {
                            ["name"] = g < 40 ? $"Piloto Exemplo {g + 1:D2}" : $"Piloto Fictício {g + 1}",
                            ["nationality"] = nations[g % nations.Length],
                            ["avatar"] = "https://exemplo.invalid/avatar.png",
                        },
                        ["class"] = cls,
                        ["car"] = new JsonObject { ["name"] = cars[cls][g % cars[cls].Length] },
                        ["carNumber"] = (g % 99 + 1).ToString(),
                    });
                }
                splits.Add(new JsonObject { ["splitNo"] = s + 1, ["drivers"] = drivers });
                servers.Add(new JsonObject { ["host"] = "0.0.0.0", ["port"] = 64290, ["password"] = "falsa", ["status"] = "Running" });
            }
            var overview = new JsonObject
            {
                ["id"] = e.EventId, ["status"] = "Running", ["totalSplits"] = count, ["eventFull"] = false,
                ["lastUpdatesAt"] = now.AddMinutes(-3).ToString("O"),
                ["registered"] = new JsonObject
                {
                    ["total"] = total,
                    ["class"] = new JsonObject { ["Hyper"] = hyper, ["LMP2_ELMS"] = lmp2, ["LMGT3"] = gt3 },
                },
                ["splits"] = splits, ["servers"] = servers,
            };
            var raw = JsonSerializer.SerializeToElement(overview);
            e.LiveData = LiveSnapshot.FromOverview(raw);
            e.LiveDetail = LiveDetail.Encode(LiveDetail.Clean(raw), null, null);
            e.LiveCheckedAt = now.AddMinutes(-2);
            Console.WriteLine($"{e.Title}: {e.LiveData}");
            var (src, _, list) = LiveDetail.Read(e.LiveDetail);
            using var stored = JsonBlob.Decode(e.LiveDetail);
            var text = stored?.RootElement.GetRawText() ?? "";
            var leaked = text.Contains("falsa") || text.Contains("0.0.0.0") || text.Contains("avatar");
            Console.WriteLine($"  detalhe: {src}, {list.Count} pilotos; host/senha/avatar guardados: {(leaked ? "SIM (erro)" : "não")}");
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"{events.Count} eventos com retrato fictício.");
    }

    public static async Task RunAsync(AppDbContext db)
    {
        // Limpa o seed anterior (eventos em cascata levam splits e resultados).
        db.ScanEvents.RemoveRange(await db.ScanEvents.Where(e => e.SeriesId == SeedSeries).ToListAsync());
        db.Drivers.RemoveRange(await db.Drivers.Where(d => d.SteamId.StartsWith(FakeSteamPrefix)).ToListAsync());
        db.Users.RemoveRange(await db.Users.Where(u => u.Username == "demo").ToListAsync());
        await db.SaveChangesAsync();

        var rnd = new Random(397);
        var nations = new[] { "BR", "FR", "DE", "IT", "GB", "ES", "PT", "US", "NL", "BE", "PL", "JP" };
        var ranks = new[] { "Bronze", "Silver", "Gold", "Platinum" };
        var drivers = Enumerable.Range(1, 40).Select(i => new Driver
        {
            SteamId = FakeSteamPrefix + i.ToString("D4"),
            Name = $"Piloto Exemplo {i:D2}",
            Nationality = nations[i % nations.Length],
            Badge = i % 5 == 0 ? "sr-saint" : "sr-clean",
            DriverRankTitle = ranks[Math.Min(3, i / 11)], DriverRankTier = 1 + i % 3, DriverRankProgress = (i * 10) % 100,
            SafetyRankTitle = ranks[Math.Min(3, 1 + i / 14)], SafetyRankTier = 1 + i % 3, SafetyRankProgress = (i * 30) % 100,
            LastSeenAt = DateTime.UtcNow.AddHours(-1),
        }).ToList();
        db.Drivers.AddRange(drivers);

        var now = DateTime.UtcNow;
        var races = new[]
        {
            (Title: "LMGT3 Fixed", Track: "Fuji", Scene: "FujiWEC", Tier: "beginner", Type: "daily", Ago: 50, Splits: 2, Laps: 18, Lap: 98.4, Classes: new[] { "LMGT3" }),
            (Title: "ELMS Super 60", Track: "Portimão", Scene: "PortimaoELMS", Tier: "advanced", Type: "daily", Ago: 120, Splits: 1, Laps: 34, Lap: 91.6, Classes: new[] { "LMP2_ELMS", "LMP3", "LMGT3" }),
            (Title: "Hypersprint Series", Track: "Road Atlanta", Scene: "RoadAtlanta", Tier: "weekly", Type: "weekly", Ago: 300, Splits: 1, Laps: 40, Lap: 72.2, Classes: new[] { "Hyper" }),
        };

        var raceNo = 0;
        foreach (var r in races)
        {
            raceNo++;
            var starts = now.AddMinutes(-r.Ago);
            var ev = new ScanEvent
            {
                EventId = $"00000000-dev0-0000-0000-{raceNo:D12}", SeriesId = SeedSeries, Title = r.Title, Track = r.Track,
                Scene = r.Scene, EventType = r.Type, Tier = r.Tier, EventStartsAt = starts,
                RaceStartsAt = starts.AddMinutes(12), RaceEndsAt = starts.AddMinutes(45), RaceOsStatus = "Finished",
                Status = ScanEventStatus.Fetched, TotalSplits = r.Splits, FetchedAt = now, DetailVersion = 2,
            };
            db.ScanEvents.Add(ev);

            var pool = drivers.OrderBy(_ => rnd.Next()).ToList();
            for (var s = 1; s <= r.Splits; s++)
            {
                var field = pool.Skip((s - 1) * 16).Take(16).ToList();
                var split = new RaceSplit { ScanEvent = ev, SplitNo = s, MostLapsCompleted = r.Laps, DriverCount = field.Count };
                var pace = field.Select((d, i) => (d, skill: r.Lap * (1 + i * 0.004 + rnd.NextDouble() * 0.006))).ToList();
                var classOf = field.ToDictionary(d => d.SteamId, d => r.Classes[rnd.Next(r.Classes.Length)]);

                var finish = pace.OrderBy(p => p.skill).ToList();
                var classCount = new Dictionary<string, int>();
                for (var pos = 1; pos <= finish.Count; pos++)
                {
                    var (d, skill) = finish[pos - 1];
                    var cls = classOf[d.SteamId];
                    classCount[cls] = classCount.GetValueOrDefault(cls) + 1;
                    var dnf = pos == finish.Count;
                    var laps = MakeLaps(rnd, dnf ? r.Laps / 2 : r.Laps, skill, pos);
                    var grid = Math.Clamp(pos + rnd.Next(-3, 4), 1, finish.Count);
                    split.Results.Add(new RaceResult
                    {
                        SteamId = d.SteamId, Name = d.Name, Nationality = d.Nationality, Badge = d.Badge,
                        CarClass = cls, CarNumber = (10 + pos).ToString(), VehName = $"Equipe Exemplo #{10 + pos}",
                        TeamName = $"Equipe Exemplo {pos % 7 + 1}",
                        GridPos = grid, ClassGridPos = Math.Max(1, grid / 2), Position = dnf ? null : pos,
                        ClassPosition = dnf ? null : classCount[cls], Laps = laps.Count,
                        BestLapTime = laps.Where(l => !l.Pit && l.Lap > 1).Min(l => l.LapTime),
                        FinishTime = dnf ? null : laps.Sum(l => l.LapTime), FinishStatus = dnf ? "DNF" : "Finished Normally",
                        DriverRankAdjustment = rnd.Next(-3, 5), SafetyRankAdjustment = rnd.Next(-2, 4),
                        DriverRankTitle = d.DriverRankTitle, DriverRankTier = d.DriverRankTier, DriverRankProgress = d.DriverRankProgress,
                        SafetyRankTitle = d.SafetyRankTitle, SafetyRankTier = d.SafetyRankTier, SafetyRankProgress = d.SafetyRankProgress,
                        QualiPosition = grid, QualiClassPosition = Math.Max(1, grid / 2), QualiBestLapTime = Math.Round(skill * 0.995, 3),
                        PitStops = laps.Count(l => l.Pit), LapsData = LapCodec.Encode(laps),
                    });
                }
                db.RaceSplits.Add(split);
            }
        }

        db.Users.Add(new User
        {
            Username = "demo", Email = "demo@exemplo.com", DisplayName = "Usuário Demo",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo12345"),
            SteamId = drivers[6].SteamId, LinkedAt = now,
        });

        await db.SaveChangesAsync();
        Console.WriteLine("Seed fictício criado: 3 corridas, 40 pilotos. Login de teste: demo / demo12345.");
    }

    private static List<LapDto> MakeLaps(Random rnd, int count, double pace, int pos)
    {
        var laps = new List<LapDto>();
        var pitLap = count / 2;
        double elapsed = 0, fuel = 0.98, wear = 1.0;
        for (var n = 1; n <= count; n++)
        {
            var pit = n == pitLap;
            var t = pace * (n == 1 ? 1.12 : 1) + (rnd.NextDouble() - 0.5) * 0.8 + (pit ? 24 : 0) + (1 - wear) * 3;
            var s1 = t * 0.31; var s2 = t * 0.36; var s3 = t - s1 - s2;
            elapsed += t;
            fuel = pit ? 0.98 : Math.Max(0.02, fuel - 0.035);
            wear = pit ? 1.0 : Math.Max(0.4, wear - 0.012 - rnd.NextDouble() * 0.004);
            var compound = n > pitLap && pos % 3 == 0 ? "Hard" : "Medium";
            laps.Add(new LapDto(n, Math.Clamp(pos + rnd.Next(-2, 3), 1, 16), Math.Round(t, 3), Math.Round(s1, 3),
                Math.Round(s2, 3), Math.Round(s3, 3), Math.Round(elapsed, 3), Math.Round(fuel, 3), compound, compound,
                Math.Round(wear - 0.02, 3), Math.Round(wear, 3), Math.Round(wear + 0.01, 3), Math.Round(wear + 0.005, 3), pit));
        }
        return laps;
    }
}
