using System.Reflection;
using LmuTracker.Application.Lmu;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace LmuTracker.Api.Og;

/// <summary>
/// Imagens Open Graph (1200×630) para o link do Pitwall virar um card no Discord/WhatsApp: piloto, corrida e
/// resultado. Desenhadas no servidor com SkiaSharp e a fonte do site (Saira Condensed, OFL, embutida), e guardadas
/// 10 min em memória.
/// </summary>
public sealed class OgCards
{
    public const int W = 1200, H = 630;
    private const float Pad = 72;
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private static readonly SKTypeface Bold = LoadFont("SairaCondensed-Bold.ttf");
    private static readonly SKTypeface Semi = LoadFont("SairaCondensed-SemiBold.ttf");
    private static readonly SKTypeface Medium = LoadFont("SairaCondensed-Medium.ttf");

    private static readonly SKColor Bg0 = SKColor.Parse("#070b16");
    private static readonly SKColor Bg = SKColor.Parse("#0d1a38");
    private static readonly SKColor Fg = SKColor.Parse("#f2f5ff");
    private static readonly SKColor Soft = SKColor.Parse("#c9d2ea");
    private static readonly SKColor Muted = SKColor.Parse("#9aa6c4");
    private static readonly SKColor Accent = SKColor.Parse("#c8f031");
    private static readonly SKColor AccentInk = SKColor.Parse("#0b1200");
    private static readonly SKColor Line = SKColor.Parse("#22325a");

    private readonly ILmuQueryService _lmu;
    private readonly IMemoryCache _cache;

    public OgCards(ILmuQueryService lmu, IMemoryCache cache)
    {
        _lmu = lmu;
        _cache = cache;
    }

