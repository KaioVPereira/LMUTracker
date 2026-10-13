using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace LmuTracker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Id do usuário logado, tirado do claim 'sub' do JWT.</summary>
    protected Guid UserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User.FindFirstValue("sub"), out var id)
            ? id
            : Guid.Empty;
}
