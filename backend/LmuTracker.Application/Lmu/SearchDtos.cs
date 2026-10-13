namespace LmuTracker.Application.Lmu;

// Busca global (paleta Ctrl+K). Pilotos pelo id interno, nunca pelo SteamID.

public record SearchDriverDto(
    Guid DriverId, string Name, string? Nationality, string? DriverRankTitle, int? DriverRankTier, int Races);

public record SearchTrackDto(string Track, int Races);

public record SearchCarDto(string Car, string? CarClass, int Starts);

public record SearchRaceDto(
    string EventId, string? Title, string? Track, string? Tier, DateTime StartsAt, string? Classes);

public record SearchResultsDto(
    string Query,
    IReadOnlyList<SearchDriverDto> Drivers,
    IReadOnlyList<SearchTrackDto> Tracks,
    IReadOnlyList<SearchCarDto> Cars,
    IReadOnlyList<SearchRaceDto> Races);

public interface ISearchService
{
    /// <summary>Até alguns itens por grupo. Termos com menos de 2 letras devolvem tudo vazio.</summary>
    Task<SearchResultsDto> SearchAsync(string? query, CancellationToken ct = default);

    /// <summary>Pistas com corrida guardada (para filtros), da mais corrida para a menos.</summary>
    Task<IReadOnlyList<SearchTrackDto>> ListTracksAsync(CancellationToken ct = default);
}
