using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using LmuTracker.Application.Lmu;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Guarda o volta a volta de um piloto como JSON de chaves curtas, comprimido com gzip (~1 KB por piloto).
/// Uma tabela com uma linha por volta passaria de 400 mil linhas por dia — e o SQL Express tem teto de 10 GB.
/// </summary>
public static class LapCodec
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class Stored
    {
        [JsonPropertyName("n")] public int N { get; set; }
        [JsonPropertyName("p")] public int? P { get; set; }
        [JsonPropertyName("t")] public double? T { get; set; }
        [JsonPropertyName("s")] public double?[]? S { get; set; }   // s1, s2, s3
        [JsonPropertyName("e")] public double? E { get; set; }
        [JsonPropertyName("f")] public double? F { get; set; }
        [JsonPropertyName("c")] public string?[]? C { get; set; }   // dianteiro, traseiro
        [JsonPropertyName("w")] public double?[]? W { get; set; }   // FL, FR, RL, RR
        [JsonPropertyName("x")] public bool? Pit { get; set; }
    }

    public static byte[]? Encode(IReadOnlyList<LapDto> laps)
    {
        if (laps.Count == 0) return null;
        var stored = laps.Select(l => new Stored
        {
            N = l.Lap,
            P = l.Position,
            T = Round(l.LapTime, 4),
            S = new[] { Round(l.S1, 4), Round(l.S2, 4), Round(l.S3, 4) },
            E = Round(l.Elapsed, 3),
            F = Round(l.Fuel, 3),
            C = new[] { l.FrontCompound, l.RearCompound },
            W = new[] { Round(l.WearFL, 3), Round(l.WearFR, 3), Round(l.WearRL, 3), Round(l.WearRR, 3) },
            Pit = l.Pit ? true : null,
        }).ToList();

        var json = JsonSerializer.SerializeToUtf8Bytes(stored, Json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
            gzip.Write(json);
        return output.ToArray();
    }

    public static IReadOnlyList<LapDto> Decode(byte[]? data)
    {
        if (data is null || data.Length == 0) return Array.Empty<LapDto>();
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        var stored = JsonSerializer.Deserialize<List<Stored>>(input, Json) ?? new();
        return stored.Select(s => new LapDto(
            s.N, s.P, s.T, At(s.S, 0), At(s.S, 1), At(s.S, 2), s.E, s.F, At(s.C, 0), At(s.C, 1),
            At(s.W, 0), At(s.W, 1), At(s.W, 2), At(s.W, 3), s.Pit == true)).ToList();
    }

    private static double? Round(double? v, int digits) => v is null ? null : Math.Round(v.Value, digits);
    private static T? At<T>(T?[]? arr, int i) => arr is not null && i < arr.Length ? arr[i] : default;
}
