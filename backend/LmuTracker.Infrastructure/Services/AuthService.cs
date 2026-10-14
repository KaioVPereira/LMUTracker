using LmuTracker.Application.Auth;
using LmuTracker.Application.Common;
using LmuTracker.Domain.Entities;
using LmuTracker.Infrastructure.Auth;
using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokens;

    public AuthService(AppDbContext db, ITokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var username = (request.Username ?? "").Trim();
        var email = (request.Email ?? "").Trim().ToLowerInvariant();

        if (username.Length is < 3 or > 32)
            throw new AppException("O nome de usuário deve ter entre 3 e 32 caracteres.");
        if (!username.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            throw new AppException("O nome de usuário só pode conter letras, números, '_' e '-'.");
        if ((request.Password ?? "").Length < 8)
            throw new AppException("A senha deve ter pelo menos 8 caracteres.");
        if (!email.Contains('@'))
            throw new AppException("E-mail inválido.");

        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            throw new AppException("Este nome de usuário já está em uso.", 409);
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            throw new AppException("Este e-mail já está cadastrado.", 409);

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName!.Trim(),
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return Issue(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var id = (request.UsernameOrEmail ?? "").Trim();
        var idLower = id.ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Username == id || u.Email == idLower, ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new AppException("Usuário ou senha inválidos.", 401);

        return Issue(user);
    }

    public async Task<UserDto?> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : ToDto(user);
    }

    private AuthResponse Issue(User user)
    {
        var (token, expires) = _tokens.CreateAccessToken(user);
        return new AuthResponse(token, expires, ToDto(user));
    }

    private static UserDto ToDto(User u) =>
        new(u.Id, u.Username, u.DisplayName, u.Email, u.SteamId, u.LinkedAt, u.CreatedAt, u.IsSuperAdmin);
}
