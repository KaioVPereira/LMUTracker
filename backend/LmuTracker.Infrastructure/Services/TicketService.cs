using System.Text.RegularExpressions;
using LmuTracker.Application.Common;
using LmuTracker.Application.Lmu;
using LmuTracker.Application.Tickets;
using LmuTracker.Domain.Entities;
using LmuTracker.Domain.Enums;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

public partial class TicketService : ITicketService
{
    private const int MaxMessageLength = 5000;
    private const int MaxNoteLength = 2000;
    private const int MaxImages = 4;

    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/gif", "image/webp" };

    [GeneratedRegex(@"^data:(?<type>[\w/+.-]+);base64,(?<data>.+)$", RegexOptions.Singleline)]
    private static partial Regex DataUrlRegex();

    private readonly AppDbContext _db;
    private readonly ITicketImageStorage _images;

    public TicketService(AppDbContext db, ITicketImageStorage images)
    {
        _db = db;
        _images = images;
    }

    public async Task<int> SubmitAsync(Guid userId, SubmitTicketRequest request, CancellationToken ct = default)
    {
        var ticket = new Ticket
        {
            ReporterId = userId,
            Type = ParseType(request.Type),
            Message = ValidateMessage(request.Message),
        };

        ticket.Images.AddRange(await SaveImagesAsync(request.Images, alreadyAttached: 0, ct));

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        return ticket.Number;
    }

    private static string ValidateMessage(string? raw)
    {
        var message = raw?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            throw new AppException("Escreva a sua mensagem antes de enviar.");

        if (message.Length > MaxMessageLength)
            throw new AppException($"A mensagem é muito longa (máximo {MaxMessageLength:N0} caracteres).");

        return message;
    }

    // Só dois tipos: bug ou sugestão. Qualquer outra coisa é entrada inválida.
    private static TicketType ParseType(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "bug" => TicketType.Bug,
        "idea" or "improvement" or "suggestion" or "sugestao" => TicketType.Idea,
        _ => throw new AppException("Tipo de chamado inválido."),
    };

    /// <summary>
    /// Decodifica os data URLs, valida tipo/tamanho e grava cada imagem em disco. A ordem continua depois dos
    /// <paramref name="alreadyAttached"/> anexos que o chamado já tem.
    /// </summary>
    private async Task<List<TicketImage>> SaveImagesAsync(IReadOnlyList<string>? images, int alreadyAttached, CancellationToken ct)
    {
        var saved = new List<TicketImage>();
        if (images is null || images.Count == 0)
            return saved;

        if (alreadyAttached + images.Count > MaxImages)
            throw new AppException($"Você pode anexar no máximo {MaxImages} imagens.");

        var order = alreadyAttached;
        foreach (var dataUrl in images)
        {
            var match = DataUrlRegex().Match(dataUrl ?? "");
            if (!match.Success)
                throw new AppException("Anexo inválido: envie apenas imagens.");

            var contentType = match.Groups["type"].Value.ToLowerInvariant();
            if (!AllowedImageTypes.Contains(contentType))
                throw new AppException("Formato de imagem não suportado (use PNG, JPG, GIF ou WebP).");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(match.Groups["data"].Value);
            }
            catch (FormatException)
            {
                throw new AppException("Anexo inválido: não foi possível ler a imagem.");
            }

            var url = await _images.SaveAsync(bytes, contentType, ct);
            saved.Add(new TicketImage { Url = url, Order = order++ });
        }

