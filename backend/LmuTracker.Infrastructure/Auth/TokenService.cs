using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LmuTracker.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LmuTracker.Infrastructure.Auth;

public interface ITokenService
{
    (string token, DateTime expiresAt) CreateAccessToken(User user);
}

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _opt;

    public TokenService(IOptions<JwtOptions> opt)
    {
        _opt = opt.Value;
    }

    public (string token, DateTime expiresAt) CreateAccessToken(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_opt.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
