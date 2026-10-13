using System.Text.RegularExpressions;

namespace LmuTracker.Infrastructure.Scanning;

/// <summary>
/// Nome de pista para exibir. O RaceOS manda um nome interno (MonzaWEC) e um "amigável" que às vezes é limpo
/// ("Monza") e às vezes vem com sufixo interno ("FujiWEC_2023", "LongBeach_2026").
/// </summary>
public static partial class TrackNames
{
    // Casos que a limpeza automática não acerta (acento, nome comercial).
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Portimao"] = "Portimão",
        ["Spa"] = "Spa-Francorchamps",
        ["Spa Francorchamps"] = "Spa-Francorchamps",
        ["COTA"] = "COTA",
        ["Qatar"] = "Lusail",
        ["Interlagos"] = "Interlagos",
    };

    [GeneratedRegex(@"_\d{4}$")] private static partial Regex YearSuffix();
    [GeneratedRegex(@"(WEC|ELMS|IMSA)$")] private static partial Regex SeriesSuffix();
    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")] private static partial Regex CamelBreak();

    public static string? Display(string? friendly, string? internalName)
    {
        var raw = !string.IsNullOrWhiteSpace(friendly) ? friendly : internalName;
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Trim();
        // Já vem limpo (tem espaço ou hífen e nenhum sufixo interno): usa como está.
        if (!s.Contains('_') && (s.Contains(' ') || s.Contains('-')) && !SeriesSuffix().IsMatch(s))
            return Known.GetValueOrDefault(s, s);

        s = YearSuffix().Replace(s, "");
        s = SeriesSuffix().Replace(s, "");
        s = CamelBreak().Replace(s, " ").Replace('_', ' ').Trim();
        return Known.GetValueOrDefault(s, s);
    }
}
