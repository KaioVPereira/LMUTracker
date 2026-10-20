using LmuTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Api.Controllers;

/// <summary>
/// Novidades do Pitwall (o mural que abre no acesso quando há algo novo, e a página /novidades). As entradas ficam
/// no código, como no GameTracker: para anunciar algo, acrescente uma entrada com data mais recente e uma Key nova
/// (nunca reutilize uma Key). Quem está logado guarda no servidor até onde já viu (vale no site e no app); quem não
/// está logado guarda no próprio navegador.
/// </summary>
[Route("api/changelog")]
public class ChangelogController(AppDbContext db) : ApiControllerBase
{
    /// <param name="Kind">feature | improvement | fix</param>
    /// <param name="Url">Onde conferir (rota do site); "/perfil" leva ao perfil do piloto vinculado.</param>
    public record ChangelogEntryDto(string Key, string Title, string Body, string Kind, string? Url, DateTime Date);

    public static readonly IReadOnlyList<ChangelogEntryDto> Entries =
    [
        new("pit-lap-countries",
            "Volta do pit no gráfico e países em ordem",
            "No gráfico de ritmo, a linha do pit agora diz em que volta ele foi feito. E o filtro de país dos rankings " +
            "ficou em ordem alfabética, com o número de pilotos de cada país ao lado.",
            "fix", "/rankings", new DateTime(2026, 10, 20, 1, 30, 0, DateTimeKind.Utc)),

        new("result-detail-revamp",
            "Sua corrida, com tudo num lugar só",
            "A página do piloto na corrida ganhou o que antes só existia na página da corrida: o gráfico de posições volta " +
            "a volta, o ritmo comparado com quem estava perto, a estratégia de pneus e paradas, as penalidades e uma tabela " +
            "com os adversários diretos. Dá para mostrar a corrida de alguém sem mandar a pessoa ficar trocando de tela.",
            "feature", "/perfil", new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc)),

        new("profile-recent-races",
            "Últimas corridas no topo do perfil",
            "As 5 corridas mais recentes agora aparecem logo no começo do perfil, com largada, chegada e saldo de DR. " +
            "O histórico completo continua no fim da página.",
            "improvement", "/perfil", new DateTime(2026, 10, 20, 12, 5, 0, DateTimeKind.Utc)),

        new("driver-compare",
            "Compare-se com qualquer piloto",
            "No perfil, o botão Comparar abre um comparativo por pista e classe: melhor volta setor a setor (onde cada um " +
            "ganha ou perde tempo), volta ideal, ritmo médio, constância, chegadas e as corridas de cada um ali. O adversário " +
            "pode ser qualquer piloto da busca — ou um atalho como \"quem mais venceu nesta pista\" e \"dono da volta mais rápida\".",
            "feature", "/perfil", new DateTime(2026, 10, 20, 12, 10, 0, DateTimeKind.Utc)),

        new("team-races",
            "Corridas de equipe corrigidas",
            "Nas corridas com troca de piloto (como as especiais de endurance), o resultado mostrava só quem terminou o carro. " +
            "Agora aparecem todos os pilotos do carro, com os turnos de cada um, e a corrida entra no perfil de todos eles — " +
            "com a melhor volta e as voltas de cada um, não as do colega. Os rankings também: a vitória, o pódio e a pole " +
            "contam para todos do carro, e no ranking de voltas e nos recordes cada um aparece com a volta que ele mesmo fez.",
            "fix", "/corridas?nivel=special", new DateTime(2026, 10, 20, 12, 15, 0, DateTimeKind.Utc)),

        new("mobile-charts",
            "Gráficos legíveis no celular",
            "No celular os gráficos abrem no tamanho certo para ler, em vez de espremidos na largura da tela: arraste para " +
            "os lados (e para baixo, nos mais altos) e use os botões − e + para afastar ou aproximar. Tocar no gráfico " +
            "mostra os números daquele ponto.",
            "improvement", "/corridas", new DateTime(2026, 10, 20, 12, 17, 0, DateTimeKind.Utc)),

        new("whats-new",
            "Central de Novidades",
            "Este mural: sempre que o Pitwall ganhar algo novo, ele aparece no seu próximo acesso. E a página Novidades " +
            "(no menu) guarda tudo o que já foi lançado.",
            "feature", "/novidades", new DateTime(2026, 10, 20, 12, 20, 0, DateTimeKind.Utc)),
    ];

    /// <summary>Todas as novidades, da mais recente para a mais antiga. Público.</summary>
    [HttpGet]
    public IReadOnlyList<ChangelogEntryDto> All() => Entries.OrderByDescending(e => e.Date).ToList();

    /// <summary>As que a pessoa logada ainda não viu (quem nunca viu recebe só as dos últimos 30 dias).</summary>
    [Authorize]
    [HttpGet("unseen")]
    public async Task<IReadOnlyList<ChangelogEntryDto>> Unseen(CancellationToken ct)
    {
        var seen = await db.Users.Where(u => u.Id == UserId).Select(u => u.LastSeenChangelogAt).FirstOrDefaultAsync(ct);
        var cutoff = seen ?? DateTime.UtcNow.AddDays(-30);
        return Entries.Where(e => e.Date > cutoff).OrderByDescending(e => e.Date).ToList();
    }

    /// <summary>Marca tudo como visto, até a novidade mais recente (mesmo que ela esteja datada à frente do relógio).</summary>
    [Authorize]
    [HttpPost("seen")]
    public async Task<IActionResult> Seen(CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return NotFound();
        var latest = Entries.Max(e => e.Date);
        var now = DateTime.UtcNow;
        user.LastSeenChangelogAt = now > latest ? now : latest;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
