using System.Text;
using System.Threading.RateLimiting;
using LmuTracker.Api.Auth;
using LmuTracker.Api.Middleware;
using LmuTracker.Api.Og;
using LmuTracker.Infrastructure;
using LmuTracker.Infrastructure.Auth;
using LmuTracker.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
// Cache de saída das leituras públicas (só requisições sem Authorization — a política padrão não guarda as logadas).
builder.Services.AddOutputCache();
builder.Services.AddScoped<OgCards>();
builder.Services.AddScoped<SpaMeta>();

// ---------- Chamados (bugs e sugestões) ----------
// Prints ficam fora do build: em produção no volume /data (Tickets:UploadsPath), em dev numa pasta ignorada pelo git.
var uploadsRoot = builder.Configuration["Tickets:UploadsPath"] is { Length: > 0 } configuredUploads
    ? configuredUploads
    : Path.Combine(builder.Environment.ContentRootPath, "uploads");
var ticketImagesDir = Path.Combine(uploadsRoot, "tickets");
Directory.CreateDirectory(ticketImagesDir);
builder.Services.AddSingleton<ITicketImageStorage>(sp =>
    new LocalTicketImageStorage(ticketImagesDir, sp.GetRequiredService<ILogger<LocalTicketImageStorage>>()));
// Fica aqui (e não no AddInfrastructure) porque depende do armazenamento dos prints, que só a Api tem.
builder.Services.AddScoped<LmuTracker.Application.Tickets.ITicketService, LmuTracker.Infrastructure.Services.TicketService>();
builder.Services.AddScoped<IAuthorizationHandler, SuperAdminHandler>();
builder.Services.AddHostedService<SuperAdminSeeder>();
builder.Services.AddHostedService<LmuTracker.Api.Background.HomeWarmer>();

// ---------- JWT ----------
// Em dev o segredo vem do appsettings.Development.json. Em produção não há segredo versionado: ele é
// gerado na primeira subida e guardado em Jwt:SecretFile (volume), sem passo manual.
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
var signingSecret = jwt.Secret ?? "";
if (signingSecret.Length < 32)
{
    var secretFile = builder.Configuration["Jwt:SecretFile"];
    if (string.IsNullOrWhiteSpace(secretFile))
        throw new InvalidOperationException("Configure Jwt:Secret (>= 32 caracteres) ou Jwt:SecretFile.");
    var generated = JwtSecretFile.LoadOrCreate(secretFile);
    signingSecret = generated;
    builder.Services.PostConfigure<JwtOptions>(o => o.Secret = generated);
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy("SuperAdmin", policy => policy.RequireAuthenticatedUser().AddRequirements(new SuperAdminRequirement())));

// ---------- Proxy (Caddy) ----------
// Em produção a Api só é alcançada pelo Caddy, que manda o IP real no X-Forwarded-For. Sem confiar nele,
// todo acesso pareceria vir do contêiner do Caddy e o limite por IP viraria um balde único para todos.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---------- Limite de tentativas (login, cadastro, vínculo) ----------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"message":"Muitas tentativas seguidas. Espere alguns minutos e tente de novo."}""", ct);
    };
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// CORS só importa em dev quando o front roda em outra origem sem o proxy do Vite. Em produção o site e a
// Api estão no mesmo domínio.
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:3000" };
// O app Android (Capacitor) serve o front de https://localhost e chama a Api pelo endereço completo, então essa
// origem vale em todo ambiente (igual ao Finanças).
string[] nativeAppOrigins = ["https://localhost", "capacitor://localhost"];
builder.Services.AddCors(o => o.AddPolicy("spa", p =>
    p.WithOrigins([.. origins, .. nativeAppOrigins]).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseForwardedHeaders(); // cedo: o resto do pipeline já enxerga o IP real
app.UseMiddleware<ExceptionMiddleware>();

// ---------- Site (SPA) ----------
// Na imagem de produção o build do front fica em wwwroot; em dev ele não existe (o Vite serve o front).
var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
var serveSpa = File.Exists(spaIndex);
if (serveSpa)
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        // Arquivos de /assets têm hash no nome: cache longo. O resto (index.html) sempre revalida.
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
            ctx.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
    });
}

// Prints dos chamados. Nome aleatório (não dá para listar nem adivinhar), como no GameTracker.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(ticketImagesDir)),
    RequestPath = LocalTicketImageStorage.PublicPrefix.TrimEnd('/'),
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "private, max-age=86400";
        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
    },
});

app.UseCors("spa");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();
app.MapControllers();

if (serveSpa)
{
    // Qualquer rota que não seja da Api nem um arquivo é rota do React: devolve o index.html, com as meta tags
    // Open Graph daquela página (para o link virar card no Discord/WhatsApp, que não rodam o JavaScript).
    var indexHtml = await File.ReadAllTextAsync(spaIndex);
    app.MapFallback(async (HttpContext ctx, SpaMeta meta) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/og"))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.WriteAsync(await meta.RenderAsync(indexHtml, ctx.Request.Path.Value ?? "/", ctx.RequestAborted));
    });
}

app.Run();
