using System.Text.Json;
using System.Text.Json.Nodes;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Detalhe do evento em andamento (ScanEvent.LiveDetail): o event/overview inteiro menos o que é sensível, e a lista de
/// inscritos de event/entries quando os splits não trazem pilotos. A leitura dos pilotos é tolerante — o formato de
/// cada piloto dentro do split não aparece no código do jogo (que só usa <c>drivers[].driver.name</c>), então os campos
/// são procurados no item e no <c>driver</c> dele, com os nomes usados no resto do RaceOS.
/// </summary>
public static class LiveDetail
{
    /// <summary>Teto do JSON comprimido (um evento de 24 h tem ~1.600 inscritos: ~100 KB comprimidos).</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    // Nunca guardados: acesso aos servidores, credenciais, chat, e-mail, imagens de perfil (URLs de terceiros) e os
    // números exatos de rating de terceiros (elo do DR, rating do SR, razões de contato) — o site só mostra rank/tier,
    // igual ao resto do Pitwall. A configuração do evento já é guardada à parte (ConfigData).
    private static readonly HashSet<string> Secret = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "port", "password", "auth", "authToken", "token", "accessToken", "email", "ip", "ipAddress",
        "avatar", "avatarUrl", "taskArn", "configuration", "elo", "rating", "contactImpactsPerLapRatio",
        "contactImpactsPerLapRatios", "carImages",
    };

    private static bool Drop(string key) =>
        Secret.Contains(key) || key.Contains("chat", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("secret", StringComparison.OrdinalIgnoreCase);

    /// <summary>Cópia do JSON sem as chaves sensíveis, em qualquer nível.</summary>
    public static JsonNode? Clean(JsonElement e)
    {
        var node = JsonNode.Parse(e.GetRawText());
        Strip(node);
        return node;
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (Drop(key)) obj.Remove(key);
                    else Strip(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) Strip(item);
                break;
        }
    }

    /// <summary>Grava overview + inscritos, limpando de novo (a lista reaproveitada pode ter sido guardada com regras antigas).</summary>
    /// <summary>
    /// Inscritos de event/entries, limpos. Antes de descartar o elo, anota em cada um a posição dele na ordem do DR
    /// (1 = maior elo): é o que a previsão de split usa (LiveSplits), sem guardar o número de ninguém.
    /// </summary>
    public static JsonArray CleanEntries(IReadOnlyList<JsonElement> items)
    {
        var elo = items.Select(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("dr", out var dr)
                && dr.ValueKind == JsonValueKind.Object && dr.TryGetProperty("elo", out var v) && v.ValueKind == JsonValueKind.Number
                && v.TryGetDouble(out var x) ? x : (double?)null).ToList();
        var order = Enumerable.Range(0, items.Count).Where(i => elo[i] is not null)
            .OrderByDescending(i => elo[i]).ThenBy(i => i).ToList();
        var pos = new int?[items.Count];
        for (var k = 0; k < order.Count; k++) pos[order[k]] = k + 1;

        var all = new JsonArray();
        for (var i = 0; i < items.Count; i++)
        {
            var node = Clean(items[i]);
            if (node is JsonObject obj && pos[i] is { } p) obj["eloPos"] = p;
            all.Add(node);
        }
        return all;
    }

    public static byte[]? Encode(JsonNode? overview, JsonArray? entries, DateTime? entriesAt)
    {
        Strip(overview);
        Strip(entries);
        return JsonBlob.Encode(new JsonObject { ["overview"] = overview, ["entries"] = entries, ["entriesAt"] = entriesAt }, MaxBytes);
    }

    /// <summary>A lista de inscritos guardada antes (para manter entre as releituras de 30 em 30 min).</summary>
    public static JsonArray? StoredEntries(byte[]? detail)
    {
        using var doc = JsonBlob.Decode(detail);
        return doc is not null && doc.RootElement.TryGetProperty("entries", out var e) && e.ValueKind == JsonValueKind.Array
            ? JsonNode.Parse(e.GetRawText()) as JsonArray : null;
    }

    /// <summary>Os splits do overview têm pilotos? (Se não, a lista vem de event/entries.)</summary>
    public static bool SplitsHaveDrivers(JsonElement overview) =>
        overview.TryGetProperty("splits", out var s) && s.ValueKind == JsonValueKind.Array
        && s.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("drivers", out var d)
                                       && d.ValueKind == JsonValueKind.Array && d.GetArrayLength() > 0);

    // ------------------------------------------------------------------ leitura

    /// <summary>Um inscrito. Dr/Sr = rank e tier atuais segundo o RaceOS (vêm em event/entries).</summary>
    public sealed record Entry(
        int? SplitNo, string Name, string? Nationality, string? CarClass, string? Car, string? CarNumber, string? Team,
        string? SteamId, string? Badge, IReadOnlyList<string> Members, string? Livery,
        string? DrRank, int? DrTier, string? SrRank, int? SrTier, int? EloPos = null);

    /// <summary>
    /// Pilotos por split (do overview) ou, sem isso, a lista de inscritos (sem split). Source: splits|entries|null.
    /// TeamEvent: cada inscrição é uma equipe (nome da equipe + integrantes), como nas 24h de Le Mans.
    /// </summary>
    public static (string? Source, bool TeamEvent, List<Entry> Entries) Read(byte[]? detail)
    {
        using var doc = JsonBlob.Decode(detail);
        if (doc is null) return (null, false, new());
        var root = doc.RootElement;
        var team = root.TryGetProperty("overview", out var ov) && ov.ValueKind == JsonValueKind.Object
                   && ov.TryGetProperty("teamEvent", out var te) && te.ValueKind == JsonValueKind.True;

        if (root.TryGetProperty("overview", out var o) && o.ValueKind == JsonValueKind.Object
            && o.TryGetProperty("splits", out var splits) && splits.ValueKind == JsonValueKind.Array)
        {
            var list = new List<Entry>();
            var i = 0;
            foreach (var s in splits.EnumerateArray())
            {
                i++;
                if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("drivers", out var drivers)
                    || drivers.ValueKind != JsonValueKind.Array) continue;
                var no = Int(s, "splitNo") ?? Int(s, "split") ?? i;
                foreach (var d in drivers.EnumerateArray())
                    if (FromItem(d, no) is { } en) list.Add(en);
            }
            if (list.Count > 0) return ("splits", team, list);
        }

        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            var list = entries.EnumerateArray().Select(d => FromItem(d, null)).OfType<Entry>().ToList();
            if (list.Count > 0) return ("entries", team, list);
        }
        return (null, team, new());
    }

    /// <summary>Um piloto (ou carro de equipe). Procura cada campo no item e no objeto <c>driver</c> dele.</summary>
    private static Entry? FromItem(JsonElement item, int? splitNo)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var inner = item.TryGetProperty("driver", out var dv) && dv.ValueKind == JsonValueKind.Object ? dv : (JsonElement?)null;
        string? Pick(params string[] names)
        {
            foreach (var n in names)
            {
                if (Str(item, n) is { Length: > 0 } a) return a;
                if (inner is { } x && Str(x, n) is { Length: > 0 } b) return b;
            }
            return null;
        }

        var name = Pick("name", "username", "displayName", "driverName");
        var team = TeamName(item) ?? (inner is { } t ? TeamName(t) : null);
        name ??= team;
        if (name is null) return null;

        // Integrantes de equipe: só quem pilota (papel "Driver"); engenheiro, manager e dono ficam de fora.
        var members = new List<string>();
        foreach (var key in new[] { "teamMembers", "drivers", "lineup" })
            if (item.TryGetProperty(key, out var m) && m.ValueKind == JsonValueKind.Array)
                members.AddRange(m.EnumerateArray().Where(IsDriver).Select(x => x.ValueKind == JsonValueKind.Object
                    ? Str(x, "name") ?? (x.TryGetProperty("driver", out var xd) && xd.ValueKind == JsonValueKind.Object ? Str(xd, "name") : null)
                    : null).OfType<string>());

        var livery = Livery(item) ?? (inner is { } lv ? Livery(lv) : null);
        var (drRank, drTier) = RankOf(item, "dr");
        var (srRank, srTier) = RankOf(item, "sr");
        return new Entry(
            splitNo, name,
            Pick("nationality", "country", "countryCode"),
            Pick("class", "carClass", "vehicleClass"),
            CarName(item) ?? (inner is { } c ? CarName(c) : null),
            Pick("carNumber", "number", "raceNumber"),
            team is not null && team != name ? team : null,
            Pick("steamId", "steamID", "platformId"),
            Pick("badge"),
            members.Distinct().Where(x => x != name).ToList(),
            livery, drRank, drTier, srRank, srTier, Int(item, "eloPos"));
    }

    /// <summary>Integrante que pilota: sem lista de papéis (formato antigo) conta como piloto.</summary>
    private static bool IsDriver(JsonElement m) =>
        m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array
        || roles.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String && r.GetString() == "Driver");

    /// <summary>{ "dr": { "rank": "Silver", "tier": 2 } } → ("Silver", 2).</summary>
    private static (string?, int?) RankOf(JsonElement e, string key) =>
        e.TryGetProperty(key, out var r) && r.ValueKind == JsonValueKind.Object ? (Str(r, "rank"), Int(r, "tier")) : (null, null);

    /// <summary>
    /// Pintura escolhida: "Team WRT 2026 #69:LP" (nome do sub-item do carro) → "Team WRT 2026 #69". O número da pintura
    /// não é o do inscrito (as personalizadas vêm todas como "Custom Team #397"), então não vira número do carro.
    /// </summary>
    private static string? Livery(JsonElement e)
    {
        if (!e.TryGetProperty("car", out var car) || car.ValueKind != JsonValueKind.Object
            || !car.TryGetProperty("subItems", out var sub) || sub.ValueKind != JsonValueKind.Array) return null;
        foreach (var s in sub.EnumerateArray())
            if (s.ValueKind == JsonValueKind.Object && Str(s, "name") is { Length: > 0 } n)
                return (n.IndexOf(':') is var i and > 0 ? n[..i] : n).Trim();
        return null;
    }


    private static string? TeamName(JsonElement e)
    {
        if (Str(e, "teamName") is { Length: > 0 } s) return s;
        if (e.TryGetProperty("team", out var t))
        {
            if (t.ValueKind == JsonValueKind.String) return t.GetString();
            if (t.ValueKind == JsonValueKind.Object)
                return Str(t, "name") ?? (t.TryGetProperty("lineup", out var l) && l.ValueKind == JsonValueKind.Object ? Str(l, "name") : null);
        }
        return null;
    }

    /// <summary>Carro: texto, ou objeto com nome/título, ou o .veh do primeiro sub-item (formato de event/entries).</summary>
    private static string? CarName(JsonElement e)
    {
        if (Str(e, "carName") is { Length: > 0 } n) return n;
        if (!e.TryGetProperty("car", out var car)) return Str(e, "vehicle") ?? Str(e, "vehName");
        if (car.ValueKind == JsonValueKind.String) return car.GetString();
        if (car.ValueKind != JsonValueKind.Object) return null;
        // "BMW_M4_LMGT3" → "BMW M4 LMGT3", o mesmo nome dos resultados (CarType) e das páginas de carro.
        var name = Str(car, "name") ?? Str(car, "title") ?? Str(car, "displayName") ?? Str(car, "model");
        if (name is not null) return name.Replace('_', ' ');
        if (car.TryGetProperty("subItems", out var sub) && sub.ValueKind == JsonValueKind.Array)
            foreach (var s in sub.EnumerateArray())
                if (s.ValueKind == JsonValueKind.Object && (Str(s, "name") ?? Str(s, "veh")) is { Length: > 0 } v)
                    return v.EndsWith(".veh", StringComparison.OrdinalIgnoreCase) ? v[..^4] : v;
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
            _ => null,
        } : null;
}
