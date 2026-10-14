using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>Print anexado a um chamado (arquivo no volume de uploads; aqui fica o caminho público).</summary>
public class TicketImage : Entity
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;

    public string Url { get; set; } = null!;
    public int Order { get; set; }
}
