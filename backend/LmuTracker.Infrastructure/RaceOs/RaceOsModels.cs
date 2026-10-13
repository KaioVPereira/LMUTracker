using System.Text.Json;
using System.Text.Json.Serialization;

namespace LmuTracker.Infrastructure.RaceOs;

// DTOs mínimos do RaceOS — só os campos que o scanner usa. JSON em camelCase.

internal sealed class AuthResult
{
    [JsonPropertyName("accessToken")] public string? AccessToken { get; set; }
    [JsonPropertyName("expiresAt")] public DateTime? ExpiresAt { get; set; }
    [JsonPropertyName("banned")] public bool Banned { get; set; }
}

/// <summary>
/// Instância de corrida vinda de <c>daily/list/{tier}</c>. Os dados da corrida em si (nome, largada oficial,
/// pista) estão em <see cref="Configuration"/>. <see cref="RaceStartsAt"/>/<see cref="RaceEndsAt"/> são da
/// SESSÃO de corrida (depois de treino e quali), não do evento — o jogo mostra <c>configuration.starts</c>.
/// </summary>
public sealed class RaceOsEvent
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;
    [JsonPropertyName("seriesId")] public string? SeriesId { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("raceStartsAt")] public DateTime? RaceStartsAt { get; set; }
    [JsonPropertyName("raceEndsAt")] public DateTime? RaceEndsAt { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("configuration")] public EventConfiguration? Configuration { get; set; }
}

public sealed class EventConfiguration
{
    [JsonPropertyName("title")] public string? Title { get; set; }

    /// <summary>Largada oficial do evento (o horário que o jogo mostra no cronograma).</summary>
    [JsonPropertyName("starts")] public DateTime? Starts { get; set; }

    [JsonPropertyName("content")] public EventContent? Content { get; set; }
    [JsonPropertyName("tier")] public EventTier? Tier { get; set; }
    [JsonPropertyName("sessions")] public List<EventSession>? Sessions { get; set; }
}

public sealed class EventSession
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    /// <summary>Ex.: "3m", "1h12m30s".</summary>
    [JsonPropertyName("duration")] public string? Duration { get; set; }
}

public sealed class EventTier
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

/// <summary>Série de <c>special-events/{weekly|special}</c>: um título com várias instâncias em <see cref="Events"/>.</summary>
public sealed class RaceOsSeries
{
    [JsonPropertyName("seriesId")] public string? SeriesId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    /// <summary>Nas séries vem como objeto (<c>{ name, ... }</c>), mas aceitamos texto também.</summary>
    [JsonPropertyName("tier")] public JsonElement? TierRaw { get; set; }
    [JsonPropertyName("type")] public JsonElement? Type { get; set; }
    [JsonPropertyName("events")] public List<RaceOsEvent> Events { get; set; } = new();

    [JsonIgnore]
    public string? Tier => TierRaw?.ValueKind switch
    {
        JsonValueKind.String => TierRaw.Value.GetString(),
        JsonValueKind.Object when TierRaw.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String => n.GetString(),
        _ => null,
    };
}

public sealed class EventContent
{
    [JsonPropertyName("track")] public EventTrack? Track { get; set; }
}

public sealed class EventTrack
{
    /// <summary>Nome interno da pista (ex.: MonzaWEC).</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>Nome "amigável" — às vezes limpo ("Monza"), às vezes com sufixo interno ("FujiWEC_2023").</summary>
    [JsonPropertyName("friendly")] public string? Friendly { get; set; }
}

public sealed class OverviewResult
{
    [JsonPropertyName("totalSplits")] public int TotalSplits { get; set; }
    [JsonPropertyName("seriesId")] public string? SeriesId { get; set; }
    [JsonPropertyName("raceStartsAt")] public DateTime? RaceStartsAt { get; set; }
    [JsonPropertyName("raceEndsAt")] public DateTime? RaceEndsAt { get; set; }
}

// ----- Resultado de um split (result?eventId=&splitNo=) -----

public sealed class SplitResult
{
    /// <summary>Largada oficial do evento (mesmo horário de configuration.starts).</summary>
    [JsonPropertyName("date")] public DateTime? Date { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("track")] public string? Track { get; set; }
    [JsonPropertyName("trackScene")] public string? TrackScene { get; set; }
    [JsonPropertyName("tier")] public string? Tier { get; set; }
    [JsonPropertyName("official")] public bool Official { get; set; }
    [JsonPropertyName("drivers")] public List<ResultDriver> Drivers { get; set; } = new();
    [JsonPropertyName("qualifying")] public SessionWrapper? Qualifying { get; set; }
    [JsonPropertyName("race")] public SessionWrapper? Race { get; set; }
}

