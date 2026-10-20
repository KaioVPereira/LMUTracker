using System.Globalization;
using System.Text.Json;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Revezamentos de piloto de uma linha de resultado. Em corrida com troca de piloto o RaceOS manda uma linha por
/// CARRO (com quem terminou) e os turnos em <c>swaps: [{ startLap, endLap, driver }]</c> — o único lugar onde os
/// outros pilotos do carro aparecem. Só devolve algo quando o carro teve 2+ pilotos diferentes.
/// </summary>
public static class CrewReader
{
    public sealed record Stint(int Seq, int StartLap, int EndLap, string Name);

    /// <summary>Da linha recém-baixada (campos extras do JSON do RaceOS).</summary>
    public static List<Stint>? FromRowExtra(Dictionary<string, JsonElement>? extra) =>
        extra is not null && extra.TryGetValue("swaps", out var swaps) ? Read(swaps) : null;

    /// <summary>Da linha já guardada (RaceResult.ExtraData, JSON comprimido).</summary>
    public static List<Stint>? FromStored(byte[]? extraData)
    {
        try
        {
            using var doc = JsonBlob.Decode(extraData);
            return doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("swaps", out var swaps) ? Read(swaps) : null;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static List<Stint>? Read(JsonElement swaps)
    {
        if (swaps.ValueKind != JsonValueKind.Array) return null;
        var list = new List<Stint>();
        foreach (var s in swaps.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            var name = s.TryGetProperty("driver", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()?.Trim() : null;
            var from = Int(s, "startLap");
            var to = Int(s, "endLap");
            if (string.IsNullOrEmpty(name) || from is null || to is null) continue;
            list.Add(new Stint(list.Count, from.Value, Math.Max(from.Value, to.Value), name));
        }
        return list.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 ? list : null;
    }

    /// <summary>O RaceOS manda números às vezes como texto ("31"), às vezes como número.</summary>
    private static int? Int(JsonElement o, string prop) =>
        !o.TryGetProperty(prop, out var v) ? null
        : v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i
        : v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var j) ? j
        : null;
}
