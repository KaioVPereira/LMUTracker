using System.Net;
using System.Text.RegularExpressions;
using LmuTracker.Application.Lmu;
using LmuTracker.Infrastructure.Linking;
using Microsoft.Extensions.Options;

namespace LmuTracker.Api.Og;

/// <summary>
/// Meta tags Open Graph/Twitter no index.html servido para cada rota do site. Discord, WhatsApp e afins não rodam o
/// JavaScript do site: leem só o HTML. Perfil, corrida e resultado ganham título, descrição e o card próprio; o
/// resto, o card genérico.
/// </summary>
public sealed partial class SpaMeta
{
    [GeneratedRegex(@"^/pilotos/(?<id>[0-9a-fA-F-]{36})/?$")] private static partial Regex DriverPath();
    [GeneratedRegex(@"^/corridas/(?<id>[0-9a-fA-F-]{36})/?$")] private static partial Regex RacePath();
    [GeneratedRegex(@"^/resultados/(?<id>[0-9a-fA-F-]{36})/?$")] private static partial Regex ResultPath();
    [GeneratedRegex(@"<title>.*?</title>", RegexOptions.Singleline)] private static partial Regex TitleTag();

    private const string SiteName = "Pitwall";
    private const string DefaultDescription =
        "Estatísticas do Le Mans Ultimate: resultados das corridas oficiais, rankings por pista, perfis de piloto e comparação direta.";

    private readonly ILmuQueryService _lmu;
    private readonly string _base;

    public SpaMeta(ILmuQueryService lmu, IOptions<SiteOptions> site)
    {
        _lmu = lmu;
        _base = site.Value.BaseUrl.TrimEnd('/');
    }

    public async Task<string> RenderAsync(string html, string path, CancellationToken ct)
    {
        var (title, description, image) = await DescribeAsync(path, ct);
        var url = _base + path;
        var tags = string.Join("\n    ",
            Meta("og:site_name", SiteName), Meta("og:type", "website"), Meta("og:title", title),
            Meta("og:description", description), Meta("og:url", url), Meta("og:image", _base + image),
            Meta("og:image:width", "1200"), Meta("og:image:height", "630"),
            Name("twitter:card", "summary_large_image"), Name("twitter:title", title),
            Name("twitter:description", description), Name("twitter:image", _base + image),
            Name("description", description));
        html = TitleTag().Replace(html, $"<title>{WebUtility.HtmlEncode(title)}</title>", 1);
        return html.Replace("</head>", $"    {tags}\n  </head>", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string Title, string Description, string Image)> DescribeAsync(string path, CancellationToken ct)
    {
        try
        {
            if (DriverPath().Match(path) is { Success: true } dm && Guid.TryParse(dm.Groups["id"].Value, out var did)
                && await _lmu.GetDriverAsync(did, Guid.Empty, 1, ct) is { } d)
            {
                var s = d.Stats;
                var rank = d.DriverRankTitle is null ? "" : $"{d.DriverRankTitle} {d.DriverRankTier} · ";
                return ($"{d.Name} — {SiteName}",
                    $"{rank}{s.Races} corridas · {s.Wins} vitórias · {s.Podiums} pódios no Le Mans Ultimate.",
                    $"/og/pilotos/{did}.png");
            }
            if (RacePath().Match(path) is { Success: true } rm && await _lmu.GetRaceAsync(rm.Groups["id"].Value, ct) is { } r)
            {
                return ($"{r.Title ?? "Corrida"} · {r.Track} — {SiteName}",
                    $"{r.StartsAt:dd/MM/yyyy HH:mm} UTC · {r.Splits.Count} splits · {r.Splits.Sum(x => x.Drivers)} pilotos. Resultado, posições volta a volta e ritmo.",
                    $"/og/corridas/{r.EventId}.png");
            }
            if (ResultPath().Match(path) is { Success: true } xm && Guid.TryParse(xm.Groups["id"].Value, out var xid)
                && await _lmu.GetResultAsync(xid, ct) is { } x)
            {
                var pos = x.Row.FinishStatus == "Finished Normally" && x.Row.ClassPosition is { } p ? $"P{p} na classe" : "não terminou";
                return ($"{x.Row.Name}: {pos} em {x.Title} — {SiteName}",
                    $"{x.Track} · split {x.SplitNo} · {x.Row.VehName}. Volta a volta, pneus e combustível.",
                    $"/og/resultados/{xid}.png");
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Sem meta específica, a página continua funcionando com a genérica.
        }
        return ($"{SiteName} — estatísticas do Le Mans Ultimate", DefaultDescription, "/og/site.png");
    }

    private static string Meta(string property, string content) =>
        $"<meta property=\"{property}\" content=\"{WebUtility.HtmlEncode(content)}\" />";

    private static string Name(string name, string content) =>
        $"<meta name=\"{name}\" content=\"{WebUtility.HtmlEncode(content)}\" />";
}
