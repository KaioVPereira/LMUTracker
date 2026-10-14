using LmuTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Api.Controllers;

/// <summary>
/// App Android para download. A versão liberada fica na tabela AppReleases; o APK fica em disco, em
/// <c>AppReleases:Directory</c> (em produção, uma pasta do servidor montada no contêiner). Público de propósito: o
/// site oferece o download antes de a pessoa ter o app, e o app consulta sem login.
/// </summary>
[ApiController]
[Route("api/app")]
public class AppController(AppDbContext db, IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    private const string DownloadPath = "/api/app/android/download";

    /// <summary>Sem arquivo publicado, <paramref name="Available"/> vem false e o resto nulo.</summary>
    public record AndroidReleaseDto(
        bool Available, string? Version, int? VersionCode, string? Notes, DateTime? PublishedAt, long? SizeBytes, string DownloadUrl);

    [HttpGet("android")]
    public async Task<AndroidReleaseDto> GetAndroid(CancellationToken ct)
    {
        // Sem cache no navegador: o aviso de versão nova tem que aparecer assim que ela for publicada.
        Response.Headers.CacheControl = "no-cache";
        var release = await LatestAsync(ct);
        var file = release is null ? null : FileFor(release.FileName);
        if (release is null || file is null) return new(false, null, null, null, null, null, DownloadPath);
        return new(true, release.Version, release.VersionCode, release.Notes, release.PublishedAt, file.Length, DownloadPath);
    }

    [HttpGet("android/download")]
    public async Task<IActionResult> Download(CancellationToken ct)
    {
        var release = await LatestAsync(ct);
        var file = release is null ? null : FileFor(release.FileName);
        if (release is null || file is null) return NotFound();
        return PhysicalFile(file.FullName, "application/vnd.android.package-archive", $"pitwall-{release.Version}.apk");
    }

    private Task<Domain.Entities.AppRelease?> LatestAsync(CancellationToken ct) =>
        db.AppReleases.AsNoTracking().OrderByDescending(x => x.VersionCode).FirstOrDefaultAsync(ct);

    private FileInfo? FileFor(string fileName)
    {
        var dir = config["AppReleases:Directory"] is { Length: > 0 } d ? d : Path.Combine(env.ContentRootPath, "App_Data", "apk");
        // Só o nome do arquivo: nada vindo do banco sai do diretório dos APKs.
        var file = new FileInfo(Path.Combine(dir, Path.GetFileName(fileName)));
        return file.Exists ? file : null;
    }
}
