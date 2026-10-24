using System.Text.Json;
using LmuTracker.Application.Lmu;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Retrato do evento em andamento tirado do <c>event/overview</c> do RaceOS — o que dá para saber durante a corrida
/// (a cronometragem só existe nos servidores da corrida; o resultado só sai no fim). Guarda SÓ números: o overview
/// também traz nomes por split e host/senha dos servidores, que não saem daqui. Leitura tolerante: campo que não vier
/// no formato esperado fica vazio, sem derrubar o resto. Formato tirado do código do jogo (classe Event do UI).
/// </summary>
public static class LiveSnapshot
{
    /// <summary>Teto do JSON guardado em ScanEvent.LiveData (bem acima do normal: ~10 classes e ~40 splits).</summary>
    public const int MaxLength = 2000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Data(
        string? Status, int? TotalSplits, int? Registered, Dictionary<string, int>? ByClass,
        List<int>? SplitSizes, int? Servers, Dictionary<string, int>? ServerStatus, bool? Full, DateTime? LastUpdatesAt);

    /// <summary>Overview cru → JSON compacto para guardar. Null se não sobrar nada útil ou passar do teto.</summary>
    public static string? FromOverview(JsonElement o)
    {
        if (o.ValueKind != JsonValueKind.Object) return null;

        int? registered = null;
        Dictionary<string, int>? byClass = null;
        if (o.TryGetProperty("registered", out var reg) && reg.ValueKind == JsonValueKind.Object)
        {
            registered = Int(reg, "total");
            if (reg.TryGetProperty("class", out var cls) && cls.ValueKind == JsonValueKind.Object)
            {
                byClass = new();
                foreach (var p in cls.EnumerateObject())
                    if (Count(p.Value) is { } n) byClass[p.Name] = n;
                if (byClass.Count == 0) byClass = null;
            }
        }

        // Tamanho de cada split = quantos inscritos foram para ele (os nomes ficam de fora).
        List<int>? splitSizes = null;
        if (o.TryGetProperty("splits", out var splits) && splits.ValueKind == JsonValueKind.Array)
            splitSizes = splits.EnumerateArray()
                .Select(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("drivers", out var d) && d.ValueKind == JsonValueKind.Array
                    ? d.GetArrayLength() : 0)
                .ToList();

        // Servidores: só a contagem por status (host, porta e senha nunca são guardados).
        int? servers = null;
        Dictionary<string, int>? serverStatus = null;
        if (o.TryGetProperty("servers", out var srv) && srv.ValueKind == JsonValueKind.Array)
        {
            servers = srv.GetArrayLength();
            serverStatus = srv.EnumerateArray()
                .Select(s => s.ValueKind == JsonValueKind.Object ? Str(s, "status") : null)
                .Where(s => !string.IsNullOrEmpty(s))
                .GroupBy(s => s!)
                .ToDictionary(g => g.Key, g => g.Count());
            if (serverStatus.Count == 0) serverStatus = null;
        }

        var data = new Data(
            Str(o, "status"), Int(o, "totalSplits"), registered, byClass,
            splitSizes is { Count: > 0 } ? splitSizes : null, servers, serverStatus,
            o.TryGetProperty("eventFull", out var full) && full.ValueKind is JsonValueKind.True or JsonValueKind.False ? full.GetBoolean() : null,
            o.TryGetProperty("lastUpdatesAt", out var lu) && lu.ValueKind == JsonValueKind.String && lu.TryGetDateTime(out var dt)
                ? dt.ToUniversalTime() : null);

        if (data.Status is null && data.TotalSplits is null && data.Registered is null && data.SplitSizes is null)
            return null;
        var json = JsonSerializer.Serialize(data, Json);
        return json.Length <= MaxLength ? json : null;
    }

    /// <summary>Quantos splits o retrato guardado diz (0 antes de os splits se formarem).</summary>
    public static int SplitsOf(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return 0;
        try { return JsonSerializer.Deserialize<Data>(stored, Json)?.TotalSplits ?? 0; }
        catch (JsonException) { return 0; }
    }

    /// <summary>JSON guardado → DTO do site. Null se não houver retrato.</summary>
    public static LiveStatusDto? Read(string? stored, DateTime? checkedAt)
    {
        if (string.IsNullOrEmpty(stored) || checkedAt is null) return null;
        Data? d;
        try { d = JsonSerializer.Deserialize<Data>(stored, Json); }
        catch (JsonException) { return null; }
        if (d is null) return null;

        var running = d.ServerStatus?.Where(kv => kv.Key.Equals("Running", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
        return new LiveStatusDto(
            d.Status,
            d.TotalSplits is > 0 ? d.TotalSplits : d.SplitSizes?.Count,
            d.Registered,
            (d.ByClass ?? new()).OrderBy(kv => ClassRank(kv.Key)).ThenByDescending(kv => kv.Value)
                .Select(kv => new ClassCountDto(kv.Key, kv.Value)).ToList(),
            d.SplitSizes ?? new(),
            d.Servers,
            d.ServerStatus is null ? null : running,
            d.Full,
            d.LastUpdatesAt,
            DateTime.SpecifyKind(checkedAt.Value, DateTimeKind.Utc));
    }

    /// <summary>Ordem das classes no site (das mais rápidas para as mais lentas); LMP2_ELMS junto de LMP2, LMGT3 de GT3.</summary>
    private static int ClassRank(string c)
    {
        var u = c.ToUpperInvariant();
        return u.StartsWith("HYPER") ? 0 : u.StartsWith("LMP2") ? 1 : u.StartsWith("LMP3") ? 2 : u.Contains("GT3") ? 3 : 4;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? Count(v) : null;

    /// <summary>Número, número em texto, ou objeto com total/count (formato da contagem por classe não confirmado ao vivo).</summary>
    private static int? Count(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number when v.TryGetDouble(out var d) => (int)Math.Round(d),
        JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
        JsonValueKind.Object => Int(v, "total") ?? Int(v, "count"),
        JsonValueKind.Array => v.GetArrayLength(),
        _ => null,
    };
}
