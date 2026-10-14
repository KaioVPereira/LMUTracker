using System.Text.Json;
using LmuTracker.Application.Common;

namespace LmuTracker.Api.Middleware;

/// <summary>Converte <see cref="AppException"/> em resposta JSON limpa; o resto vira 500 genérico.</summary>
public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _log;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (AppException ex)
        {
            ctx.Response.StatusCode = ex.StatusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { message = ex.Message }));
        }
        catch (Exception) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // O cliente desistiu (fechou a aba, trocou de página): não é erro do servidor e não há a quem responder.
            ctx.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Erro não tratado.");
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { message = "Erro interno." }));
        }
    }
}
