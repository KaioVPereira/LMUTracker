using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LmuTracker.Infrastructure.RaceOs;

/// <summary>Sessão Steam persistida pelo bootstrap e reusada pelo servidor sem precisar de senha/Steam Guard.</summary>
public sealed record SteamSession(string AccountName, string RefreshToken, string? GuardData);

/// <summary>Lê/grava a <see cref="SteamSession"/> em arquivo JSON (ver aviso de segurança em SteamKitOptions).</summary>
public sealed class SteamSessionStore
{
    private readonly string _path;

    public SteamSessionStore(IOptions<SteamKitOptions> opt)
    {
        _path = opt.Value.SessionStorePath;
    }

    public SteamSession? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<SteamSession>(File.ReadAllText(_path));
        }
        catch
        {
            return null;
        }
    }

    public void Save(SteamSession session)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(session));
    }
}
