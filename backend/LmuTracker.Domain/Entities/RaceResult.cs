using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>
/// Resultado de um piloto num split: a linha de classificação mais o que o RaceOS reporta de variação
/// de Driver Rank e Safety Rank naquela corrida. É por aqui que um usuário vinculado é reencontrado:
/// casa-se <see cref="SteamId"/> com o SteamID provado no login.
/// </summary>
public class RaceResult : Entity
{
    // Tamanhos das colunas de texto que as agregações leem. nvarchar(max) faz o SQL Server tratar MAX/GROUP BY como
    // LOB (MAX(Nationality) sozinho levava ~5 s em 270 mil linhas); o scanner corta o que passar disso.
    public const int NameMax = 96;
    public const int CarTypeMax = 96;
    public const int NationalityMax = 16;
    public const int CarClassMax = 32;
    public const int FinishStatusMax = 32;
    public const int RankTitleMax = 32;

    public Guid RaceSplitId { get; set; }
    public RaceSplit RaceSplit { get; set; } = null!;

    // ---------- Identidade do piloto ----------
    public string? SteamId { get; set; }
    public string? RaceOsPlayerId { get; set; }
    public string Name { get; set; } = null!;
    public string? Nationality { get; set; }
    public string? Badge { get; set; }

    // ---------- Carro / equipe ----------
    public string? CarClass { get; set; }
    public string? CarNumber { get; set; }
    public string? CarType { get; set; }
    public string? VehName { get; set; }
    public string? TeamName { get; set; }

    // ---------- Classificação ----------
    public int? GridPos { get; set; }
    public int? Position { get; set; }
    public int? ClassGridPos { get; set; }
    public int? ClassPosition { get; set; }
    public int Laps { get; set; }

    /// <summary>Melhor volta em segundos (null quando não marcou volta válida).</summary>
    public double? BestLapTime { get; set; }

    /// <summary>Tempo total em segundos (null quando não terminou).</summary>
    public double? FinishTime { get; set; }

    public string? FinishStatus { get; set; }
    public int Points { get; set; }

    // ---------- Variação de rating nesta corrida (o que a visão pública entrega de terceiros) ----------
    public int? DriverRankAdjustment { get; set; }
    public int? SafetyRankAdjustment { get; set; }
    public bool JokerPlayed { get; set; }

    // ---------- Rating do piloto NESTA corrida (retrato público: rank, tier, progresso arredondado) ----------
    public string? DriverRankTitle { get; set; }
    public int? DriverRankTier { get; set; }
    public int? DriverRankProgress { get; set; }
    public string? SafetyRankTitle { get; set; }
    public int? SafetyRankTier { get; set; }
    public int? SafetyRankProgress { get; set; }

    // ---------- Quali ----------
    public int? QualiPosition { get; set; }
    public int? QualiClassPosition { get; set; }
    public double? QualiBestLapTime { get; set; }

    // ---------- Volta a volta ----------
    public int PitStops { get; set; }

    // ---------- Incidentes (do stream da corrida; null = corrida sem o resultado cru guardado) ----------

    /// <summary>Contatos relatados pelo piloto (com carro ou muro), como o jogo conta.</summary>
    public int? Incidents { get; set; }

    /// <summary>Penalidades recebidas na corrida.</summary>
    public int? Penalties { get; set; }

    /// <summary>
    /// Voltas da corrida (tempo, setores, posição, combustível, pneus, desgaste, pit) em JSON compacto
    /// comprimido com gzip — ~1 KB por piloto. Ver LapCodec. Null em resultados gravados antes disso.
    /// </summary>
    public byte[]? LapsData { get; set; }

    /// <summary>Campos da linha do piloto que o scanner não lê um a um (penalidades etc.), crus e comprimidos.</summary>
    public byte[]? ExtraData { get; set; }
}
