using LmuTracker.Application.Lmu;

namespace LmuTracker.Application.Tickets;

/// <summary>
/// O que o usuário está mandando: um bug ou uma sugestão de melhoria, com prints opcionais.
/// <paramref name="Images"/> são data URLs (ex.: "data:image/png;base64,....") — de anexo ou colados.
/// </summary>
public record SubmitTicketRequest(string Type, string Message, IReadOnlyList<string>? Images = null);

public record TicketSubmittedDto(int Number);

/// <summary>
/// Edição do chamado pelo próprio autor. <paramref name="KeepImages"/> são as URLs dos anexos atuais que continuam
/// (o que ficar de fora é removido); <paramref name="NewImages"/> são data URLs de anexos novos.
/// </summary>
public record EditTicketRequest(
    string Type,
    string Message,
    IReadOnlyList<string>? KeepImages = null,
    IReadOnlyList<string>? NewImages = null);

public record TicketPersonDto(string Username, string DisplayName);

public record TicketUpdateDto(
    Guid Id,
    TicketPersonDto Admin,
    string Status,
    /// <summary>Se esta ação mudou o status (falso quando foi só uma resposta).</summary>
    bool StatusChanged,
    string? Note,
    DateTime CreatedAt);

public record TicketListItemDto(
    Guid Id,
    int Number,
    TicketPersonDto Reporter,
    string Type,
    string Message,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? EditedAt);

public record TicketDetailDto(
    Guid Id,
    int Number,
    TicketPersonDto Reporter,
    string Type,
    string Message,
    IReadOnlyList<string> Images,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? EditedAt,
    IReadOnlyList<TicketUpdateDto> Updates);

/// <summary>
/// <paramref name="Status"/> ausente = só uma resposta, sem trocar o status atual — precisa vir pelo menos um de
/// <paramref name="Status"/>/<paramref name="Note"/>.
/// </summary>
public record AddTicketUpdateRequest(string? Status, string? Note);

public interface ITicketService
{
    /// <summary>Persiste o chamado e devolve o número atribuído.</summary>
    Task<int> SubmitAsync(Guid userId, SubmitTicketRequest request, CancellationToken ct = default);

    Task<PagedDto<TicketListItemDto>> ListForAdminAsync(
        string? status, string? type, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<TicketDetailDto> GetForAdminAsync(Guid ticketId, CancellationToken ct = default);

    /// <summary>"Meus chamados": só os do próprio usuário.</summary>
    Task<PagedDto<TicketListItemDto>> ListMineAsync(Guid userId, int page = 1, int pageSize = 20, CancellationToken ct = default);

    /// <summary>Detalhe de um chamado, restrito a quem o abriu (404 se não for o dono).</summary>
    Task<TicketDetailDto> GetMineAsync(Guid userId, Guid ticketId, CancellationToken ct = default);

    /// <summary>
    /// O autor corrige ou complementa o próprio chamado (tipo, mensagem e anexos). Só enquanto ele não foi
    /// encerrado — concluído/cancelado pede um chamado novo.
    /// </summary>
    Task<TicketDetailDto> EditMineAsync(Guid userId, Guid ticketId, EditTicketRequest request, CancellationToken ct = default);

    /// <summary>Registra uma resposta e/ou mudança de status (o autor vê em "Meus chamados").</summary>
    Task<TicketDetailDto> AddUpdateAsync(Guid adminId, Guid ticketId, AddTicketUpdateRequest request, CancellationToken ct = default);
}
