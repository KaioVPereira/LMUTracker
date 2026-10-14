using LmuTracker.Domain.Common;
using LmuTracker.Domain.Enums;

namespace LmuTracker.Domain.Entities;

/// <summary>Chamado aberto por um usuário (bug ou sugestão), acompanhado pelo super admin.</summary>
public class Ticket : Entity
{
    /// <summary>Número sequencial e amigável (ex.: "#42") — coluna IDENTITY, atribuída pelo banco.</summary>
    public int Number { get; set; }

    public Guid ReporterId { get; set; }
    public User Reporter { get; set; } = null!;

    public TicketType Type { get; set; }
    public string Message { get; set; } = null!;
    public TicketStatus Status { get; set; } = TicketStatus.Open;

    /// <summary>Última vez que o autor editou a mensagem/anexos (nulo = nunca editado).</summary>
    public DateTime? EditedAt { get; set; }

    public List<TicketImage> Images { get; set; } = new();
    public List<TicketUpdate> Updates { get; set; } = new();
}
