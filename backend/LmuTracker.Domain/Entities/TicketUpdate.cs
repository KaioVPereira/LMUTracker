using LmuTracker.Domain.Common;
using LmuTracker.Domain.Enums;

namespace LmuTracker.Domain.Entities;

/// <summary>
/// Uma ação do super admin sobre um chamado: mudança de status e/ou resposta ao usuário. <see cref="Status"/>
/// é sempre o status vigente do chamado após esta ação, mesmo quando ela não o alterou (só resposta).
/// </summary>
public class TicketUpdate : Entity
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    public Guid AdminId { get; set; }
    public User Admin { get; set; } = null!;

    public TicketStatus Status { get; set; }

    /// <summary>Se esta ação mudou o status (falso quando foi só uma resposta, sem trocar status).</summary>
    public bool StatusChanged { get; set; }

    /// <summary>Resposta ao usuário, opcional (ele vê em "Meus chamados").</summary>
    public string? Note { get; set; }
}
