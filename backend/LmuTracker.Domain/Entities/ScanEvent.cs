using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>Estágio de uma corrida no scanner, do momento em que aparece na agenda até os resultados virem.</summary>
public enum ScanEventStatus
{
    /// <summary>Vista na agenda, ainda não terminou (status "Waiting"/"Running" no RaceOS).</summary>
    Pending = 0,

    /// <summary>Já terminou (passou de <see cref="ScanEvent.RaceEndsAt"/>) e está na fila para baixar os resultados.</summary>
    Ready = 1,

    /// <summary>Resultados de todos os splits baixados e guardados.</summary>
    Fetched = 2,

    /// <summary>Falhou ao baixar — vai ser tentado de novo.</summary>
    Failed = 3,
}

/// <summary>
/// Uma instância de corrida da agenda do RaceOS (um horário de uma série). É a unidade que o scanner
/// descobre em <c>daily/list/{tier}</c> e, depois que termina, expande em splits + resultados.
/// Guardar o <see cref="EventId"/> cedo é o que importa: a agenda só mostra corridas futuras/próximas,
/// então o que não for capturado na janela some — mas, com o id em mãos, o resultado pode ser baixado depois.
/// </summary>
public class ScanEvent : Entity
{
    /// <summary>eventId do RaceOS (UUID). Chave natural para baixar <c>result?eventId=</c>.</summary>
    public string EventId { get; set; } = null!;

    public string SeriesId { get; set; } = null!;
    public string? Title { get; set; }

    /// <summary>daily | weekly | special | championship.</summary>
    public string EventType { get; set; } = null!;

    /// <summary>beginner | intermediate | advanced (quando se aplica).</summary>
    public string? Tier { get; set; }

    public string? Track { get; set; }
    public string? Scene { get; set; }

    /// <summary>Largada oficial do evento (treino + quali + corrida), igual ao cronograma do jogo.</summary>
    public DateTime? EventStartsAt { get; set; }

    /// <summary>Início e fim previstos da SESSÃO de corrida (depois de treino e quali).</summary>
    public DateTime RaceStartsAt { get; set; }
    public DateTime RaceEndsAt { get; set; }

    /// <summary>Status cru vindo do RaceOS (Waiting, Running, etc.), para diagnóstico.</summary>
    public string? RaceOsStatus { get; set; }

    /// <summary>Número de splits, preenchido por <c>event/overview</c> quando vamos baixar.</summary>
    public int? TotalSplits { get; set; }

    public ScanEventStatus Status { get; set; } = ScanEventStatus.Pending;

    /// <summary>Quando os resultados foram baixados com sucesso (null até lá).</summary>
    public DateTime? FetchedAt { get; set; }

    /// <summary>Tentativas de baixar que falharam, para backoff.</summary>
    public int FetchAttempts { get; set; }

    /// <summary>
    /// Versão do detalhe gravado. Quando o scanner passa a guardar mais coisa (ex.: volta a volta), corridas
    /// baixadas com versão menor voltam para a fila enquanto o resultado ainda existe no RaceOS.
    /// </summary>
    public int DetailVersion { get; set; }

    /// <summary>
    /// Os revezamentos de piloto (carros divididos) já foram lidos para esta corrida. O download novo já lê; as
    /// corridas antigas são lidas aos poucos do resultado cru guardado (ScannerService.BackfillCrewAsync).
    /// </summary>
    public bool CrewChecked { get; set; }

    /// <summary>Classes que correram (ex.: "Hyper,GT3"), na ordem fixa do site — base dos filtros por classe.</summary>
    public string? Classes { get; set; }

    /// <summary>
    /// Configuração do evento como veio da agenda (sessões com horário no jogo e clima, regras, carros),
    /// JSON comprimido. Só existe para corridas vistas na agenda a partir do scanner v3.
    /// </summary>
    public byte[]? ConfigData { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

    public ICollection<RaceSplit> Splits { get; set; } = new List<RaceSplit>();
}
