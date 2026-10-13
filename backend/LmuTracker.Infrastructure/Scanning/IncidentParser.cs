using System.Text.Json;
using System.Text.RegularExpressions;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Conta incidentes e penalidades por piloto a partir do <c>streams[0]</c> da corrida (formato do rFactor, igual ao
/// XML de resultados do jogo):
/// <list type="bullet">
/// <item><c>Nome(12) reported contact (123.80) with another vehicle Outro(3)</c> — contato com carro;</item>
/// <item><c>Nome(5) reported contact (2560.56) with Wall</c> — contato com muro/objeto;</item>
/// <item><c>Nome received Drive Thru penalty, 10s, 0laps for …</c> — penalidade aplicada.</item>
/// </list>
/// Cada contato entre dois carros é relatado pelos dois — conta para os dois, como o jogo conta.
/// </summary>
public static partial class IncidentParser
{
    [GeneratedRegex(@"^(?<name>.+?)\(\d+\) reported contact \((?<force>[\d.]+)\) with (?<other>.+)$")]
    private static partial Regex Contact();

    [GeneratedRegex(@"^(?<name>.+?) received .*penalty")]
    private static partial Regex Penalty();

    public sealed record Counts(int Incidents, int Penalties);

    /// <summary>
    /// Contagens por nome de piloto (o stream não traz SteamID). <paramref name="streams"/> é o array <c>streams</c>
    /// do bloco da corrida; nomes desconhecidos ficam de fora.
    /// </summary>
    public static Dictionary<string, Counts> Count(JsonElement? streams, IReadOnlyCollection<string> names)
    {
        var known = new HashSet<string>(names, StringComparer.Ordinal);
        var incidents = new Dictionary<string, int>(StringComparer.Ordinal);
        var penalties = new Dictionary<string, int>(StringComparer.Ordinal);

        if (streams is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var block in arr.EnumerateArray())
            {
                foreach (var e in Items(block, "incidents"))
                {
                    var m = Contact().Match(e);
                    if (m.Success && known.Contains(m.Groups["name"].Value.Trim()))
                        Add(incidents, m.Groups["name"].Value.Trim());
                }
                foreach (var e in Items(block, "penalties"))
                {
                    var m = Penalty().Match(e);
                    if (m.Success && known.Contains(m.Groups["name"].Value.Trim()))
                        Add(penalties, m.Groups["name"].Value.Trim());
                }
            }
        }

        return known.ToDictionary(n => n, n => new Counts(incidents.GetValueOrDefault(n), penalties.GetValueOrDefault(n)),
            StringComparer.Ordinal);
    }

    /// <summary>Mesmo cálculo a partir do resultado cru guardado (RaceSplit.ExtraData).</summary>
    public static Dictionary<string, Counts>? CountFromStored(byte[]? splitExtra, IReadOnlyCollection<string> names)
    {
        using var doc = JsonBlob.Decode(splitExtra);
        if (doc is null) return null;
        if (!doc.RootElement.TryGetProperty("raceBlock", out var block) || block.ValueKind != JsonValueKind.Object
            || !block.TryGetProperty("streams", out var streams))
            return Count(null, names);
        return Count(streams, names);
    }

    private static IEnumerable<string> Items(JsonElement block, string name)
    {
        if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in list.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                yield return d.GetString() ?? "";
    }

    private static void Add(Dictionary<string, int> map, string key) => map[key] = map.GetValueOrDefault(key) + 1;
}