/// <summary>Entrada do array <c>drivers</c>: identidade + variação de rating na corrida.</summary>
public sealed class ResultDriver
{
    [JsonPropertyName("steamId")] public string? SteamId { get; set; }
    [JsonPropertyName("nkId")] public string? NkId { get; set; }
    [JsonPropertyName("raceControlId")] public string? RaceControlId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("nationality")] public string? Nationality { get; set; }
    [JsonPropertyName("badge")] public string? Badge { get; set; }
    [JsonPropertyName("class")] public string? Class { get; set; }
    [JsonPropertyName("driverRank")] public RankInfo? DriverRank { get; set; }
    [JsonPropertyName("safetyRank")] public RankInfo? SafetyRank { get; set; }
    [JsonPropertyName("driverRankAdjustment")] public int? DriverRankAdjustment { get; set; }
    [JsonPropertyName("safetyRankAdjustment")] public int? SafetyRankAdjustment { get; set; }
    [JsonPropertyName("jokerPlayed")] public bool JokerPlayed { get; set; }
}

public sealed class RankInfo
{
    [JsonPropertyName("rank")] public string? Rank { get; set; }
    [JsonPropertyName("tier")] public int Tier { get; set; }
    /// <summary>Progresso até o próximo tier (na visão pública vem arredondado, de 10 em 10).</summary>
    [JsonPropertyName("progress")] public double? Progress { get; set; }
}

public sealed class SessionWrapper
{
    [JsonPropertyName("races")] public List<RaceBlock> Races { get; set; } = new();
}

public sealed class RaceBlock
{
    [JsonPropertyName("mostLapsCompleted")] public int MostLapsCompleted { get; set; }
    [JsonPropertyName("results")] public List<ResultRow> Results { get; set; } = new();
}

/// <summary>Linha de classificação de um piloto no split.</summary>
public sealed class ResultRow
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("steamId")] public string? SteamId { get; set; }
    [JsonPropertyName("raceControlId")] public string? RaceControlId { get; set; }
    [JsonPropertyName("vehName")] public string? VehName { get; set; }
    [JsonPropertyName("carType")] public string? CarType { get; set; }
    [JsonPropertyName("carClass")] public string? CarClass { get; set; }
    [JsonPropertyName("carNumber")] public string? CarNumber { get; set; }
    [JsonPropertyName("teamName")] public string? TeamName { get; set; }
    [JsonPropertyName("gridPos")] public string? GridPos { get; set; }
    [JsonPropertyName("position")] public string? Position { get; set; }
    [JsonPropertyName("classGridPos")] public string? ClassGridPos { get; set; }
    [JsonPropertyName("classPosition")] public string? ClassPosition { get; set; }
    [JsonPropertyName("bestLapTime")] public string? BestLapTime { get; set; }
    [JsonPropertyName("finishTime")] public string? FinishTime { get; set; }
    [JsonPropertyName("Laps")] public int Laps { get; set; }
    [JsonPropertyName("finishStatus")] public string? FinishStatus { get; set; }
    [JsonPropertyName("points")] public int Points { get; set; }

    /// <summary>Volta a volta. Mesmo objeto tem "Laps" (número) — por isso o JSON é lido com nomes exatos.</summary>
    [JsonPropertyName("laps")] public List<LapRow>? LapList { get; set; }
}

/// <summary>Aceita texto, número ou null e entrega texto — o RaceOS manda números como texto, mas não é garantido.</summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetDecimal(out var d)
                ? d.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "1",
            JsonTokenType.False => "",
            JsonTokenType.Null => null,
            _ => SkipAndNull(ref reader),
        };

    private static string? SkipAndNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>Uma volta do piloto. O RaceOS manda tudo como texto (números inclusive).</summary>
public sealed class LapRow
{
    [JsonPropertyName("num")] public string? Num { get; set; }
    [JsonPropertyName("p")] public string? Position { get; set; }
    [JsonPropertyName("et")] public string? Elapsed { get; set; }
    [JsonPropertyName("lapTime")] public string? LapTime { get; set; }
    [JsonPropertyName("s1")] public string? S1 { get; set; }
    [JsonPropertyName("s2")] public string? S2 { get; set; }
    [JsonPropertyName("s3")] public string? S3 { get; set; }
    [JsonPropertyName("fuel")] public string? Fuel { get; set; }
    [JsonPropertyName("fcompound")] public string? FrontCompound { get; set; }
    [JsonPropertyName("rcompound")] public string? RearCompound { get; set; }
    [JsonPropertyName("twfl")] public string? WearFL { get; set; }
    [JsonPropertyName("twfr")] public string? WearFR { get; set; }
    [JsonPropertyName("twrl")] public string? WearRL { get; set; }
    [JsonPropertyName("twrr")] public string? WearRR { get; set; }
    [JsonPropertyName("pit")] public string? Pit { get; set; }
}
