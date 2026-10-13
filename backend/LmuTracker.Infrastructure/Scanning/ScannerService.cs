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
    private static readonly string[] SpecialKinds = { "weekly" }; // "special" (eventos especiais) é o mesmo formato
    private const int MaxSplits = 40; // trava de segurança

    /// <summary>
    /// Versão do que o download grava. 2 = volta a volta, quali e DR/SR do piloto na corrida. Corridas baixadas
    /// numa versão menor voltam para a fila (enquanto o resultado ainda existir) para ganhar o detalhe novo.
    /// </summary>
    private const int CurrentDetailVersion = 2;

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
        var found = 0;

        // Diárias: a próxima leva de cada nível (uma corrida a cada 15/20/30 min).
        foreach (var tier in Tiers)
        {
            var events = await _raceos.ListTierAsync(tier, take: 8, ct);
            foreach (var e in events)
                if (await UpsertEventAsync(e, tier, "daily", ct)) found++;
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);

        // Semanais: isoladas — se o formato vier diferente, só elas falham e as diárias seguem normais.
        foreach (var kind in SpecialKinds)
        {
            try
            {
                var series = await _raceos.ListSpecialAsync(kind, ct);
                var weekly = 0;
                foreach (var s in series)
                    foreach (var e in s.Events)
                        if (await UpsertEventAsync(e, e.Configuration?.Tier?.Name ?? s.Tier ?? kind, kind, ct, s.Title, s.Configuration))
                            weekly++;
                if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
                found += weekly;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DiscardPendingChanges();
                _log.LogWarning(ex, "Scanner: não consegui ler os eventos '{Kind}'; diárias seguem normais.", kind);
            }
        }

        if (found > 0)
            _log.LogInformation("Scanner: {Count} novas corridas na agenda guardadas.", found);
    }

    /// <summary>Grava (ou atualiza, se ainda pendente) uma corrida descoberta. Devolve true se for nova.</summary>
    private async Task<bool> UpsertEventAsync(
        RaceOsEvent e, string tier, string defaultType, CancellationToken ct,
        string? seriesTitle = null, EventConfiguration? seriesConfig = null)
    {
        if (string.IsNullOrWhiteSpace(e.Id)) return false;

        // Nome, largada oficial e pista vêm da configuração de cada corrida (é o que o jogo mostra).
        // Nas semanais a configuração é da série e a instância só traz a própria largada.
        var cfg = e.Configuration ?? seriesConfig;
        var track = cfg?.Content?.Track;
        var title = cfg?.Title ?? seriesTitle;
        var trackName = TrackNames.Display(track?.Friendly, track?.Name);
        var eventStarts = (e.Configuration?.Starts ?? e.Starts)?.ToUniversalTime();

        // Sem horário de sessão de corrida (comum fora das diárias): fim = largada + duração das sessões,
        // como o jogo calcula. Sem nem isso, não dá para saber quando baixar — ignora.
        var raceEnds = e.RaceEndsAt?.ToUniversalTime()
            ?? (eventStarts is { } st ? st.AddMinutes(SessionMinutes(cfg)) : null);
        if (raceEnds is null)
        {
            _log.LogWarning("Scanner: corrida {Event} sem horário de fim; ignorada.", e.Id);
            return false;
        }
        var raceStarts = e.RaceStartsAt?.ToUniversalTime() ?? eventStarts ?? raceEnds.Value;

        var existing = _db.ScanEvents.Local.FirstOrDefault(x => x.EventId == e.Id)
                       ?? await _db.ScanEvents.FirstOrDefaultAsync(x => x.EventId == e.Id, ct);
        if (existing is null)
        {
            _db.ScanEvents.Add(new ScanEvent
            {
                EventId = e.Id,
                SeriesId = e.SeriesId ?? "",
                Title = title,
                Track = trackName,
                Scene = track?.Name,
                EventType = e.Type ?? defaultType,
                Tier = tier,
                EventStartsAt = eventStarts,
                RaceStartsAt = raceStarts,
                RaceEndsAt = raceEnds.Value,
                RaceOsStatus = e.Status,
                Status = ScanEventStatus.Pending,
            });
            return true;
        }

        if (existing.Status == ScanEventStatus.Pending)
        {
            // Ainda não baixado: atualiza estado e horários. Nome/pista são sobrescritos de propósito:
            // uma versão anterior do scanner gravou nomes errados (cruzava pelo seriesId).
            existing.RaceOsStatus = e.Status;
            existing.RaceStartsAt = raceStarts;
            existing.RaceEndsAt = raceEnds.Value;
            if (eventStarts is not null) existing.EventStartsAt = eventStarts;
            if (title is not null) existing.Title = title;
            if (trackName is not null) existing.Track = trackName;
            if (track?.Name is not null) existing.Scene = track.Name;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        return false;
    }

    /// <summary>Duração total do evento em minutos: soma das sessões + 5 (mesma conta do jogo).</summary>
    private static double SessionMinutes(EventConfiguration? cfg)
    {
        var total = 0.0;
        foreach (var s in cfg?.Sessions ?? new())
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(s.Duration ?? "", @"(\d+)([hms])"))
            {
                var v = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                total += m.Groups[2].Value switch { "h" => v * 60, "m" => v, _ => v / 60.0 };
            }
        return total + 5;
    }

    // ---------- 2) Expansão: baixar resultados das que terminaram ----------

    public async Task FetchDueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var readyAfter = now - FirstFetchDelay;

        await RequeueBrokenAsync(now, ct);
        await RequeueForDetailsAsync(now, ct);

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

    /// <summary>
    /// Nº de splits pelo event/overview. Semanais: o jogo chama de "weekly", mas a série se declara "specialevent";
    /// se um não responder, tenta o outro.
    /// </summary>
    private async Task<int> GetTotalSplitsAsync(ScanEvent e, CancellationToken ct)
    {
        if (e.EventType != "weekly")
            return (await _raceos.GetOverviewAsync(e.EventType, e.EventId, ct)).TotalSplits;

        Exception? last = null;
        var best = 0;
        foreach (var type in new[] { "weekly", "specialevent" })
        {
            try
            {
                best = Math.Max(best, (await _raceos.GetOverviewAsync(type, e.EventId, ct)).TotalSplits);
                if (best > 0) return best;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { last = ex; }
        }
        return best > 0 || last is null ? best : throw last;
    }

    private async Task<FetchOutcome> FetchEventAsync(ScanEvent e, DateTime now, CancellationToken ct)
    {
        // ---- Fase 1: só rede. Nada no banco até termos o que gravar. ----
        var reported = await GetTotalSplitsAsync(e, ct);
        var total = Math.Min(reported, MaxSplits);

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

        e.TotalSplits = reported;
        foreach (var (n, result) in results)
        {
            // O resultado é a fonte final de nome, pista e largada — corrige também as corridas gravadas pela
            // versão antiga do scanner (que cruzava pelo seriesId e errava os três).
            e.Title = result.Title ?? e.Title;
            var scene = !string.IsNullOrEmpty(result.TrackScene) ? result.TrackScene : result.Track;
            if (!string.IsNullOrEmpty(scene))
            {
                e.Scene = scene;
                e.Track = TrackNames.Display(null, scene);
            }
            if (result.Date is { } officialStart) e.EventStartsAt = officialStart.ToUniversalTime();

            var split = new RaceSplit { ScanEventId = e.Id, SplitNo = n };
            var bySteam = result.Drivers
                .Where(d => !string.IsNullOrEmpty(d.SteamId))
                .GroupBy(d => d.SteamId!)
                .ToDictionary(g => g.Key, g => g.First());

            var rows = result.Race?.Races.FirstOrDefault();
            split.MostLapsCompleted = rows?.MostLapsCompleted ?? 0;

            // Quali do mesmo split, por SteamID (posição geral, na classe e melhor volta).
            var quali = (result.Qualifying?.Races.FirstOrDefault()?.Results ?? new())
                .Where(q => !string.IsNullOrEmpty(q.SteamId))
                .GroupBy(q => q.SteamId!)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var r in rows?.Results ?? new())
            {
                bySteam.TryGetValue(r.SteamId ?? "", out var meta);
                quali.TryGetValue(r.SteamId ?? "", out var q);
                var laps = ToLaps(r.LapList);
                split.Results.Add(new RaceResult
                {
                    DriverRankTitle = meta?.DriverRank?.Rank,
                    DriverRankTier = meta?.DriverRank?.Tier,
                    DriverRankProgress = RoundProgress(meta?.DriverRank?.Progress),
                    SafetyRankTitle = meta?.SafetyRank?.Rank,
                    SafetyRankTier = meta?.SafetyRank?.Tier,
                    SafetyRankProgress = RoundProgress(meta?.SafetyRank?.Progress),
                    QualiPosition = ParseInt(q?.Position),
                    QualiClassPosition = ParseInt(q?.ClassPosition),
                    QualiBestLapTime = ParseDouble(q?.BestLapTime),
                    PitStops = laps.Count(l => l.Pit),
                    LapsData = LapCodec.Encode(laps),
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

            await UpsertDriversAsync(result.Drivers, e.EventStartsAt ?? e.RaceStartsAt, ct);
        }

        e.Status = ScanEventStatus.Fetched;
        e.DetailVersion = CurrentDetailVersion;
        e.FetchedAt = DateTime.UtcNow;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Scanner: evento {Event} baixado ({Splits} de {Total} splits).",
            e.EventId, results.Count, reported);
        return FetchOutcome.Fetched;
    }

    /// <summary>Converte o volta a volta do RaceOS (tudo em texto) para o formato guardado.</summary>
    private static List<Application.Lmu.LapDto> ToLaps(List<LapRow>? rows)
    {
        var laps = new List<Application.Lmu.LapDto>();
        foreach (var l in rows ?? new())
        {
            var n = ParseInt(l.Num);
            if (n is null) continue;
            var pit = !string.IsNullOrWhiteSpace(l.Pit) && l.Pit.Trim() != "0";
            laps.Add(new Application.Lmu.LapDto(
                n.Value, ParseInt(l.Position), ParseDouble(l.LapTime),
                ParseDouble(l.S1), ParseDouble(l.S2), ParseDouble(l.S3), ParseDouble(l.Elapsed),
                ParseFraction(l.Fuel), Compound(l.FrontCompound), Compound(l.RearCompound),
                ParseFraction(l.WearFL), ParseFraction(l.WearFR), ParseFraction(l.WearRL), ParseFraction(l.WearRR),
                pit));
        }
        return laps;
    }

    /// <summary>"0,Medium" -> "Medium".</summary>
    private static string? Compound(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var i = raw.IndexOf(',');
        return (i >= 0 ? raw[(i + 1)..] : raw).Trim();
    }

    /// <summary>Fração 0..1 (combustível, desgaste). Aceita zero, ao contrário de ParseDouble.</summary>
    private static double? ParseFraction(string? s) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int? RoundProgress(double? p) => p is null ? null : (int)Math.Round(p.Value);

    /// <summary>
    /// Corridas baixadas antes do volta a volta existir voltam para a fila uma vez, enquanto o RaceOS ainda
    /// tem o resultado — ganham voltas, quali e DR/SR por corrida sem perder nada (o download é tudo ou nada).
    /// </summary>
    private async Task RequeueForDetailsAsync(DateTime now, CancellationToken ct)
    {
        var stale = await _db.ScanEvents
            .Where(x => x.Status == ScanEventStatus.Fetched
                        && x.DetailVersion < CurrentDetailVersion
                        && x.TotalSplits > 0
                        && x.RaceEndsAt > now.AddDays(-2))
            .ToListAsync(ct);
        if (stale.Count == 0) return;

        foreach (var e in stale)
        {
            e.Status = ScanEventStatus.Ready;
            e.FetchAttempts = 0;
        }
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Scanner: {Count} corridas voltaram para a fila para ganhar o volta a volta.", stale.Count);
    }

    private async Task UpsertDriversAsync(IEnumerable<ResultDriver> drivers, DateTime raceTime, CancellationToken ct)
    {
        foreach (var d in drivers)
        {
            if (string.IsNullOrEmpty(d.SteamId)) continue;
            // Procura primeiro no que já está na memória: um piloto incluído agora e ainda não salvo não
            // aparece na consulta ao banco, e incluí-lo de novo quebraria o índice único de SteamId.
            var drv = _db.Drivers.Local.FirstOrDefault(x => x.SteamId == d.SteamId)
                      ?? await _db.Drivers.FirstOrDefaultAsync(x => x.SteamId == d.SteamId, ct);
            var isNew = drv is null;
            if (drv is null)
            {
                drv = new Driver { SteamId = d.SteamId!, Name = d.Name ?? "—", LastSeenAt = raceTime };
                _db.Drivers.Add(drv);
            }
            // O retrato (nome, rank) só avança: uma corrida mais antiga reprocessada depois não pode
            // sobrescrever o rank de uma mais nova. LastSeenAt guarda a largada da corrida do retrato.
            if (!isNew && raceTime < drv.LastSeenAt) continue;

            drv.RaceOsPlayerId = d.NkId ?? d.RaceControlId ?? drv.RaceOsPlayerId;
            drv.Name = d.Name ?? drv.Name ?? "—";
            drv.Nationality = d.Nationality ?? drv.Nationality;
            drv.Badge = d.Badge ?? drv.Badge;
            drv.DriverRankTitle = d.DriverRank?.Rank ?? drv.DriverRankTitle;
            drv.DriverRankTier = d.DriverRank?.Tier ?? drv.DriverRankTier;
            drv.DriverRankProgress = RoundProgress(d.DriverRank?.Progress) ?? drv.DriverRankProgress;
            drv.SafetyRankTitle = d.SafetyRank?.Rank ?? drv.SafetyRankTitle;
            drv.SafetyRankTier = d.SafetyRank?.Tier ?? drv.SafetyRankTier;
            drv.SafetyRankProgress = RoundProgress(d.SafetyRank?.Progress) ?? drv.SafetyRankProgress;
            drv.LastSeenAt = raceTime;
            drv.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static int? ParseInt(string? s) =>
        int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string? s) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
}
