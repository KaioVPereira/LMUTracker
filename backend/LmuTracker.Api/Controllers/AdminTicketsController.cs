using LmuTracker.Application.Lmu;
using LmuTracker.Application.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LmuTracker.Api.Controllers;

/// <summary>Painel do super admin — todos os chamados (bugs/sugestões) abertos pelos usuários.</summary>
[Authorize(Policy = "SuperAdmin")]
[Route("api/admin/tickets")]
public class AdminTicketsController : ApiControllerBase
{
    private readonly ITicketService _tickets;

    public AdminTicketsController(ITicketService tickets)
    {
        _tickets = tickets;
    }

    [HttpGet]
    public async Task<ActionResult<PagedDto<TicketListItemDto>>> List(
        [FromQuery] string? status, [FromQuery] string? type,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _tickets.ListForAdminAsync(status, type, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _tickets.GetForAdminAsync(id, ct));

    /// <summary>Registra resposta e/ou mudança de status.</summary>
    [HttpPost("{id:guid}/updates")]
    public async Task<ActionResult<TicketDetailDto>> AddUpdate(Guid id, AddTicketUpdateRequest request, CancellationToken ct)
        => Ok(await _tickets.AddUpdateAsync(UserId, id, request, ct));
}
