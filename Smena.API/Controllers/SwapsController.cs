using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Common;
using Smena.Application.Swaps;
using Smena.Domain.Shifts;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

/// <summary>Prepuštanje smene kolegi: zahtev, odgovor kolege, odluka menadžera.</summary>
[ApiController]
[Route("api/swaps")]
public sealed class SwapsController : ControllerBase
{
    /// <summary>Zahtevi u kojima prijavljeni zaposleni učestvuje (kao podnosilac ili kolega).</summary>
    [HttpGet("mine")]
    public Task<IReadOnlyList<SwapDto>> Mine(SwapStatus? status, ListSwapsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleMineAsync(status, cancellationToken);

    /// <summary>Svi zahtevi u restoranu.</summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpGet]
    public Task<IReadOnlyList<SwapDto>> List(SwapStatus? status, ListSwapsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(status, cancellationToken);

    [HttpPost]
    public async Task<ActionResult<SwapDto>> Create(
        CreateSwapCommand command, CreateSwapHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpPost("{id:guid}/accept")]
    public Task<ActionResult<SwapDto>> Accept(Guid id, RespondToSwapHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.AcceptAsync(id, cancellationToken));

    [HttpPost("{id:guid}/decline")]
    public Task<ActionResult<SwapDto>> Decline(Guid id, RespondToSwapHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.DeclineAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<SwapDto>> Cancel(Guid id, RespondToSwapHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.CancelAsync(id, cancellationToken));

    [Authorize(Policy = Policies.Manager)]
    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<SwapDto>> Approve(Guid id, DecideSwapHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.ApproveAsync(id, cancellationToken));

    [Authorize(Policy = Policies.Manager)]
    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<SwapDto>> Reject(Guid id, DecideSwapHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.RejectAsync(id, cancellationToken));

    private async Task<ActionResult<SwapDto>> Respond(Task<Result<SwapDto>> action)
    {
        var result = await action;
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }
}
