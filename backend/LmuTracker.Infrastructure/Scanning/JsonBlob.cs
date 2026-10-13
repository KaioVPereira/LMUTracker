using System.IO.Compression;
using System.Text.Json;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// JSON cru comprimido com gzip. É onde vai o que o RaceOS manda e o scanner ainda não lê campo a campo
/// (incidentes, penalidades, clima, configuração do evento): guardando agora, dá para extrair depois sem
/// depender de o resultado ainda existir no RaceOS.
/// </summary>
public static class JsonBlob
{
    /// <summary>Serializa e comprime. Null quando não há nada ou quando passa do teto (proteção de espaço).</summary>
    public static byte[]? Encode(object? value, int maxBytes)
    {
        if (value is null) return null;
        var json = JsonSerializer.SerializeToUtf8Bytes(value);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
            gzip.Write(json);
        var bytes = output.ToArray(); // o GZipStream fecha o MemoryStream; ToArray ainda funciona, Length não
        return bytes.Length <= maxBytes ? bytes : null;
    }

    public static JsonDocument? Decode(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        return JsonDocument.Parse(input);
    }

    /// <summary>
    /// Campos extras sem os que não guardamos: chat (texto livre de terceiros, o site não mostra) e os que
    /// o chamador já grava em outro lugar. Null quando não sobra nada.
    /// </summary>
    public static Dictionary<string, JsonElement>? Keep(Dictionary<string, JsonElement>? extra, params string[] skip)
    {
        if (extra is null) return null;
        var kept = extra
            .Where(kv => !kv.Key.Contains("chat", StringComparison.OrdinalIgnoreCase)
                         && !skip.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return kept.Count > 0 ? kept : null;
    }
}