    private static SKTypeface LoadFont(string file)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
        using var s = asm.GetManifestResourceStream(name)!;
        using var data = SKData.Create(s);
        return SKTypeface.FromData(data);
    }

    private Task<byte[]?> Cached(string key, Func<Task<byte[]?>> make) =>
        _cache.GetOrCreateAsync($"og:{key}", async e => { e.AbsoluteExpirationRelativeToNow = CacheFor; return await make(); });

    // ---------------------------------------------------------------- cards

    public Task<byte[]?> DriverAsync(Guid driverId, CancellationToken ct) => Cached($"d:{driverId}", async () =>
    {
        var d = await _lmu.GetDriverAsync(driverId, Guid.Empty, 1, ct);
        if (d is null) return null;
        var s = d.Stats;
        return Render(c =>
        {
            Eyebrow(c, "PERFIL DO PILOTO");
            Title(c, d.Name, 250, 104);
            var x = Pad;
            if (d.Nationality is not null) x = Pill(c, d.Nationality, Pad, 286) + 18;
            Text(c, $"DR {RankText(d.DriverRankTitle, d.DriverRankTier)}", x, 324, 40, RankColor(d.DriverRankTitle), Bold);
            Text(c, $"SR {RankText(d.SafetyRankTitle, d.SafetyRankTier)}", x + 300, 324, 40, RankColor(d.SafetyRankTitle), Bold);
            Stats(c, 390, ("corridas", s.Races.ToString()), ("vitórias", s.Wins.ToString()), ("pódios", s.Podiums.ToString()),
                ("poles", s.Poles.ToString()), ("abandono", s.Races > 0 ? $"{Math.Round(100.0 * s.Dnfs / s.Races)}%" : "—"));
        });
    });

    public Task<byte[]?> RaceAsync(string eventId, CancellationToken ct) => Cached($"r:{eventId}", async () =>
    {
        var r = await _lmu.GetRaceAsync(eventId, ct);
        if (r is null) return null;
        var split = r.Splits.Count > 0 ? await _lmu.GetSplitAsync(eventId, r.Splits[0].SplitNo, ct) : null;
        var winners = (split?.Rows ?? []).Where(x => x.ClassPosition == 1).OrderBy(x => x.Position ?? 99).Take(3).ToList();
        return Render(c =>
        {
            Eyebrow(c, $"RESULTADO · {(r.Track ?? "").ToUpperInvariant()}");
            Title(c, r.Title ?? "Corrida", 240, 92);
            Text(c, $"{r.StartsAt:dd/MM/yyyy} · {r.Splits.Count} splits · {r.Splits.Sum(s => s.Drivers)} pilotos", Pad, 296, 34, Soft, Medium);
            var y = 366f;
            Text(c, winners.Count > 0 ? "VENCEDORES DO SPLIT 1" : "SEM RESULTADO AINDA", Pad, y, 26, Muted, Semi);
            y += 26;
            foreach (var w in winners)
            {
                ClassTag(c, w.CarClass, Pad, y);
                Text(c, w.Name, Pad + 128, y + 38, 40, Fg, Bold, W - Pad * 2 - 128);
                y += 58;
            }
        });
    });

    public Task<byte[]?> ResultAsync(Guid resultId, CancellationToken ct) => Cached($"x:{resultId}", async () =>
    {
        var x = await _lmu.GetResultAsync(resultId, ct);
        if (x is null) return null;
        var row = x.Row;
        var finished = row.FinishStatus == "Finished Normally";
        var pos = finished && row.ClassPosition is { } p ? $"P{p}" : (row.FinishStatus == "DNF" ? "DNF" : "—");
        return Render(c =>
        {
            Eyebrow(c, $"{(x.Title ?? "CORRIDA").ToUpperInvariant()} · {(x.Track ?? "").ToUpperInvariant()}");
            using (var font = new SKFont(Bold, 230))
            using (var paint = new SKPaint { Color = Accent, IsAntialias = true })
                c.DrawText(pos, W - Pad, 360, SKTextAlign.Right, font, paint);
            var posWidth = Measure(pos, Bold, 230);
            Title(c, row.Name, 250, 96, W - Pad * 2 - posWidth - 40);
            ClassTag(c, row.CarClass, Pad, 290);
            Text(c, row.VehName ?? "", Pad + 128, 326, 32, Soft, Medium, W - Pad * 2 - posWidth - 170);
            Stats(c, 400, ("largada", row.ClassGridPos is { } g ? $"P{g}" : "—"), ("melhor volta", Lap(row.BestLapTime)),
                ("voltas", row.Laps.ToString()), ("pits", row.PitStops.ToString()), ("split", x.SplitNo.ToString()));
        });
    });

    /// <summary>Card genérico do site (página inicial e o resto).</summary>
    public Task<byte[]?> SiteAsync() => Cached("site", () => Task.FromResult<byte[]?>(Render(c =>
    {
        Eyebrow(c, "LE MANS ULTIMATE");
        Title(c, "Cada corrida oficial,", 260, 100);
        Title(c, "cronometrada.", 360, 100);
        Text(c, "Resultados, rankings por pista, perfis de piloto e comparação direta.", Pad, 440, 36, Soft, Medium, W - Pad * 2);
    })));

    // ---------------------------------------------------------------- desenho

    private static byte[] Render(Action<SKCanvas> body)
    {
        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        using (var bg = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, H), [Bg, Bg0], SKShaderTileMode.Clamp) })
            c.DrawRect(0, 0, W, H, bg);
        using (var glow = new SKPaint { Shader = SKShader.CreateRadialGradient(new SKPoint(W - 120, -40), 720, [Accent.WithAlpha(0x40), SKColors.Transparent], SKShaderTileMode.Clamp) })
            c.DrawRect(0, 0, W, H, glow);
        using (var glow2 = new SKPaint { Shader = SKShader.CreateRadialGradient(new SKPoint(-80, 40), 620, [SKColor.Parse("#3b9eff").WithAlpha(0x30), SKColors.Transparent], SKShaderTileMode.Clamp) })
            c.DrawRect(0, 0, W, H, glow2);
        Logo(c);
        body(c);
        Rect(c, Pad, H - 76, W - Pad * 2, 2, Line);
        Text(c, "lmutracker.gamestrackers.com", Pad, H - 34, 26, Muted, Medium);
        using var img = surface.Snapshot();
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static void Logo(SKCanvas c)
    {
        // Bandeira quadriculada 2×2 + "PITWALL", como na barra do site.
        const float x = Pad, y = 60, s = 20;
        Rect(c, x, y, s, s, Accent);
        Rect(c, x + s, y + s, s, s, Accent);
        Rect(c, x + s, y, s, s, AccentInk);
        Rect(c, x, y + s, s, s, AccentInk);
        Text(c, "PITWALL", x + 2 * s + 16, y + 36, 44, Fg, Bold);
    }

    private static void Eyebrow(SKCanvas c, string text) => Text(c, text, Pad, 160, 26, Accent, Semi, W - Pad * 2);

    /// <summary>Título em caixa alta que encolhe até caber numa linha (ou corta com reticências).</summary>
    private static void Title(SKCanvas c, string text, float baseline, float size, float maxWidth = W - Pad * 2)
    {
        text = text.ToUpperInvariant();
        while (size > 44 && Measure(text, Bold, size) > maxWidth) size -= 4;
        Text(c, text, Pad, baseline, size, Fg, Bold, maxWidth);
    }

    private static void Text(SKCanvas c, string text, float x, float baseline, float size, SKColor color, SKTypeface face, float maxWidth = 0)
    {
        if (maxWidth > 0) text = Ellipsize(text, face, size, maxWidth);
        using var font = new SKFont(face, size);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        c.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
    }

    private static float Measure(string text, SKTypeface face, float size)
    {
        using var font = new SKFont(face, size);
        return font.MeasureText(text);
    }

    private static string Ellipsize(string text, SKTypeface face, float size, float max)
    {
        if (Measure(text, face, size) <= max) return text;
        while (text.Length > 1 && Measure(text + "…", face, size) > max) text = text[..^1];
        return text.TrimEnd() + "…";
    }

    private static void Rect(SKCanvas c, float x, float y, float w, float h, SKColor color, float radius = 0)
    {
        using var p = new SKPaint { Color = color, IsAntialias = true };
        if (radius > 0) c.DrawRoundRect(x, y, w, h, radius, radius, p);
        else c.DrawRect(x, y, w, h, p);
    }

    /// <summary>Pílula com texto; devolve onde ela termina (x).</summary>
    private static float Pill(SKCanvas c, string text, float x, float y)
    {
        var w = Measure(text, Semi, 30) + 32;
        Rect(c, x, y, w, 50, new SKColor(255, 255, 255, 0x1a), 25);
        Text(c, text, x + 16, y + 36, 30, Soft, Semi);
        return x + w;
    }

    private static void ClassTag(SKCanvas c, string? cls, float x, float y)
    {
        var (label, color) = ClassStyle(cls);
        Rect(c, x, y, 108, 48, color, 24);
        using var font = new SKFont(Bold, 30);
        using var paint = new SKPaint { Color = Bg0, IsAntialias = true };
        c.DrawText(label, x + 54, y + 35, SKTextAlign.Center, font, paint);
    }

    /// <summary>Faixa de números (até 5), cada um com rótulo embaixo.</summary>
    private static void Stats(SKCanvas c, float y, params (string Label, string Value)[] items)
    {
        var w = (W - Pad * 2 - (items.Length - 1) * 16) / items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            var x = Pad + i * (w + 16);
            Rect(c, x, y, w, 118, new SKColor(255, 255, 255, 0x0f), 18);
            // Valor encolhe até caber (tempo de volta é mais largo que um número de corridas), sem reticências.
            var size = 56f;
            while (size > 30 && Measure(items[i].Value, Bold, size) > w - 36) size -= 2;
            Text(c, items[i].Value, x + 22, y + 66, size, Fg, Bold, w - 36);
            Text(c, items[i].Label, x + 22, y + 100, 26, Muted, Medium, w - 36);
        }
    }

    private static (string, SKColor) ClassStyle(string? cls)
    {
        var c = (cls ?? "").ToUpperInvariant();
        if (c.StartsWith("HYPER")) return ("HYPER", SKColor.Parse("#ff4f5e"));
        if (c.StartsWith("LMP2")) return ("LMP2", SKColor.Parse("#3b9eff"));
        if (c.StartsWith("LMP3")) return ("LMP3", SKColor.Parse("#b07cff"));
        if (c.Contains("GT3")) return ("GT3", SKColor.Parse("#20c997"));
        return (c.Length > 0 ? c.Replace('_', ' ') : "—", Muted);
    }

    private static string RankText(string? title, int? tier) => title is null ? "—" : $"{title}{(tier is null ? "" : $" {tier}")}";

    private static SKColor RankColor(string? title) => (title ?? "").ToLowerInvariant() switch
    {
        "bronze" => SKColor.Parse("#cd7f4e"),
        "silver" => SKColor.Parse("#c7ccd4"),
        "gold" => SKColor.Parse("#ffcf5a"),
        "platinum" => SKColor.Parse("#7fe3e0"),
        _ => Soft,
    };

    private static string Lap(double? s)
    {
        if (s is not { } v || v <= 0) return "—";
        var m = (int)(v / 60);
        return (m + ":" + (v - m * 60).ToString("00.000", System.Globalization.CultureInfo.InvariantCulture));
    }
}
