using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    /// Campos extras sem os que não guardamos: chat (texto livre de terceiros, o site não mostra) — em qualquer
    /// nível, porque o RaceOS manda o chat dentro de <c>streams[0].chats</c> — e os que o chamador já grava em
    /// outro lugar. Null quando não sobra nada.
    /// </summary>
    public static Dictionary<string, JsonNode?>? Keep(Dictionary<string, JsonElement>? extra, params string[] skip)
    {
        if (extra is null) return null;
        var kept = extra
            .Where(kv => !IsChat(kv.Key) && !skip.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => WithoutChat(JsonNode.Parse(kv.Value.GetRawText())));
        return kept.Count > 0 ? kept : null;
    }

    private static bool IsChat(string key) => key.Contains("chat", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tira, em toda a árvore, as propriedades cujo nome contém "chat".</summary>
    private static JsonNode? WithoutChat(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsChat(key)) obj.Remove(key);
                    else WithoutChat(obj[key]);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) WithoutChat(item);
                break;
        }
        return node;
    }
}
