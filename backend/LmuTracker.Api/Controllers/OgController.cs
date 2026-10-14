using LmuTracker.Api.Og;
using Microsoft.AspNetCore.Mvc;

namespace LmuTracker.Api.Controllers;

/// <summary>Imagens dos cards Open Graph (públicas; o Discord busca sem login).</summary>
[ApiController]
[Route("og")]
public class OgController : ControllerBase
{
    private readonly OgCards _cards;

    public OgController(OgCards cards)
    {
        _cards = cards;
    }

    [HttpGet("pilotos/{driverId:guid}.png")]
    public async Task<IActionResult> Driver(Guid driverId, CancellationToken ct) => Png(await _cards.DriverAsync(driverId, ct));

    [HttpGet("corridas/{eventId}.png")]
    public async Task<IActionResult> Race(string eventId, CancellationToken ct) => Png(await _cards.RaceAsync(eventId, ct));

    [HttpGet("resultados/{resultId:guid}.png")]
    public async Task<IActionResult> Result(Guid resultId, CancellationToken ct) => Png(await _cards.ResultAsync(resultId, ct));

    [HttpGet("site.png")]
    public async Task<IActionResult> Site() => Png(await _cards.SiteAsync());

    private IActionResult Png(byte[]? bytes)
    {
        if (bytes is null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=600";
        return File(bytes, "image/png");
    }
}
