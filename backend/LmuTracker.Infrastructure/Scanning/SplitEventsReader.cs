using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LmuTracker.Application.Lmu;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Penalidades e decisões dos comissários de um split, do resultado cru guardado (RaceSplit.ExtraData →
/// raceBlock.streams[0]). Contatos ficam de fora (são centenas por corrida e já viram contagem por piloto).
/// </summary>
public static partial class SplitEventsReader
{
    [GeneratedRegex(@"^(?<name>.+?) received (?<what>.+?) penalty(?:, (?<detail>[^.]*))?")]
    private static partial Regex Received();

    [GeneratedRegex(@"^(?<name>.+?) (?:served|finished before serving) ")]
    private static partial Regex Served();

    public static IReadOnlyList<SplitEventDto>? Read(byte[]? splitExtra)
    {
        using var doc = JsonBlob.Decode(splitExtra);
        if (doc is null) return null;
        var list = new List<SplitEventDto>();
        if (!doc.RootElement.TryGetProperty("raceBlock", out var block) || block.ValueKind != JsonValueKind.Object
            || !block.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var s in streams.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            if (s.TryGetProperty("penalties", out var pens) && pens.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pens.EnumerateArray())
                {
                    var text = Str(p, "description") ?? "";
                    var at = Dbl(p, "et");
                    var m = Received().Match(text);
                    if (m.Success)
                        list.Add(new SplitEventDto(at, "penalty", m.Groups["name"].Value.Trim(), null,
                            $"{m.Groups["what"].Value.Trim()}{(m.Groups["detail"].Success ? $" · {m.Groups["detail"].Value.Trim()}" : "")}", null));
                    else if (Served().Match(text) is { Success: true } sv)
                        list.Add(new SplitEventDto(at, "served", sv.Groups["name"].Value.Trim(), null,
                            text.Contains("finished before serving") ? "terminou sem cumprir: +10 s no tempo" : "cumpriu a penalidade", null));
                }
            }
            if (s.TryGetProperty("liveStewards", out var st) && st.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in st.EnumerateArray())
                {
                    var lap = int.TryParse(Str(d, "lap"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : (int?)null;
                    var detail = string.Join(" · ", new[] { Str(d, "type"), Str(d, "penalty") }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    list.Add(new SplitEventDto(Dbl(d, "et"), "steward", Str(d, "name"), Str(d, "opponent"), detail, lap));
                }
            }
        }
        return list.OrderBy(e => e.At ?? double.MaxValue).ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static double? Dbl(JsonElement e, string name) =>
        double.TryParse(Str(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