        return saved;
    }

    public async Task<PagedDto<TicketListItemDto>> ListForAdminAsync(
        string? status, string? type, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _db.Tickets.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, true, out var statusValue))
            query = query.Where(t => t.Status == statusValue);

        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<TicketType>(type, true, out var typeValue))
            query = query.Where(t => t.Type == typeValue);

        return await PageAsync(query, page, pageSize, ct);
    }

    public async Task<TicketDetailDto> GetForAdminAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await WithDetail(_db.Tickets.AsNoTracking())
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new AppException("Chamado não encontrado.", 404);

        return ToDetailDto(ticket);
    }

    public Task<PagedDto<TicketListItemDto>> ListMineAsync(Guid userId, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => PageAsync(_db.Tickets.AsNoTracking().Where(t => t.ReporterId == userId), page, pageSize, ct);

    public async Task<TicketDetailDto> GetMineAsync(Guid userId, Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await WithDetail(_db.Tickets.AsNoTracking())
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.ReporterId == userId, ct)
            ?? throw new AppException("Chamado não encontrado.", 404);

        return ToDetailDto(ticket);
    }

    public async Task<TicketDetailDto> EditMineAsync(Guid userId, Guid ticketId, EditTicketRequest request, CancellationToken ct = default)
    {
        var type = ParseType(request.Type);
        var message = ValidateMessage(request.Message);

        var ticket = await WithDetail(_db.Tickets)
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.ReporterId == userId, ct)
            ?? throw new AppException("Chamado não encontrado.", 404);

        if (ticket.Status is TicketStatus.Completed or TicketStatus.Cancelled)
            throw new AppException("Este chamado já foi encerrado e não pode mais ser editado — se precisar, abra um novo.");

        // Só vale o que é deste chamado: URL desconhecida em KeepImages é ignorada.
        var keep = new HashSet<string>(request.KeepImages ?? [], StringComparer.Ordinal);
        var kept = ticket.Images.Where(i => keep.Contains(i.Url)).OrderBy(i => i.Order).ToList();
        var removed = ticket.Images.Where(i => !keep.Contains(i.Url)).ToList();

        var added = await SaveImagesAsync(request.NewImages, alreadyAttached: kept.Count, ct);

        foreach (var image in removed)
        {
            ticket.Images.Remove(image);
            _db.TicketImages.Remove(image);
        }

        for (var i = 0; i < kept.Count; i++)
            kept[i].Order = i;

        // Add explícito: Id já preenchido + pai rastreado faria o EF tratar a imagem nova como existente (UPDATE
        // em vez de INSERT).
        ticket.Images.AddRange(added);
        _db.TicketImages.AddRange(added);

        ticket.Type = type;
        ticket.Message = message;
        var now = DateTime.UtcNow;
        ticket.UpdatedAt = now;
        ticket.EditedAt = now;

        await _db.SaveChangesAsync(ct);

        // Só apaga o arquivo depois que o banco confirmou a remoção.
        foreach (var image in removed)
            _images.Delete(image.Url);

        return ToDetailDto(ticket);
    }

    public async Task<TicketDetailDto> AddUpdateAsync(Guid adminId, Guid ticketId, AddTicketUpdateRequest request, CancellationToken ct = default)
    {
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is not null && note.Length > MaxNoteLength)
            throw new AppException($"A resposta é muito longa (máximo {MaxNoteLength:N0} caracteres).");

        TicketStatus? newStatus = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<TicketStatus>(request.Status, true, out var parsed) || !Enum.IsDefined(parsed))
                throw new AppException("Status inválido.");
            newStatus = parsed;
        }

        if (newStatus is null && note is null)
            throw new AppException("Escreva uma resposta ou selecione um novo status.");

        var ticket = await WithDetail(_db.Tickets)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new AppException("Chamado não encontrado.", 404);

        var statusChanged = newStatus is not null && newStatus.Value != ticket.Status;
        if (newStatus is not null)
            ticket.Status = newStatus.Value;

        var admin = await _db.Users.FirstAsync(u => u.Id == adminId, ct);
        ticket.UpdatedAt = DateTime.UtcNow;

        var update = new TicketUpdate
        {
            Ticket = ticket,
            Admin = admin,
            Status = ticket.Status,
            StatusChanged = statusChanged,
            Note = note,
        };
        ticket.Updates.Add(update);
        // Mesmo motivo do Add explícito das imagens na edição.
        _db.TicketUpdates.Add(update);

        await _db.SaveChangesAsync(ct);
        return ToDetailDto(ticket);
    }

    private static IQueryable<Ticket> WithDetail(IQueryable<Ticket> q) => q
        .Include(t => t.Reporter)
        .Include(t => t.Images)
        .Include(t => t.Updates).ThenInclude(u => u.Admin)
        .AsSplitQuery();

    private static async Task<PagedDto<TicketListItemDto>> PageAsync(
        IQueryable<Ticket> query, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TicketListItemDto(
                t.Id,
                t.Number,
                new TicketPersonDto(t.Reporter.Username, t.Reporter.DisplayName),
                t.Type.ToString(),
                t.Message,
                t.Status.ToString(),
                t.CreatedAt,
                t.UpdatedAt,
                t.EditedAt))
            .ToListAsync(ct);

        return new PagedDto<TicketListItemDto>(items, page, pageSize, total);
    }

    private static TicketDetailDto ToDetailDto(Ticket ticket) => new(
        ticket.Id,
        ticket.Number,
        new TicketPersonDto(ticket.Reporter.Username, ticket.Reporter.DisplayName),
        ticket.Type.ToString(),
        ticket.Message,
        ticket.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList(),
        ticket.Status.ToString(),
        ticket.CreatedAt,
        ticket.UpdatedAt,
        ticket.EditedAt,
        ticket.Updates
            .OrderBy(u => u.CreatedAt)
            .Select(u => new TicketUpdateDto(
                u.Id,
                new TicketPersonDto(u.Admin.Username, u.Admin.DisplayName),
                u.Status.ToString(),
                u.StatusChanged,
                u.Note,
                u.CreatedAt))
            .ToList());
}
