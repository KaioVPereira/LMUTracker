using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    // special-events/{kind}: as telas "Weekly Races" e "Special Events" do jogo (mesmo formato, mesma tela por dentro).
    private static readonly string[] SpecialKinds = { "weekly", "special" };
    // championships/active{filtro}: os campeonatos normais, os de esports e os da VLMS são listas separadas no jogo.
    private static readonly string[] ChampionshipLists = { "", "?esports=true", "?vlms=true" };
    private const int MaxSplits = 40; // trava de segurança

    /// <summary>
    /// Versão do que o download grava. 2 = volta a volta, quali e DR/SR do piloto na corrida. 3 = classes da
    /// corrida e o resto do resultado guardado cru (incidentes, penalidades…). Corridas baixadas numa versão menor
    /// voltam para a fila (enquanto o resultado ainda existir) para ganhar o detalhe novo.
    /// </summary>
    private const int CurrentDetailVersion = 3;

    // Tetos do JSON cru comprimido — proteção de espaço caso o RaceOS mande algo muito maior que o esperado.
    private const int MaxConfigBytes = 64 * 1024;
    private const int MaxSplitExtraBytes = 256 * 1024;
    private const int MaxRowExtraBytes = 16 * 1024;

    /// <summary>Ordem das classes no site (das mais rápidas para as mais lentas).</summary>
    private static readonly string[] ClassOrder = { "Hyper", "LMP2", "LMP2_ELMS", "LMP3", "GT3", "LMGT3" };

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
        await RefreshLiveAsync(ct);
        await FetchDueAsync(ct);
        await BackfillIncidentsAsync(ct);
        await BackfillCrewAsync(ct);
    }

    /// <summary>
    /// Revezamentos das corridas baixadas antes de o scanner ler os "swaps": saem do JSON cru de cada linha, já
    /// guardado. O array drivers não foi guardado, então o SteamID do turno vem do índice de pilotos pelo nome
    /// (só quando o nome é único) e o saldo de DR/SR do turno fica vazio. Umas corridas por passada.
    /// </summary>
    public async Task<int> BackfillCrewAsync(CancellationToken ct)
    {
        var events = await _db.ScanEvents
            .Where(e => !e.CrewChecked && e.Status == ScanEventStatus.Fetched)
            .OrderByDescending(e => e.EventStartsAt ?? e.RaceStartsAt)
            .Take(100)
            .ToListAsync(ct);
        if (events.Count == 0) return 0;

        var ids = events.Select(e => e.Id).ToList();
        var rows = await _db.RaceResults
            .Where(r => ids.Contains(r.RaceSplit.ScanEventId) && r.ExtraData != null && !r.Crew.Any())
            .Select(r => new { r.Id, r.Name, r.SteamId, r.ExtraData, r.LapsData })
            .ToListAsync(ct);
        var crews = rows.Select(r => (Row: r, Stints: CrewReader.FromStored(r.ExtraData)))
            .Where(x => x.Stints is not null).ToList();

        var names = crews.SelectMany(x => x.Stints!.Select(s => s.Name)).Distinct().ToList();
        var steamByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in names.Chunk(500))
        {
            var found = await _db.Drivers.AsNoTracking().Where(d => chunk.Contains(d.Name))
                .Select(d => new { d.Name, d.SteamId }).ToListAsync(ct);
            foreach (var g in found.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1))
                steamByName[g.Key] = g.First().SteamId;
        }

        foreach (var (row, stints) in crews)
        {
            var laps = LapCodec.Decode(row.LapsData);
            foreach (var s in stints!)
                _db.RaceCrewStints.Add(new RaceCrewStint
                {
                    RaceResultId = row.Id, Seq = s.Seq, StartLap = s.StartLap, EndLap = s.EndLap,
                    Name = Cut(s.Name, RaceResult.NameMax)!,
                    BestLapTime = StintBest(laps, s),
                    SteamId = s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase) && row.SteamId is not null
                        ? row.SteamId : steamByName.GetValueOrDefault(s.Name),
                });
        }
        foreach (var e in events) e.CrewChecked = true;
        await _db.SaveChangesAsync(ct);
        if (crews.Count > 0)
            _log.LogInformation("Scanner: revezamentos lidos em {Cars} carros de {Events} corridas já guardadas.", crews.Count, events.Count);
        return events.Count;
    }

    /// <summary>
    /// Incidentes e penalidades das corridas baixadas antes de existir a contagem: saem do resultado cru que já está
    /// guardado (RaceSplit.ExtraData), sem baixar nada de novo. Uns splits por passada.
    /// </summary>
    public async Task<int> BackfillIncidentsAsync(CancellationToken ct)
    {
        var splits = await _db.RaceSplits
            .Where(s => s.ExtraData != null && s.Results.Any(r => r.Incidents == null))
            .OrderByDescending(s => s.CreatedAt)
            .Take(100)
            .Include(s => s.Results)
            .ToListAsync(ct);
        if (splits.Count == 0) return 0;

        foreach (var s in splits)
        {
            var counts = IncidentParser.CountFromStored(s.ExtraData, s.Results.Select(r => r.Name).Distinct().ToList());
            foreach (var r in s.Results)
            {
                var c = counts?.GetValueOrDefault(r.Name);
                r.Incidents = c?.Incidents ?? 0;
                r.Penalties = c?.Penalties ?? 0;
            }
        }
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Scanner: incidentes contados em {Count} splits já guardados.", splits.Count);
        return splits.Count;
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

        // Semanais e especiais: isoladas — se o formato vier diferente, só elas falham e as diárias seguem normais.
        foreach (var kind in SpecialKinds)
        {
            try
            {
                var series = await _raceos.ListSpecialAsync(kind, ct);
                var added = 0;
                foreach (var s in series)
                    foreach (var e in s.Events)
                        if (await UpsertEventAsync(e, e.Configuration?.Tier?.Name ?? s.Tier ?? kind, kind, ct, s.Title, s.Configuration))
                            added++;
                if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
                if (added > 0) _log.LogInformation("Scanner: {Count} corridas '{Kind}' novas.", added, kind);
                found += added;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DiscardPendingChanges();
                _log.LogWarning(ex, "Scanner: não consegui ler os eventos '{Kind}'; diárias seguem normais.", kind);
            }
        }

        // Campeonatos: também isolados (formato lido do código do jogo, ainda não visto ao vivo).
        try
        {
            found += await DiscoverChampionshipsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DiscardPendingChanges();
            _log.LogWarning(ex, "Scanner: não consegui ler os campeonatos; o resto segue normal.");
        }

        if (found > 0)
            _log.LogInformation("Scanner: {Count} novas corridas na agenda guardadas.", found);
    }

    /// <summary>
    /// Horários das rodadas dos campeonatos ativos. Cada horário é uma corrida (eventId) com splits, baixada como
    /// as outras pelo event/overview com eventType "championship". Ver <see cref="Championships"/>.
    /// </summary>
    private async Task<int> DiscoverChampionshipsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>();
        foreach (var filter in ChampionshipLists)
        {
            try
            {
                ids.UnionWith(Championships.Ids(await _raceos.GetRawAsync($"/api/v1/championships/active{filter}", ct)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogDebug(ex, "championships/active{Filter} falhou.", filter);
            }
        }

        var added = 0;
        var slots = 0;
        foreach (var id in ids)
        {
            var detail = await _raceos.GetRawAsync($"/api/v1/championship/{id}", ct);
            foreach (var slot in Championships.Slots(detail, id))
            {
                slots++;
                var cfg = new EventConfiguration
                {
                    Title = slot.Title,
                    Starts = slot.Starts,
                    Tier = new EventTier { Name = "championship" },
                    Content = new EventContent { Track = new EventTrack { Name = slot.Scene, Friendly = slot.TrackName } },
                    Extra = new() { ["championship"] = JsonSerializer.SerializeToElement(slot.Info) },
                };
                var e = new RaceOsEvent
                {
                    Id = slot.EventId, SeriesId = id, Type = "championship", Starts = slot.Starts, RaceEndsAt = slot.Ends,
                    Configuration = cfg,
                };
                if (await UpsertEventAsync(e, "championship", "championship", ct)) added++;
            }
        }
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
        if (added > 0)
            _log.LogInformation("Scanner: {Count} corridas de campeonato novas ({Champs} campeonatos, {Slots} horários).",
                added, ids.Count, slots);
        return added;
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

        // Configuração inteira (sessões com horário/clima, regras, carros) + o que a instância traz a mais.
        var configData = JsonBlob.Encode(new { configuration = cfg, instance = JsonBlob.Keep(e.Extra, "configuration") },
            MaxConfigBytes);

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
                ConfigData = configData,
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
            if (configData is not null) existing.ConfigData = configData;
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

    // ---------- 1b) Ao vivo: retrato do evento em andamento ----------

    // O RaceOS não publica a cronometragem durante a corrida; o que ele diz é o estado do evento (status, splits
    // abertos, inscritos por classe). Relido a cada 10 min do começo (com folga antes, para os inscritos) até o fim.
    private static readonly TimeSpan LiveEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LiveLead = TimeSpan.FromMinutes(30);
    private const int LivePerPass = 15;

    /// <param name="force">Relê agora todos os eventos da janela, sem esperar os intervalos (comando live-once).</param>
    public async Task<int> RefreshLiveAsync(CancellationToken ct, bool force = false)
    {
        var now = DateTime.UtcNow;
        var startsBefore = now + LiveLead;
        var staleBefore = force ? now : now - LiveEvery;
        var due = await _db.ScanEvents
            .Where(e => e.Status == ScanEventStatus.Pending && e.RaceEndsAt > now
                        && (e.EventStartsAt ?? e.RaceStartsAt) <= startsBefore
                        && (e.LiveCheckedAt == null || e.LiveCheckedAt < staleBefore))
            .OrderBy(e => e.LiveCheckedAt)
            .Take(LivePerPass)
            .ToListAsync(ct);

        var ok = 0;
        foreach (var e in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await RefreshLiveEventAsync(e, now, force, ct)) ok++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("Scanner: retrato ao vivo de {Event} falhou: {Reason}", e.EventId, ex.Message);
            }
            e.LiveCheckedAt = now; // também na falha: tenta de novo só no próximo intervalo
        }
        if (due.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Scanner: retrato ao vivo de {Ok}/{Count} eventos em andamento.", ok, due.Count);
        }
        return ok;
    }

    private static readonly TimeSpan EntriesEvery = TimeSpan.FromMinutes(30);
    private const int EntriesPageSize = 40;  // o mesmo do jogo
    private const int EntriesMaxPages = 60;  // ~2.400 inscritos

    /// <summary>
    /// Overview → retrato (números) + detalhe (overview sem dados sensíveis). Quando os splits ainda não têm pilotos
    /// (antes da largada, ou se o RaceOS não mandar), a lista vem de event/entries, relida no máximo a cada 30 min.
    /// </summary>
    private async Task<bool> RefreshLiveEventAsync(ScanEvent e, DateTime now, bool force, CancellationToken ct)
    {
        var (type, overview) = await LiveOverviewAsync(e, ct);
        var snapshot = LiveSnapshot.FromOverview(overview);
        if (snapshot is null) return false;
        // Os splits acabaram de se formar (0 → N): relê a lista na hora, para ela ser a da divisão.
        var formed = LiveSnapshot.SplitsOf(e.LiveData) == 0 && LiveSnapshot.SplitsOf(snapshot) > 0;
        e.LiveData = snapshot;

        JsonArray? entries = null;
        DateTime? entriesAt = null;
        if (!LiveDetail.SplitsHaveDrivers(overview))
        {
            if (force || formed || e.LiveEntriesAt is null || e.LiveEntriesAt < now - EntriesEvery)
            {
                try
                {
                    entries = await LiveEntriesAsync(type, e.EventId, ct);
                    entriesAt = now;
                    e.LiveEntriesAt = now;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning("Scanner: inscritos de {Event} falharam (fica a lista anterior): {Reason}", e.EventId, ex.Message);
                }
            }
            if (entries is null)
            {
                entries = LiveDetail.StoredEntries(e.LiveDetail);
                entriesAt = e.LiveEntriesAt;
            }
        }
        e.LiveDetail = LiveDetail.Encode(LiveDetail.Clean(overview), entries, entriesAt) ?? e.LiveDetail;
        return true;
    }

    /// <summary>
    /// Overview cru e o eventType que respondeu. Semanais/especiais: os dois tipos respondem, mas só um traz os
    /// inscritos (visto ao vivo nas 24h de Le Mans: "special" volta com 0 inscritos) — fica o mais completo.
    /// </summary>
    private async Task<(string Type, JsonElement Overview)> LiveOverviewAsync(ScanEvent e, CancellationToken ct)
    {
        var types = e.EventType is "weekly" or "special" ? new[] { e.EventType, "specialevent" } : new[] { e.EventType };
        Exception? last = null;
        (string Type, JsonElement Overview, int Score)? best = null;
        foreach (var type in types)
        {
            try
            {
                var o = await _raceos.GetOverviewRawAsync(type, e.EventId, ct);
                if (LiveSnapshot.FromOverview(o) is null) continue;
                var score = Count(o, "totalSplits")
                            + (o.TryGetProperty("registered", out var r) && r.ValueKind == JsonValueKind.Object ? Count(r, "total") : 0);
                if (best is null || score > best.Value.Score) best = (type, o, score);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { last = ex; }
        }
        if (best is { } b) return (b.Type, b.Overview);
        throw last ?? new InvalidOperationException("event/overview sem dados");

        static int Count(JsonElement x, string name) =>
            x.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
    }

    /// <summary>Lista de inscritos (event/entries), paginada como o jogo faz, sem os dados sensíveis.</summary>
    private async Task<JsonArray> LiveEntriesAsync(string type, string eventId, CancellationToken ct)
    {
        var all = new List<JsonElement>();
        for (var page = 1; page <= EntriesMaxPages; page++)
        {
            var raw = await _raceos.GetRawAsync(
                $"/api/v1/event/entries/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(eventId)}?page={page}&take={EntriesPageSize}", ct);
            var list = raw.ValueKind == JsonValueKind.Array ? raw
                : raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array ? d
                : default;
            if (list.ValueKind != JsonValueKind.Array) break;
            all.AddRange(list.EnumerateArray().Select(x => x.Clone()));
            if (list.GetArrayLength() < EntriesPageSize) break;
        }
        return LiveDetail.CleanEntries(all);
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
    /// Nº de splits pelo event/overview. Semanais e especiais: a lista do jogo chama de "weekly"/"special", mas a
    /// série se declara "specialevent" (é esse o tipo nos resultados do jogo); se um não responder, tenta o outro.
    /// </summary>
    private async Task<int> GetTotalSplitsAsync(ScanEvent e, CancellationToken ct)
    {
        if (e.EventType is not ("weekly" or "special"))
            return (await _raceos.GetOverviewAsync(e.EventType, e.EventId, ct)).TotalSplits;

        Exception? last = null;
        var best = 0;
        foreach (var type in new[] { e.EventType, "specialevent" })
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
        var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            var split = new RaceSplit
            {
                ScanEventId = e.Id,
                SplitNo = n,
                // Só o que não vai para colunas: drivers/race/qualifying já são gravados; o treino é pesado e não usamos.
                ExtraData = JsonBlob.Encode(new
                {
                    result = JsonBlob.Keep(result.Extra, "practice"),
                    race = JsonBlob.Keep(result.Race?.Extra),
                    raceBlock = JsonBlob.Keep(result.Race?.Races.FirstOrDefault()?.Extra),
                    qualifying = JsonBlob.Keep(result.Qualifying?.Extra),
                    qualifyingBlock = JsonBlob.Keep(result.Qualifying?.Races.FirstOrDefault()?.Extra),
                }, MaxSplitExtraBytes),
            };
            var bySteam = result.Drivers
                .Where(d => !string.IsNullOrEmpty(d.SteamId))
                .GroupBy(d => d.SteamId!)
                .ToDictionary(g => g.Key, g => g.First());
            // Nomes repetidos no split ficam de fora: casar o turno com o piloto errado é pior que não casar.
            var byName = result.Drivers
                .Where(d => !string.IsNullOrEmpty(d.SteamId) && !string.IsNullOrWhiteSpace(d.Name))
                .GroupBy(d => d.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(d => d.SteamId).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var rows = result.Race?.Races.FirstOrDefault();
            split.MostLapsCompleted = rows?.MostLapsCompleted ?? 0;

            // Quali do mesmo split, por SteamID (posição geral, na classe e melhor volta).
            var quali = (result.Qualifying?.Races.FirstOrDefault()?.Results ?? new())
                .Where(q => !string.IsNullOrEmpty(q.SteamId))
                .GroupBy(q => q.SteamId!)
                .ToDictionary(g => g.Key, g => g.First());

            // Incidentes e penalidades por nome, do stream da corrida (o stream não traz SteamID).
            JsonElement? streams = null;
            if (rows?.Extra is { } rx && rx.TryGetValue("streams", out var st)) streams = st;
            var incidents = IncidentParser.Count(streams, (rows?.Results ?? new()).Select(x => x.Name ?? "").Distinct().ToList());

            foreach (var r in rows?.Results ?? new())
            {
                bySteam.TryGetValue(r.SteamId ?? "", out var meta);
                var inc = incidents.GetValueOrDefault(r.Name ?? "");
                quali.TryGetValue(r.SteamId ?? "", out var q);
                var laps = ToLaps(r.LapList);
                if (!string.IsNullOrWhiteSpace(r.CarClass)) classes.Add(r.CarClass);
                var rowExtra = JsonBlob.Keep(r.Extra);
                var row = new RaceResult
                {
                    DriverRankTitle = Cut(meta?.DriverRank?.Rank, RaceResult.RankTitleMax),
                    DriverRankTier = meta?.DriverRank?.Tier,
                    DriverRankProgress = RoundProgress(meta?.DriverRank?.Progress),
                    SafetyRankTitle = Cut(meta?.SafetyRank?.Rank, RaceResult.RankTitleMax),
                    SafetyRankTier = meta?.SafetyRank?.Tier,
                    SafetyRankProgress = RoundProgress(meta?.SafetyRank?.Progress),
                    QualiPosition = ParseInt(q?.Position),
                    QualiClassPosition = ParseInt(q?.ClassPosition),
                    QualiBestLapTime = ParseDouble(q?.BestLapTime),
                    PitStops = laps.Count(l => l.Pit),
                    Incidents = inc?.Incidents ?? 0,
                    Penalties = inc?.Penalties ?? 0,
                    LapsData = LapCodec.Encode(laps),
                    ExtraData = rowExtra is null ? null : JsonBlob.Encode(rowExtra, MaxRowExtraBytes),
                    // Piloto fora do array drivers chega com SteamId "" — vazio vira null para não juntar todos num só.
                    SteamId = string.IsNullOrWhiteSpace(r.SteamId) ? null : r.SteamId,
                    RaceOsPlayerId = meta?.NkId ?? r.RaceControlId,
                    Name = Cut(r.Name ?? meta?.Name, RaceResult.NameMax) ?? "—",
                    Nationality = Cut(meta?.Nationality, RaceResult.NationalityMax),
                    Badge = meta?.Badge,
                    CarClass = Cut(r.CarClass, RaceResult.CarClassMax),
                    CarNumber = r.CarNumber,
                    CarType = Cut(r.CarType, RaceResult.CarTypeMax),
                    VehName = r.VehName,
                    TeamName = r.TeamName,
                    GridPos = ParseInt(r.GridPos),
                    Position = ParseInt(r.Position),
                    ClassGridPos = ParseInt(r.ClassGridPos),
                    ClassPosition = ParseInt(r.ClassPosition),
                    Laps = r.Laps,
                    BestLapTime = ParseDouble(r.BestLapTime),
                    FinishTime = ParseDouble(r.FinishTime),
                    FinishStatus = Cut(r.FinishStatus, RaceResult.FinishStatusMax),
                    Points = r.Points,
                    DriverRankAdjustment = meta?.DriverRankAdjustment,
                    SafetyRankAdjustment = meta?.SafetyRankAdjustment,
                    JokerPlayed = meta?.JokerPlayed ?? false,
                };
                // Carro dividido: cada turno com o SteamID e o saldo de DR/SR do piloto, casados pelo nome no
                // array drivers do split (os turnos só trazem o nome).
                foreach (var s in CrewReader.FromRowExtra(r.Extra) ?? [])
                {
                    var m = s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase) && row.SteamId is not null
                        ? meta : byName.GetValueOrDefault(s.Name);
                    row.Crew.Add(new RaceCrewStint
                    {
                        Seq = s.Seq, StartLap = s.StartLap, EndLap = s.EndLap,
                        Name = Cut(s.Name, RaceResult.NameMax)!,
                        BestLapTime = StintBest(laps, s),
                        SteamId = m?.SteamId ?? (s.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase) ? row.SteamId : null),
                        DriverRankAdjustment = m?.DriverRankAdjustment,
                        SafetyRankAdjustment = m?.SafetyRankAdjustment,
                    });
                }
                split.Results.Add(row);
            }

            split.DriverCount = split.Results.Count;
            _db.RaceSplits.Add(split);

            await UpsertDriversAsync(result.Drivers, e.EventStartsAt ?? e.RaceStartsAt, ct);
        }

        e.Classes = OrderClasses(classes);
        e.CrewChecked = true;
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

    /// <summary>Melhor volta válida (sem pit) dentro de um turno de piloto.</summary>
    private static double? StintBest(IEnumerable<Application.Lmu.LapDto> laps, CrewReader.Stint s) =>
        laps.Where(l => l.Lap >= s.StartLap && l.Lap <= s.EndLap && !l.Pit && l.LapTime is > 0).Select(l => l.LapTime).Min();

    /// <summary>"GT3,Hyper" na ordem do site; classes desconhecidas vão no fim, em ordem alfabética.</summary>
    private static string? OrderClasses(IEnumerable<string> classes)
    {
        var list = classes
            .OrderBy(c => Array.FindIndex(ClassOrder, o => o.Equals(c, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count > 0 ? string.Join(",", list) : null;
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

    /// <summary>Corta no tamanho da coluna: um texto maior que o previsto não pode derrubar a gravação da corrida.</summary>
    private static string? Cut(string? s, int max) => s is { Length: var n } && n > max ? s[..max] : s;

    private static int? ParseInt(string? s) =>
        int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string? s) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
}
