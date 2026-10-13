using System.Text;
using System.Threading.RateLimiting;
using LmuTracker.Api.Middleware;
using LmuTracker.Infrastructure;
using LmuTracker.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();

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
builder.Services.AddAuthorization();

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
builder.Services.AddCors(o => o.AddPolicy("spa", p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

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

app.UseCors("spa");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (serveSpa)
{
    // Qualquer rota que não seja da Api nem um arquivo é rota do React: devolve o index.html.
    app.MapFallback(async ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.SendFileAsync(spaIndex);
    });
}

app.Run();
