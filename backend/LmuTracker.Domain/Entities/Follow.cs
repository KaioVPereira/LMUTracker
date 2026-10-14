using LmuTracker.Domain.Common;

namespace LmuTracker.Domain.Entities;

/// <summary>Um usuário do site seguindo um piloto (aparece no início com as corridas mais recentes dele).</summary>
public class Follow : Entity
{
    public Guid UserId { get; set; }
    public Guid DriverId { get; set; }
}
