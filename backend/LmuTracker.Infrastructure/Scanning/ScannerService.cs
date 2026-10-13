using System.Globalization;
using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.RaceOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Coração do scanner. Em cada passada: (1) descobre as corridas da agenda e guarda o eventId cedo;
/// (2) para as que já terminaram, baixa todos os splits e grava resultados + índice de pilotos.
/// Guardar o eventId na janela da agenda é o que não dá para recuperar depois — por isso roda sempre.
/// </summary>
public sealed class ScannerService
{
    private static readonly string[] Tiers = { "beginner", "intermediate", "advanced" };
    private const int MaxSplits = 40; // trava de segurança

    // O fim "previsto" (raceEndsAt) chega antes do resultado: corrida por tempo termina na volta seguinte e o
    // RaceOS ainda processa. Então esperamos, e um 404 significa "ainda não" — nunca "corrida vazia".
    private static readonly TimeSpan FirstFetchDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryEvery = TimeSpan.FromMinutes(5);
    private const int MaxFetchAttempts = 36;       // ~3h tentando a cada 5 min
    private const int AcceptPartialAfter = 12;     // depois de ~1h, grava os splits que vieram
    private static readonly TimeSpan EmptyGrace = TimeSpan.FromMinutes(30); // só então "sem splits" = vazia

    private enum FetchOutcome { Fetched, NotReady }

    private readonly AppDbContext _db;
    private readonly RaceOsClient _raceos;
    private readonly ILogger<ScannerService> _log;

    public ScannerService(AppDbContext db, RaceOsClient raceos, ILogger<ScannerService> log)
    {
        _db = db;
        _raceos = raceos;
        _log = log;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await DiscoverAsync(ct);
        await FetchDueAsync(ct);
    }

    // ---------- 1) Descoberta: agenda -> ScanEvents ----------

    public async Task DiscoverAsync(CancellationToken ct)
    {
        // A lista de corridas não traz nome nem pista; a agenda traz, por série. Falhar aqui não pode
        // impedir a descoberta (o que importa é guardar o eventId), então só registra e segue sem título.
        var series = new Dictionary<string, ScheduleSeries>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var schedule = await _raceos.GetScheduleAsync(ct);
            foreach (var s in schedule?.Tiers.Values.SelectMany(v => v) ?? Enumerable.Empty<ScheduleSeries>())
                if (!string.IsNullOrEmpty(s.SeriesId)) series[s.SeriesId] = s;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Scanner: agenda indisponível; corridas novas ficam sem título por enquanto.");
        }

