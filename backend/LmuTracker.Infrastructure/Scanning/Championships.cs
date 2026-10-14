using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Leitura dos campeonatos do RaceOS. O formato veio do código da interface do jogo (Bin/UI.zip), não de uma
/// resposta vista ao vivo, então tudo aqui é tolerante: campo ausente ou de outro tipo só faz o horário ser pulado.
/// <list type="bullet">
/// <item><c>championships/active[?esports=true|?vlms=true]</c>: lista dos campeonatos (cada um com <c>id</c>);</item>
/// <item><c>championship/{id}</c>: <c>name</c>, <c>tier.name</c>, <c>rounds[]</c> (<c>num</c>, <c>current</c>,
/// <c>startsAt</c>/<c>endsAt</c>, <c>track.subItems[0].scn</c>, <c>sessionPreset.Player["Game Options"]["Race Time"]</c>)
/// e <c>events[]</c> = horários da rodada (<c>{ eventId, time }</c>) — cada horário é uma corrida com splits,
/// baixada pelo <c>event/overview</c> com eventType "championship".</item>
/// </list>
/// </summary>
public static class Championships
{
    /// <summary>Sem a duração da corrida na rodada: estimativa do fim (o download re-tenta por ~3 h depois disso).</summary>
    private const int FallbackMinutes = 90;
    /// <summary>Treino + classificação antes da corrida (a rodada só informa o tempo de corrida).</summary>
    private const int BeforeRaceMinutes = 35;

    public sealed record Slot(
        string EventId, DateTime Starts, DateTime Ends, string Title, string? Scene, string? TrackName, JsonObject Info);

    /// <summary>Ids dos campeonatos de uma lista (array direto ou <c>{ championships: [...] }</c>).</summary>
    public static IEnumerable<string> Ids(JsonElement list)
    {
        var items = list.ValueKind == JsonValueKind.Array ? list
            : list.ValueKind == JsonValueKind.Object && list.TryGetProperty("championships", out var c) && c.ValueKind == JsonValueKind.Array ? c
            : default;
        if (items.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in items.EnumerateArray())
            if ((Str(item, "id") ?? Str(item, "_id")) is { Length: > 0 } id)
                yield return id;
    }

    /// <summary>Horários (corridas) de um campeonato, com a rodada de cada um.</summary>
    public static IEnumerable<Slot> Slots(JsonElement c, string championshipId)
    {
        if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            yield break;

        var name = Str(c, "name") ?? Str(c, "title") ?? "Campeonato";
        var rounds = c.TryGetProperty("rounds", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().ToList() : new List<JsonElement>();

        foreach (var ev in events.EnumerateArray())
        {
            var eventId = Str(ev, "eventId") ?? Str(ev, "id");
            var time = Date(ev, "time") ?? Date(ev, "starts");
            if (string.IsNullOrWhiteSpace(eventId) || time is null) continue;

            // A rodada do horário: a que contém a data; senão a atual.
            var idx = rounds.FindIndex(x => Date(x, "startsAt") is { } a && Date(x, "endsAt") is { } b && a <= time && time < b);
            if (idx < 0) idx = rounds.FindIndex(x => x.TryGetProperty("current", out var cur) && cur.ValueKind == JsonValueKind.True);
            JsonElement? round = idx >= 0 ? rounds[idx] : null;
            var num = round is { } rd && rd.TryGetProperty("num", out var n) && n.TryGetInt32(out var nv) ? nv : idx + 1;

            string? scene = null, trackName = null;
            if (round is { } rr && rr.TryGetProperty("track", out var track) && track.ValueKind == JsonValueKind.Object)
            {
                trackName = Str(track, "name");
                if (track.TryGetProperty("subItems", out var sub) && sub.ValueKind == JsonValueKind.Array && sub.GetArrayLength() > 0)
                    scene = Str(sub[0], "scn");
            }

            var raceMinutes = round is { } r2 ? RaceMinutes(r2) : null;
            var ends = time.Value.AddMinutes(raceMinutes is > 0 ? raceMinutes.Value + BeforeRaceMinutes : FallbackMinutes);

            var info = new JsonObject
            {
                ["id"] = championshipId,
                ["name"] = name,
                ["type"] = Str(c, "type"),
                ["tier"] = c.TryGetProperty("tier", out var tier) && tier.ValueKind == JsonValueKind.Object ? Str(tier, "name") : Str(c, "tier"),
                ["round"] = num > 0 ? num : null,
                ["rounds"] = rounds.Count,
                ["raceMinutes"] = raceMinutes,
            };
            // A rodada inteira (sessões, regras) sem os resultados de rodadas passadas que ela pode carregar.
            if (round is { } r3 && JsonNode.Parse(r3.GetRawText()) is JsonObject ro)
            {
                ro.Remove("race");
                ro.Remove("qualifying");
                info["roundData"] = ro;
            }

            var title = num > 0 && rounds.Count > 1 ? $"{name} · Rodada {num}" : name;
            yield return new Slot(eventId, time.Value, ends, title, scene, trackName, info);
        }
    }

    /// <summary><c>sessionPreset.Player["Game Options"]["Race Time"]</c> (minutos, como no rFactor).</summary>
    private static int? RaceMinutes(JsonElement round)
    {
        if (!round.TryGetProperty("sessionPreset", out var sp) || sp.ValueKind != JsonValueKind.Object) return null;
        if (!sp.TryGetProperty("Player", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        if (!p.TryGetProperty("Game Options", out var go) || go.ValueKind != JsonValueKind.Object) return null;
        if (!go.TryGetProperty("Race Time", out var t)) return null;
        return t.ValueKind switch
        {
            JsonValueKind.Number when t.TryGetDouble(out var d) => (int)Math.Round(d),
            JsonValueKind.String when double.TryParse(t.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => (int)Math.Round(d),
            _ => null,
        };
    }

    private static string? Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? Date(JsonElement e, string prop) =>
        Str(e, prop) is { } s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d : null;
}
