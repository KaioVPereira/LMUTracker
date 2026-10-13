using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LmuTracker.Application.Lmu;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Lê a configuração do evento guardada pelo scanner (ScanEvent.ConfigData): sessões com duração, horário no jogo
/// e clima, e as regras. Formato do RaceOS (o mesmo que o jogo usa): durações "3m" / "1h12m30s", horário "11h40m",
/// clima em nós com sky (0 limpo … 10 tempestade), temperature (°C) e rainChange (% de chuva).
/// </summary>
public static partial class EventConfigReader
{
    [GeneratedRegex(@"(\d+)([hms])")] private static partial Regex DurationPart();

    public static EventInfoDto? Read(byte[]? configData)
    {
        using var doc = JsonBlob.Decode(configData);
        if (doc is null || !doc.RootElement.TryGetProperty("configuration", out var cfg) || cfg.ValueKind != JsonValueKind.Object)
            return null;

        var sessions = new List<SessionInfoDto>();
        if (cfg.TryGetProperty("sessions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in arr.EnumerateArray())
            {
                var kind = Kind(Str(s, "type"));
                if (kind is null) continue;
                int? sky = null, temp = null, rain = null;
                if (s.TryGetProperty("weather", out var w) && w.ValueKind == JsonValueKind.Array)
                {
                    var nodes = w.EnumerateArray().ToList();
                    // Céu e temperatura do começo da sessão; chuva = a maior chance prevista na sessão.
                    if (nodes.Count > 0)
                    {
                        sky = Int(nodes[0], "sky");
                        temp = Int(nodes[0], "temperature");
                    }
                    var chances = nodes.Select(n => Int(n, "rainChange") ?? Int(n, "rainChance")).Where(x => x is not null).ToList();
                    rain = chances.Count > 0 ? chances.Max() : null;
                }
                sessions.Add(new SessionInfoDto(kind, Minutes(Str(s, "duration")), Minutes(Str(s, "waitTime")),
                    Minutes(Str(s, "timeOfDay")), sky, temp, rain));
            }
        }

        bool? fixedSetup = null;
        int? maxPlayers = null;
        if (cfg.TryGetProperty("settings", out var st) && st.ValueKind == JsonValueKind.Object)
        {
            if (st.TryGetProperty("fixedSetup", out var fs) && fs.ValueKind is JsonValueKind.True or JsonValueKind.False)
                fixedSetup = fs.GetBoolean();
            maxPlayers = Int(st, "maxPlayers");
        }

        return sessions.Count == 0 && fixedSetup is null ? null : new EventInfoDto(sessions, fixedSetup, maxPlayers);
    }

    private static string? Kind(string? type) => (type ?? "").ToLowerInvariant() switch
    {
        var t when t.StartsWith("practice") => "practice",
        var t when t.StartsWith("qual") => "qualifying",
        var t when t.StartsWith("warm") => "warmup",
        var t when t.StartsWith("race") => "race",
        _ => null,
    };

    /// <summary>"1h12m30s" -> 72.5; "11h40m" -> 700 (minutos). Vazio -> 0.</summary>
    private static double Minutes(string? s)
    {
        var total = 0.0;
        foreach (Match m in DurationPart().Matches(s ?? ""))
        {
            var v = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            total += m.Groups[2].Value switch { "h" => v * 60, "m" => v, _ => v / 60.0 };
        }
        return total;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static int? Int(JsonElement e, string name)
    {
        var s = Str(e, name);
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : null;
    }
}
