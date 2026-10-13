namespace LmuTracker.Infrastructure.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "LmuTracker";
    public string Audience { get; set; } = "LmuTracker";

    /// <summary>Segredo HMAC (>= 32 bytes). Em produção vem do .env, nunca versionado.</summary>
    public string Secret { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 60 * 24 * 7; // 7 dias
}