        var found = 0;
        foreach (var tier in Tiers)
        {
            var events = await _raceos.ListTierAsync(tier, take: 8, ct);
            foreach (var e in events)
            {
                if (string.IsNullOrWhiteSpace(e.Id)) continue;
                series.TryGetValue(e.SeriesId ?? "", out var info);

                var existing = await _db.ScanEvents.FirstOrDefaultAsync(x => x.EventId == e.Id, ct);
                if (existing is null)
                {
                    _db.ScanEvents.Add(new ScanEvent
                    {
                        EventId = e.Id,
                        SeriesId = e.SeriesId ?? "",
                        Title = e.Title ?? info?.EventName,
                        Track = info?.Track,
                        Scene = info?.Scene,
                        EventType = e.Type ?? "daily",
                        Tier = tier,
                        RaceStartsAt = (e.RaceStartsAt ?? DateTime.UtcNow).ToUniversalTime(),
                        RaceEndsAt = (e.RaceEndsAt ?? DateTime.UtcNow).ToUniversalTime(),
                        RaceOsStatus = e.Status,
                        Status = ScanEventStatus.Pending,
                    });
                    found++;
                }
                else if (existing.Status == ScanEventStatus.Pending)
                {
                    // Ainda não baixado: mantém horários/estado atualizados enquanto espera terminar.
                    existing.RaceOsStatus = e.Status;
                    if (e.RaceEndsAt is { } ends) existing.RaceEndsAt = ends.ToUniversalTime();
                    existing.Title ??= info?.EventName;
                    existing.Track ??= info?.Track;
                    existing.Scene ??= info?.Scene;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(ct);
        if (found > 0)
            _log.LogInformation("Scanner: {Count} novas corridas na agenda guardadas.", found);
    }

    // ---------- 2) Expansão: baixar resultados das que terminaram ----------

    public async Task FetchDueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var readyAfter = now - FirstFetchDelay;

        await RequeueBrokenAsync(now, ct);

        // Pendentes cujo fim previsto já passou (com folga) entram na fila.
        var toReady = await _db.ScanEvents
            .Where(x => x.Status == ScanEventStatus.Pending && x.RaceEndsAt < readyAfter)
            .ToListAsync(ct);
        foreach (var e in toReady) e.Status = ScanEventStatus.Ready;
        if (toReady.Count > 0) await _db.SaveChangesAsync(ct);

        // Da fila: as nunca tentadas e as que já esperaram o intervalo desde a última tentativa.
        var retryBefore = now - RetryEvery;
        var due = await _db.ScanEvents
            .Where(x => x.Status == ScanEventStatus.Ready && x.RaceEndsAt < readyAfter
                        && (x.FetchAttempts == 0 || x.UpdatedAt < retryBefore))
            .OrderBy(x => x.RaceEndsAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var e in due)
        {
            ct.ThrowIfCancellationRequested();
            string? reason;
            try
            {
                var outcome = await FetchEventAsync(e, now, ct);
                if (outcome == FetchOutcome.Fetched) continue;
                reason = "resultado ainda não publicado";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DiscardPendingChanges(); // o que ficou pela metade não pode ir junto com o registro da tentativa
                reason = ex.Message;
            }

            e.FetchAttempts++;
            e.UpdatedAt = DateTime.UtcNow;
            if (e.FetchAttempts >= MaxFetchAttempts)
            {
                e.Status = ScanEventStatus.Failed;
                _log.LogWarning("Scanner: desisti do evento {Event} após {Attempts} tentativas ({Reason}).",
                    e.EventId, e.FetchAttempts, reason);
            }
            else
            {
                _log.LogInformation("Scanner: evento {Event} fica para depois — {Reason} (tentativa {Attempt}).",
                    e.EventId, reason, e.FetchAttempts);
            }
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Desfaz inclusões/alterações ainda não salvas (tentativa que falhou no meio).</summary>
    private void DiscardPendingChanges()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;
            }
        }
    }

    /// <summary>
    /// Conserto de um bug antigo: corridas marcadas como baixadas sem nenhum split (o 404 de "ainda não
    /// publicado" era lido como "acabou"). Voltam para a fila enquanto o resultado ainda deve existir.
    /// </summary>
    private async Task RequeueBrokenAsync(DateTime now, CancellationToken ct)
    {
        var broken = await _db.ScanEvents
            .Where(x => x.Status == ScanEventStatus.Fetched
                        && (x.TotalSplits == null || x.TotalSplits > 0)
                        && !x.Splits.Any()
                        && x.RaceEndsAt > now.AddDays(-2))
            .ToListAsync(ct);
        if (broken.Count == 0) return;

        foreach (var e in broken)
        {
            e.Status = ScanEventStatus.Ready;
            e.FetchAttempts = 0;
            e.FetchedAt = null;
        }
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Scanner: {Count} corridas salvas sem splits voltaram para a fila.", broken.Count);
    }

