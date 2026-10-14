using System.Security.Claims;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Api.Auth;

/// <summary>Exige que quem chama seja super administrador (o dono da plataforma).</summary>
public class SuperAdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Confere no banco se quem chama é super admin. Consulta o banco em vez de confiar num claim do token para
/// conceder — e principalmente revogar — o acesso valer na hora, sem depender de o token ser renovado.
/// </summary>
public class SuperAdminHandler(AppDbContext db) : AuthorizationHandler<SuperAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            return;

        if (await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsSuperAdmin))
            context.Succeed(requirement);
    }
}
