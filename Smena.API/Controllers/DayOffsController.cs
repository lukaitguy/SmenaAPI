using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Common;
using Smena.Application.DayOffs;
using Smena.Domain.DayOffs;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

/// <summary>Zahtevi za slobodan dan: zaposleni traži, menadžer odobrava ili odbija.</summary>
[ApiController]
[Route("api/dayoffs")]
public sealed class DayOffsController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<DayOffDto>> Create(
        CreateDayOffCommand command, CreateDayOffHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpGet("mine")]
    public Task<IReadOnlyList<DayOffDto>> Mine(
        DayOffStatus? status, DateOnly? from, DateOnly? to, ListDayOffsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleMineAsync(status, from, to, cancellationToken);

    [Authorize(Policy = Policies.Manager)]
    [HttpGet]
    public Task<IReadOnlyList<DayOffDto>> List(
        DayOffStatus? status, DateOnly? from, DateOnly? to, ListDayOffsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(status, from, to, cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<DayOffDto>> Cancel(Guid id, CancelDayOffHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.HandleAsync(id, cancellationToken));

    [Authorize(Policy = Policies.Manager)]
    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<DayOffDto>> Approve(Guid id, DecideDayOffHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.ApproveAsync(id, cancellationToken));

    /// <summary>Odbija zahtev na čekanju ili povlači već odobren slobodan dan.</summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<DayOffDto>> Reject(Guid id, DecideDayOffHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.RejectAsync(id, cancellationToken));

    private async Task<ActionResult<DayOffDto>> Respond(Task<Result<DayOffDto>> action)
    {
        var result = await action;
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }
}
