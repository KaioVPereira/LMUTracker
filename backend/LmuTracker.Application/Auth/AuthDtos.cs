namespace LmuTracker.Application.Auth;

public record RegisterRequest(string Username, string Email, string Password, string? DisplayName = null);

public record LoginRequest(string UsernameOrEmail, string Password);

/// <summary>Dados do usuário logado. <see cref="SteamId"/> null = conta ainda não vinculada ao LMU.</summary>
public record UserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string Email,
    string? SteamId,
    DateTime? LinkedAt,
    DateTime CreatedAt);

public record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, UserDto User);
