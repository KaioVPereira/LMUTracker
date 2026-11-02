using Microsoft.AspNetCore.HttpOverrides;

// SITE ENCERRADO (2026-11-02): a RaceControl (RaceOS) bloqueou o acesso do servidor aos dados, o scanner parou e o
// banco saiu de produção (backup local). A Api agora só serve a página de encerramento (o front) e o aviso de versão
// nova do app Android, para quem tem o APK antigo baixar o que mostra a mesma mensagem. Sem banco, login ou scanner:
// todo o resto de /api responde 410. A Api completa está no histórico do git (commit 8a357e7).

var builder = WebApplication.CreateBuilder(args);

// O APK antigo chama a Api pelo endereço completo, de https://localhost (WebView do Capacitor).
builder.Services.AddCors(o => o.AddPolicy("app", p =>
    p.WithOrigins("https://localhost", "capacitor://localhost", "http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
app.UseForwardedHeaders();
app.UseCors("app");

// ---------- App Android ----------
// A última versão vem do appsettings (Closed:Android), não mais da tabela AppReleases. O APK fica na pasta do servidor.
const string downloadPath = "/api/app/android/download";
var release = app.Configuration.GetSection("Closed:Android");
FileInfo? ApkFile()
{
    var dir = app.Configuration["AppReleases:Directory"] is { Length: > 0 } d ? d : Path.Combine(app.Environment.ContentRootPath, "App_Data", "apk");
    var file = new FileInfo(Path.Combine(dir, Path.GetFileName(release["FileName"] ?? "")));
    return file.Exists ? file : null;
}

app.MapGet("/api/app/android", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "no-cache";
    var file = ApkFile();
    return file is null
        ? Results.Ok(new { available = false, version = (string?)null, versionCode = (int?)null, notes = (string?)null, publishedAt = (string?)null, sizeBytes = (long?)null, downloadUrl = downloadPath })
        : Results.Ok(new
        {
            available = true,
            version = release["Version"],
            versionCode = release.GetValue<int>("VersionCode"),
            notes = release["Notes"],
            publishedAt = release["PublishedAt"],
            sizeBytes = file.Length,
            downloadUrl = downloadPath,
        });
});

app.MapGet(downloadPath, () => ApkFile() is { } file
    ? Results.File(file.FullName, "application/vnd.android.package-archive", $"pitwall-{release["Version"]}.apk")
    : Results.NotFound());

// Todo o resto da Api acabou.
const string closedMessage = "O Pitwall foi encerrado: a RaceControl bloqueou nosso acesso aos dados das corridas.";
app.Map("/api/{**rest}", () => Results.Json(new { message = closedMessage }, statusCode: StatusCodes.Status410Gone));
app.Map("/og/{**rest}", () => Results.StatusCode(StatusCodes.Status410Gone));

// ---------- Página de encerramento ----------
// Na imagem de produção o build do front (só a página de encerramento) fica em wwwroot; em dev o Vite serve o front.
var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
if (File.Exists(spaIndex))
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl =
            ctx.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
    });
    // Qualquer endereço antigo (perfil, corrida...) cai na mesma página.
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
    });
}

app.Run();
