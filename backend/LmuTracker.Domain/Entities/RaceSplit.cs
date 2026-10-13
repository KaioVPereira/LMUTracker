using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>Um split de uma corrida: a grelha real que correu junta. Um <see cref="ScanEvent"/> tem 1..N splits.</summary>
public class RaceSplit : Entity
{
    public Guid ScanEventId { get; set; }
    public ScanEvent ScanEvent { get; set; } = null!;

    public int SplitNo { get; set; }

    public int MostLapsCompleted { get; set; }

    /// <summary>Quantos carros largaram neste split.</summary>
    public int DriverCount { get; set; }

    /// <summary>
    /// O que o resultado do split traz além da classificação (incidentes, penalidades, equipes…), cru e
    /// comprimido — sem o chat. Extraído para colunas próprias quando cada tela precisar.
    /// </summary>
    public byte[]? ExtraData { get; set; }

    public ICollection<RaceResult> Results { get; set; } = new List<RaceResult>();
}
