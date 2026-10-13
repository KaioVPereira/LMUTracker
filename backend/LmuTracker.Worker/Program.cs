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

    var next = await db.ScanEvents.OrderBy(e => e.RaceStartsAt).Take(6)
        .Select(e => new { e.Tier, e.EventType, e.RaceStartsAt, e.RaceOsStatus, e.EventId })
        .ToListAsync();
    Console.WriteLine("Próximas na agenda:");
    foreach (var e in next)
        Console.WriteLine($"  {e.RaceStartsAt:MM-dd HH:mm} | {e.Tier,-12} | {e.RaceOsStatus,-8} | {e.EventId[..8]}…");
    return;
}

builder.Services.AddHostedService<Worker>();
var host = builder.Build();
host.Run();
