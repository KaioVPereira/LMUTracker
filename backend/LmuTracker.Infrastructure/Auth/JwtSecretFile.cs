using System.Security.Cryptography;

namespace LmuTracker.Infrastructure.Auth;

/// <summary>
/// Segredo JWT de produção sem passo manual: na primeira subida gera 64 bytes aleatórios e guarda no
/// arquivo (num volume, sobrevive a redeploys); depois só lê. Assim o segredo nunca vai para o repositório.
/// </summary>
public static class JwtSecretFile
{
    public static string LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 32) return existing;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        File.WriteAllText(path, secret);
        return secret;
    }
}
