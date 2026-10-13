using LmuTracker.Application.Auth;
using LmuTracker.Application.Linking;
using LmuTracker.Application.Lmu;
using LmuTracker.Infrastructure.Linking;
using LmuTracker.Infrastructure.Auth;
using LmuTracker.Infrastructure.Persistence;
using LmuTracker.Infrastructure.RaceOs;
using LmuTracker.Infrastructure.Scanning;
using LmuTracker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LmuTracker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlServer(config.GetConnectionString("Default")));

        // ---- Autenticação (site) ----
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ILmuQueryService, LmuQueryService>();
        services.AddMemoryCache();
        services.AddScoped<ISearchService, SearchService>();

        // ---- Vínculo Steam (OpenID) ----
        services.Configure<SiteOptions>(config.GetSection(SiteOptions.Section));
        services.AddHttpClient("steam-openid");
        services.AddScoped<ISteamLinkService, SteamOpenIdService>();

        services.Configure<RaceOsOptions>(config.GetSection(RaceOsOptions.Section));
        services.Configure<SteamKitOptions>(config.GetSection(SteamKitOptions.Section));

        services.AddHttpClient("raceos");
        services.AddHttpClient("raceos-local");

        // Fonte do ticket Steam: "localgame" (jogo aberto, dev) ou "steamkit" (headless, servidor).
        var provider = config[$"{RaceOsOptions.Section}:TicketProvider"] ?? "localgame";
        if (string.Equals(provider, "steamkit", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ISteamTicketProvider, SteamKitTicketProvider>();
        else
            services.AddSingleton<ISteamTicketProvider, LocalGameTicketProvider>();

        services.AddSingleton<SteamSessionStore>();
        services.AddSingleton<SteamBootstrap>();

        // Token em cache no processo => singleton.
        services.AddSingleton<RaceOsClient>();

        // Usa o DbContext (scoped) => scoped; o Worker abre um escopo por passada.
        services.AddScoped<ScannerService>();

        return services;
    }
}
