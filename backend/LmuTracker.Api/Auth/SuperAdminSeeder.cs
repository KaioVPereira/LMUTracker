using LmuTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LmuTracker.Api.Auth;

/// <summary>
/// Garante que os usuários listados em <c>Admin:SuperAdminUsernames</c> (nome de usuário ou e-mail) sejam super
/// administradores. Idempotente e nunca <em>rebaixa</em> ninguém — o super admin é o dono da plataforma, então o
/// papel vem da configuração do servidor, não de um painel.
/// <para>
/// Roda em segundo plano na subida e tenta de novo por alguns minutos: quem migra o banco é o worker, e na
/// primeira subida depois de um deploy a coluna pode ainda não existir quando a Api começa.
/// </para>
/// </summary>
public sealed class SuperAdminSeeder(IServiceScopeFactory scopes, IConfiguration config, ILogger<SuperAdminSeeder> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var names = (config.GetSection("Admin:SuperAdminUsernames").Get<string[]>() ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToList();
        if (names.Count == 0)
            return;

        for (var attempt = 1; attempt <= 20 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // Comparação do SQL Server já ignora maiúsculas/minúsculas (collation padrão).
                var promoted = await db.Users
                    .Where(u => !u.IsSuperAdmin && (names.Contains(u.Username) || names.Contains(u.Email)))
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsSuperAdmin, true).SetProperty(u => u.UpdatedAt, DateTime.UtcNow), stoppingToken);
                if (promoted > 0)
                    log.LogInformation("{Count} usuário(s) promovido(s) a super admin.", promoted);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                if (attempt == 20)
                    log.LogWarning(ex, "Não foi possível conferir os super admins.");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }
}
