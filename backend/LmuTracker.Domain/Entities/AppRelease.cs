using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>
/// Versão do app Android liberada para download (igual ao Finanças e ao Deu Quanto). É global, não por usuário.
/// O APK fica em disco (<c>AppReleases:Directory</c>); aqui ficam a versão e o nome do arquivo. O app instalado
/// compara o próprio versionCode com o maior daqui para saber se tem atualização.
/// </summary>
public class AppRelease : Entity
{
    /// <summary>versionName do Android, o que a pessoa vê (ex.: "1.0.0").</summary>
    public string Version { get; set; } = "";

    /// <summary>versionCode do Android: inteiro que só cresce. É ele que decide qual é a mais nova.</summary>
    public int VersionCode { get; set; }

    /// <summary>Nome do arquivo dentro do diretório dos APKs (só o nome, nunca um caminho).</summary>
    public string FileName { get; set; } = "";

    /// <summary>O que mudou, mostrado no aviso de atualização.</summary>
    public string? Notes { get; set; }

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
}
