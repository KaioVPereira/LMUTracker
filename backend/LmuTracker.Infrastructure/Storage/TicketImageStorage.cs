using LmuTracker.Application.Common;
using Microsoft.Extensions.Logging;

namespace LmuTracker.Infrastructure.Storage;

public interface ITicketImageStorage
{
    /// <summary>Salva a imagem (já decodificada) e devolve o caminho público (ex.: "/api/uploads/tickets/xxx.png").</summary>
    Task<string> SaveAsync(byte[] bytes, string contentType, CancellationToken ct = default);

    /// <summary>Remove um arquivo salvo anteriormente.</summary>
    void Delete(string? publicPath);
}

/// <summary>
/// Prints de chamado em disco, numa pasta fora do build (em produção, no volume /data — a imagem do contêiner é
/// recriada a cada deploy). Servidos pela Api em <see cref="PublicPrefix"/>.
/// </summary>
public class LocalTicketImageStorage : ITicketImageStorage
{
    /// <summary>Caminho público dos arquivos. Fica sob /api para passar pelo proxy do Vite em dev, como o resto da Api.</summary>
    public const string PublicPrefix = "/api/uploads/tickets/";

    private const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
    };

    private readonly string _directory;
    private readonly ILogger<LocalTicketImageStorage> _logger;

    /// <param name="directory">Pasta dos prints de chamado.</param>
    public LocalTicketImageStorage(string directory, ILogger<LocalTicketImageStorage> logger)
    {
        _directory = Path.GetFullPath(directory);
        _logger = logger;
    }

    public async Task<string> SaveAsync(byte[] bytes, string contentType, CancellationToken ct = default)
    {
        if (bytes.Length == 0)
            throw new AppException("Anexo inválido: imagem vazia.");

        if (bytes.Length > MaxBytes)
            throw new AppException("Cada imagem deve ter no máximo 5 MB.");

        if (!AllowedTypes.TryGetValue(contentType, out var extension))
            throw new AppException("Formato não suportado. Use JPG, PNG, WEBP ou GIF.");

        // O tipo declarado no data URL vem do navegador: confere a assinatura do arquivo antes de servir de volta.
        if (!LooksLike(bytes, extension))
            throw new AppException("Anexo inválido: o arquivo não é uma imagem.");

        Directory.CreateDirectory(_directory);

        // Nome aleatório: evita colisão e impede path traversal via nome enviado.
        var fileName = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(_directory, fileName), bytes, ct);

        return PublicPrefix + fileName;
    }

    public void Delete(string? publicPath)
    {
        if (string.IsNullOrWhiteSpace(publicPath) || !publicPath.StartsWith(PublicPrefix, StringComparison.Ordinal))
            return;

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_directory, Path.GetFileName(publicPath)));
            // Confere que o caminho resolvido continua dentro da pasta de prints.
            if (!fullPath.StartsWith(_directory, StringComparison.Ordinal))
                return;

            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao remover a imagem de chamado {Path}.", publicPath);
        }
    }

    private static bool LooksLike(byte[] b, string extension) => extension switch
    {
        ".png" => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47,
        ".jpg" => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        ".gif" => b.Length > 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38,
        ".webp" => b.Length > 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
                   && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50,
        _ => false,
    };
}
