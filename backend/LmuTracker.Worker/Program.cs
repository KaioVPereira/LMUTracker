using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.RaceOs;
using LmuTracker.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Dados fictícios para testar as telas (só em desenvolvimento).
if (args.Contains("seed-dev"))
{
    if (!builder.Environment.IsDevelopment())
    {
        Console.Error.WriteLine("seed-dev só roda em Development.");
        return;
    }
    using var hostSeed = builder.Build();
    using var scopeSeed = hostSeed.Services.CreateScope();
    await DevSeed.RunAsync(scopeSeed.ServiceProvider.GetRequiredService<AppDbContext>());
    return;
}

// Retrato ao vivo FICTÍCIO num evento real da agenda (só em desenvolvimento): seed-live <eventId>.
if (args.Contains("seed-live"))
{
    if (!builder.Environment.IsDevelopment())
    {
        Console.Error.WriteLine("seed-live só roda em Development.");
        return;
    }
    var idx = Array.IndexOf(args, "seed-live");
    using var hostLive = builder.Build();
    using var scopeLive = hostLive.Services.CreateScope();
    await DevSeed.LiveAsync(scopeLive.ServiceProvider.GetRequiredService<AppDbContext>(),
        idx + 1 < args.Length ? args[idx + 1] : null);
    return;
}

// Uma passada só do retrato ao vivo (event/overview + inscritos de todos os eventos em andamento, sem esperar os
// intervalos), sem descobrir nem baixar resultados.
if (args.Contains("live-once"))
{
    using var hostOnce = builder.Build();
    using var scopeOnce = hostOnce.Services.CreateScope();
    var n = await scopeOnce.ServiceProvider.GetRequiredService<LmuTracker.Infrastructure.Scanning.ScannerService>()
        .RefreshLiveAsync(CancellationToken.None, force: true);
    Console.WriteLine($"{n} retratos ao vivo gravados.");
    return;
}

// Confere a previsão de split contra os resultados reais (só taxas de acerto, nenhum nome).
if (args.Contains("check-splits"))
{
    using var hostCheck = builder.Build();
    using var scopeCheck = hostCheck.Services.CreateScope();
    await SplitCheck.RunAsync(scopeCheck.ServiceProvider.GetRequiredService<AppDbContext>());
    return;
}

// Bootstrap único do login Steam da conta de serviço: gera e guarda o refresh token, depois sai.
if (args.Contains("steam-login"))
{
    using var host1 = builder.Build();
    using var scope1 = host1.Services.CreateScope();
    var bootstrap = scope1.ServiceProvider.GetRequiredService<SteamBootstrap>();
    await bootstrap.RunAsync();
    return;
}

// Registra uma versão do app Android (o APK já precisa estar em AppReleases:Directory) e sai. No servidor:
//   docker compose exec worker dotnet LmuTracker.Worker.dll publish-release --version 1.0.0 --code 1 \
//     --file pitwall-1.0.0.apk --notes "O que mudou"
// Em --notes, "\n" vira quebra de linha. O app instalado avisa assim que a versão aparece em /api/app/android.
if (args.Contains("publish-release"))
{
    string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    var version = Arg("--version");
    var file = Arg("--file");
    if (version is null || file is null || !int.TryParse(Arg("--code"), out var code) || code < 1)
    {
        Console.Error.WriteLine("Uso: publish-release --version 1.0.0 --code 1 --file pitwall-1.0.0.apk [--notes \"...\"]");
        Environment.ExitCode = 1;
        return;
    }
    var dir = builder.Configuration["AppReleases:Directory"] ?? "";
    if (Path.GetFileName(file) != file || !File.Exists(Path.Combine(dir, file)))
    {
        Console.Error.WriteLine($"Arquivo não encontrado em '{dir}': {file}");
        Environment.ExitCode = 1;
        return;
    }
    using var hostR = builder.Build();
    using var scopeR = hostR.Services.CreateScope();
    var dbR = scopeR.ServiceProvider.GetRequiredService<AppDbContext>();
    if (await dbR.AppReleases.AnyAsync(r => r.VersionCode >= code))
    {
        Console.Error.WriteLine($"Já existe versão com code >= {code}: o versionCode tem que crescer a cada publicação.");
        Environment.ExitCode = 1;
        return;
    }
    dbR.AppReleases.Add(new AppRelease
    {
        Version = version,
        VersionCode = code,
        FileName = file,
        Notes = Arg("--notes")?.Replace("\\n", "\n"),
    });
    await dbR.SaveChangesAsync();
    Console.WriteLine($"Publicada a versão {version} (code {code}).");
    return;
}

// Conta incidentes e penalidades do resultado cru já guardado (sem falar com o RaceOS) e sai.
if (args.Contains("backfill-incidents"))
{
    using var hostB = builder.Build();
    using var scopeB = hostB.Services.CreateScope();
    var scanner = scopeB.ServiceProvider.GetRequiredService<LmuTracker.Infrastructure.Scanning.ScannerService>();
    var total = 0;
    int n;
    while ((n = await scanner.BackfillIncidentsAsync(CancellationToken.None)) > 0) total += n;
    Console.WriteLine($"Splits com incidentes contados: {total}");
    return;
}

// Lê os revezamentos de piloto (carros divididos) do resultado cru já guardado, sem falar com o RaceOS, e sai.
if (args.Contains("backfill-crew"))
{
    using var hostC = builder.Build();
    using var scopeC = hostC.Services.CreateScope();
    var scanner = scopeC.ServiceProvider.GetRequiredService<LmuTracker.Infrastructure.Scanning.ScannerService>();
    var total = 0;
    int n;
    while ((n = await scanner.BackfillCrewAsync(CancellationToken.None)) > 0) total += n;
    Console.WriteLine($"Corridas verificadas: {total}");
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
