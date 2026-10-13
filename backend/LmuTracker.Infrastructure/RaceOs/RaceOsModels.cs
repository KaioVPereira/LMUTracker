using System.Text.Json.Serialization;

namespace LmuTracker.Infrastructure.RaceOs;

// DTOs mínimos do RaceOS — só os campos que o scanner usa. JSON em camelCase.

internal sealed class AuthResult
{
    [JsonPropertyName("accessToken")] public string? AccessToken { get; set; }
    [JsonPropertyName("expiresAt")] public DateTime? ExpiresAt { get; set; }
    [JsonPropertyName("banned")] public bool Banned { get; set; }
}

/// <summary>Instância de corrida vinda de <c>daily/list/{tier}</c> ou do array <c>events</c> dos especiais.</summary>
public sealed class RaceOsEvent
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;
    [JsonPropertyName("seriesId")] public string? SeriesId { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("raceStartsAt")] public DateTime? RaceStartsAt { get; set; }
    [JsonPropertyName("raceEndsAt")] public DateTime? RaceEndsAt { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
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
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("track")] public string? Track { get; set; }
    [JsonPropertyName("trackScene")] public string? TrackScene { get; set; }
    [JsonPropertyName("tier")] public string? Tier { get; set; }
    [JsonPropertyName("official")] public bool Official { get; set; }
    [JsonPropertyName("drivers")] public List<ResultDriver> Drivers { get; set; } = new();
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
}
