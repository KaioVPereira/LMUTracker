using LmuTracker.Application.Auth;
using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

/// <summary>Monta o <see cref="UserDto"/> (login, /auth/me, vínculo Steam) num lugar só, com o piloto vinculado.</summary>
internal static class UserDtoFactory
{
    public static async Task<UserDto> BuildAsync(AppDbContext db, User u, CancellationToken ct)
    {
        var driver = u.SteamId is null ? null : await db.Drivers.AsNoTracking()
            .Where(d => d.SteamId == u.SteamId)
            .Select(d => new UserDriverDto(d.Id, d.DriverRankTitle, d.DriverRankTier, d.DriverRankProgress,
                d.SafetyRankTitle, d.SafetyRankTier, d.SafetyRankProgress))
            .FirstOrDefaultAsync(ct);
        return new UserDto(u.Id, u.Username, u.DisplayName, u.Email, u.SteamId, u.LinkedAt, u.CreatedAt, u.IsSuperAdmin, driver);
    }
}
