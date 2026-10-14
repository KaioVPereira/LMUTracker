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
    DateTime CreatedAt,
    bool IsSuperAdmin = false,
    UserDriverDto? Driver = null);

/// <summary>O piloto da conta vinculada, com o último Driver Rank e Safety Rank vistos (para o canto da barra).</summary>
public record UserDriverDto(
    Guid DriverId, string? DriverRankTitle, int? DriverRankTier, int? DriverRankProgress,
    string? SafetyRankTitle, int? SafetyRankTier, int? SafetyRankProgress);

public record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, UserDto User);
