namespace LmuTracker.Infrastructure.Linking;

/// <summary>Endereço público do site (front). Usado como realm/retorno do login Steam.</summary>
public class SiteOptions
{
    public const string Section = "Site";

    /// <summary>Ex.: http://localhost:5173 em dev; https://lmutracker.gamestrackers.com em produção.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5173";

    public string SteamReturnPath { get; set; } = "/vincular/retorno";

    public string Realm => BaseUrl.TrimEnd('/') + "/";
    public string SteamReturnUrl => BaseUrl.TrimEnd('/') + SteamReturnPath;
}
