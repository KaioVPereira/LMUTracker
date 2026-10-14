namespace LmuTracker.Application.Lmu;

// Rankings de pilotos e de carros (as abas da página Rankings, além das voltas mais rápidas).

/// <summary>
/// Uma linha de ranking de piloto. <see cref="Value"/> é a métrica da aba (DR/SR: pontuação; vitórias: vitórias;
/// em alta: DR ganho; mais limpos: incidentes por corrida; mais ativos: corridas; poles: poles; recuperação:
/// posições ganhas) e <see cref="Extra"/> a secundária (vitórias e poles: taxa; em alta: SR ganho; mais limpos:
/// penalidades por corrida; mais ativos: voltas; recuperação: posições ganhas por corrida).
/// </summary>
public record RankingRowDto(
    int Rank, Guid? DriverId, string Name, string? Nationality,
    string? DriverRankTitle, int? DriverRankTier, int? DriverRankProgress,
    string? SafetyRankTitle, int? SafetyRankTier, int? SafetyRankProgress,
    int Starts, int Wins, int Podiums, double Value, double? Extra);

/// <summary><see cref="Me"/>: a linha do piloto pedido em <c>me</c>, mesmo fora do topo (null se ele não entra no recorte).</summary>
public record RankingDto(
    string Kind, string? CarClass, string? Country, string Period, int MinStarts, int Total, IReadOnlyList<RankingRowDto> Rows, RankingRowDto? Me);

public record CarRankingRowDto(
    int Rank, string Car, string? CarClass, int Starts, int Wins, int Podiums, int Drivers, double WinRate, double? AvgClassFinish);

public record CarRankingDto(string? CarClass, string? Country, string Period, int MinStarts, int Total, IReadOnlyList<CarRankingRowDto> Rows);

/// <summary>Um país no filtro dos rankings: código de nacionalidade do jogo (ex.: "BR") e quantos pilotos o usam.</summary>
public record CountryDto(string Code, int Drivers);

public interface IRankingsService
{
    /// <summary>
    /// kind: dr | sr | wins | risers | clean | active | poles | gains. period: 7d | 30d | all. carClass: hyper|lmp2|lmp3|gt3 (não vale
    /// para dr/sr, que são do piloto). country: nacionalidade (ex.: "BR") — a posição passa a ser dentro do país.
    /// <paramref name="me"/>: id do piloto a destacar.
    /// </summary>
    Task<RankingDto?> DriversAsync(string kind, string? carClass, string? period, string? country, int take, Guid? me,
        CancellationToken ct = default);

    /// <summary>country: conta só as largadas de pilotos daquele país.</summary>
    Task<CarRankingDto> CarsAsync(string? carClass, string? period, string? country, CancellationToken ct = default);

    Task<IReadOnlyList<CountryDto>> CountriesAsync(CancellationToken ct = default);
}
