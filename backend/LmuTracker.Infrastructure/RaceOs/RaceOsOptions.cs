namespace LmuTracker.Infrastructure.RaceOs;

/// <summary>Configuração do acesso ao RaceOS e de como obter o ticket Steam da conta de serviço.</summary>
public class RaceOsOptions
{
    public const string Section = "RaceOs";

    /// <summary>Base da API online do RaceOS.</summary>
    public string BaseUrl { get; set; } = "https://raceos.gg";

    /// <summary>Porta da API local do jogo (ticket Steam no modo "jogo aberto").</summary>
    public int GamePort { get; set; } = 6397;

    /// <summary>
    /// Como obter o ticket Steam da conta de serviço: "localgame" (jogo aberto na porta acima) ou
    /// "steamkit" (login headless com credenciais — a ser implementado para o servidor).
    /// </summary>
    public string TicketProvider { get; set; } = "localgame";
}
