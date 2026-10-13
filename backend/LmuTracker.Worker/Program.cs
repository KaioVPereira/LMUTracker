using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.RaceOs;
using LmuTracker.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Bootstrap único do login Steam da conta de serviço: gera e guarda o refresh token, depois sai.
if (args.Contains("steam-login"))
{
    using var host1 = builder.Build();
    using var scope1 = host1.Services.CreateScope();
    var bootstrap = scope1.ServiceProvider.GetRequiredService<SteamBootstrap>();
    await bootstrap.RunAsync();
    return;
}

// Modo de status: `dotnet run -- report` imprime um resumo do que já foi guardado e sai.
if (args.Contains("report"))
{
    using var host0 = builder.Build();
    using var scope = host0.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var events = await db.ScanEvents.CountAsync();
    var pending = await db.ScanEvents.CountAsync(e => e.Status == ScanEventStatus.Pending);
    var fetched = await db.ScanEvents.CountAsync(e => e.Status == ScanEventStatus.Fetched);
    var splits = await db.RaceSplits.CountAsync();
    var results = await db.RaceResults.CountAsync();
    var drivers = await db.Drivers.CountAsync();

    Console.WriteLine($"ScanEvents: {events} (pendentes {pending}, baixados {fetched})");
    Console.WriteLine($"Splits: {splits} | Resultados: {results} | Pilotos no índice: {drivers}");

    var now = DateTime.UtcNow;
    var next = await db.ScanEvents.Where(e => e.RaceEndsAt > now)
        .OrderBy(e => e.EventStartsAt ?? e.RaceStartsAt).Take(9)
        .Select(e => new { e.Tier, e.EventStartsAt, e.RaceStartsAt, e.Title, e.Track, e.RaceOsStatus })
        .ToListAsync();
    Console.WriteLine("Próximas na agenda (largada oficial, UTC):");
    foreach (var e in next)
        Console.WriteLine($"  {e.EventStartsAt ?? e.RaceStartsAt:HH:mm} | {e.Tier,-12} | {e.Title,-42} | {e.Track,-18} | {e.RaceOsStatus}");

    var byStatus = await db.ScanEvents.GroupBy(e => e.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
    Console.WriteLine("Por estado: " + string.Join(", ", byStatus.Select(s => $"{s.Key}={s.N}")));

    var lastFetched = await db.ScanEvents.Where(e => e.Status == ScanEventStatus.Fetched)
        .OrderByDescending(e => e.EventStartsAt ?? e.RaceStartsAt).Take(8)
        .Select(e => new { e.Tier, Starts = e.EventStartsAt ?? e.RaceStartsAt, e.Title, e.Track, e.TotalSplits,
            Splits = e.Splits.Count(), Drivers = e.Splits.Sum(s => s.DriverCount) })
        .ToListAsync();
    Console.WriteLine("Últimas baixadas (largada oficial, UTC):");
    foreach (var e in lastFetched)
        Console.WriteLine($"  {e.Starts:HH:mm} | {e.Tier,-12} | {e.Title,-42} | {e.Track,-18} | splits {e.Splits}/{e.TotalSplits} | {e.Drivers} pilotos");

    // Cobertura: dentro de cada nível há uma corrida a cada 15 (bronze), 20 (prata) ou 30 min (ouro). Um
    // intervalo maior entre largadas guardadas = corrida que o scanner não viu.
    var cadence = new Dictionary<string, int> { ["beginner"] = 15, ["intermediate"] = 20, ["advanced"] = 30 };
    var all = await db.ScanEvents.Select(e => new { e.Tier, Starts = e.EventStartsAt ?? e.RaceStartsAt }).ToListAsync();
    Console.WriteLine("Cobertura por nível (desde a primeira corrida guardada):");
    foreach (var (tier, minutes) in cadence)
    {
        var times = all.Where(e => e.Tier == tier).Select(e => e.Starts).OrderBy(t => t).ToList();
        if (times.Count == 0) { Console.WriteLine($"  {tier,-12} | nenhuma"); continue; }
        var gaps = times.Zip(times.Skip(1), (a, b) => (a, b)).Where(p => (p.b - p.a).TotalMinutes > minutes + 2)
            .Select(p => $"{p.a:HH:mm}->{p.b:HH:mm}").ToList();
        var dupes = times.GroupBy(t => t).Count(g => g.Count() > 1);
        Console.WriteLine($"  {tier,-12} | {times.Count} corridas de {times[0]:HH:mm} a {times[^1]:HH:mm} | "
            + (gaps.Count == 0 ? "sem buracos" : $"BURACOS: {string.Join(", ", gaps)}")
            + (dupes > 0 ? $" | {dupes} horários repetidos" : ""));
    }
    return;
}

builder.Services.AddHostedService<Worker>();
var host = builder.Build();
host.Run();