    private async Task<FetchOutcome> FetchEventAsync(ScanEvent e, DateTime now, CancellationToken ct)
    {
        // ---- Fase 1: só rede. Nada no banco até termos o que gravar. ----
        var overview = await _raceos.GetOverviewAsync(e.EventType, e.EventId, ct);
        var total = Math.Min(overview.TotalSplits, MaxSplits);

        if (total <= 0)
        {
            // Sem splits: ninguém correu, ou o RaceOS ainda não fechou a corrida. Só aceitamos "vazia" depois
            // da carência — antes disso é "ainda não".
            if (now < e.RaceEndsAt + EmptyGrace) return FetchOutcome.NotReady;
            e.TotalSplits = 0;
            e.Status = ScanEventStatus.Fetched;
            e.FetchedAt = DateTime.UtcNow;
            e.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Scanner: evento {Event} terminou sem participantes.", e.EventId);
            return FetchOutcome.Fetched;
        }

        var acceptPartial = e.FetchAttempts >= AcceptPartialAfter;
        var results = new List<(int SplitNo, SplitResult Result)>();
        for (var n = 1; n <= total; n++)
        {
            var result = await _raceos.GetResultAsync(e.EventId, n, ct);
            if (result is null)
            {
                // 404 = esse split ainda não foi publicado. Tudo ou nada, salvo depois de ~1h esperando.
                if (acceptPartial && results.Count > 0) break;
                return FetchOutcome.NotReady;
            }
            results.Add((n, result));
        }

        // ---- Fase 2: grava tudo de uma vez. ----
        var old = await _db.RaceSplits.Where(s => s.ScanEventId == e.Id).ToListAsync(ct);
        if (old.Count > 0) _db.RaceSplits.RemoveRange(old);

        e.TotalSplits = overview.TotalSplits;
        foreach (var (n, result) in results)
        {
            if (e.Title is null) e.Title = result.Title;
            e.Track ??= result.Track;
            e.Scene ??= result.TrackScene;

            var split = new RaceSplit { ScanEventId = e.Id, SplitNo = n };
            var bySteam = result.Drivers
                .Where(d => !string.IsNullOrEmpty(d.SteamId))
                .GroupBy(d => d.SteamId!)
                .ToDictionary(g => g.Key, g => g.First());

            var rows = result.Race?.Races.FirstOrDefault();
            split.MostLapsCompleted = rows?.MostLapsCompleted ?? 0;

            foreach (var r in rows?.Results ?? new())
            {
                bySteam.TryGetValue(r.SteamId ?? "", out var meta);
                split.Results.Add(new RaceResult
                {
                    SteamId = r.SteamId,
                    RaceOsPlayerId = meta?.NkId ?? r.RaceControlId,
                    Name = r.Name ?? meta?.Name ?? "—",
                    Nationality = meta?.Nationality,
                    Badge = meta?.Badge,
                    CarClass = r.CarClass,
                    CarNumber = r.CarNumber,
                    CarType = r.CarType,
                    VehName = r.VehName,
                    TeamName = r.TeamName,
                    GridPos = ParseInt(r.GridPos),
                    Position = ParseInt(r.Position),
                    ClassGridPos = ParseInt(r.ClassGridPos),
                    ClassPosition = ParseInt(r.ClassPosition),
                    Laps = r.Laps,
                    BestLapTime = ParseDouble(r.BestLapTime),
                    FinishTime = ParseDouble(r.FinishTime),
                    FinishStatus = r.FinishStatus,
                    Points = r.Points,
                    DriverRankAdjustment = meta?.DriverRankAdjustment,
                    SafetyRankAdjustment = meta?.SafetyRankAdjustment,
                    JokerPlayed = meta?.JokerPlayed ?? false,
                });
            }

            split.DriverCount = split.Results.Count;
            _db.RaceSplits.Add(split);

            await UpsertDriversAsync(result.Drivers, ct);
        }

        e.Status = ScanEventStatus.Fetched;
        e.FetchedAt = DateTime.UtcNow;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Scanner: evento {Event} baixado ({Splits} de {Total} splits).",
            e.EventId, results.Count, overview.TotalSplits);
        return FetchOutcome.Fetched;
    }

    private async Task UpsertDriversAsync(IEnumerable<ResultDriver> drivers, CancellationToken ct)
    {
        foreach (var d in drivers)
        {
            if (string.IsNullOrEmpty(d.SteamId)) continue;
            // Procura primeiro no que já está na memória: um piloto incluído agora e ainda não salvo não
            // aparece na consulta ao banco, e incluí-lo de novo quebraria o índice único de SteamId.
            var drv = _db.Drivers.Local.FirstOrDefault(x => x.SteamId == d.SteamId)
                      ?? await _db.Drivers.FirstOrDefaultAsync(x => x.SteamId == d.SteamId, ct);
            if (drv is null)
            {
                drv = new Driver { SteamId = d.SteamId! };
                _db.Drivers.Add(drv);
            }
            drv.RaceOsPlayerId = d.NkId ?? d.RaceControlId ?? drv.RaceOsPlayerId;
            drv.Name = d.Name ?? drv.Name ?? "—";
            drv.Nationality = d.Nationality ?? drv.Nationality;
            drv.Badge = d.Badge ?? drv.Badge;
            drv.DriverRankTitle = d.DriverRank?.Rank ?? drv.DriverRankTitle;
            drv.DriverRankTier = d.DriverRank?.Tier ?? drv.DriverRankTier;
            drv.SafetyRankTitle = d.SafetyRank?.Rank ?? drv.SafetyRankTitle;
            drv.SafetyRankTier = d.SafetyRank?.Tier ?? drv.SafetyRankTier;
            drv.LastSeenAt = DateTime.UtcNow;
            drv.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static int? ParseInt(string? s) =>
        int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string? s) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
}
