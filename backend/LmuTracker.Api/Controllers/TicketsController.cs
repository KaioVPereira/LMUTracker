using LmuTracker.Application.Lmu;
using LmuTracker.Application.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LmuTracker.Api.Controllers;

/// <summary>Canal de bug/sugestão do usuário — vira um chamado acompanhado pelo super admin.</summary>
[Authorize]
[Route("api/tickets")]
public class TicketsController : ApiControllerBase
{
    private readonly ITicketService _tickets;

    public TicketsController(ITicketService tickets)
    {
        _tickets = tickets;
    }

    // Prints vão como data URLs base64 no corpo — o limite padrão (~28 MB) fica curto com 4 imagens de até 5 MB
    // (base64 infla ~33%). 40 MB cobre com folga.
    [HttpPost]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<TicketSubmittedDto>> Submit(SubmitTicketRequest request, CancellationToken ct)
        => Ok(new TicketSubmittedDto(await _tickets.SubmitAsync(UserId, request, ct)));

    /// <summary>"Meus chamados": lista só os do usuário autenticado.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedDto<TicketListItemDto>>> ListMine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _tickets.ListMineAsync(UserId, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> GetMine(Guid id, CancellationToken ct)
        => Ok(await _tickets.GetMineAsync(UserId, id, ct));

    /// <summary>O autor corrige ou complementa o próprio chamado (mesmo limite de tamanho do envio).</summary>
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<TicketDetailDto>> EditMine(Guid id, EditTicketRequest request, CancellationToken ct)
        => Ok(await _tickets.EditMineAsync(UserId, id, request, ct));
}
