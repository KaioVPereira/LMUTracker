namespace LmuTracker.Domain.Common;

/// <summary>Base de toda entidade persistida: id próprio (Guid) e marcas de tempo em UTC.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
