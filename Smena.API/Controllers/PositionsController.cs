using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Auth;
using Smena.Application.Positions;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

[ApiController]
[Route("api/positions")]
public sealed class PositionsController : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PositionDto>> List(ListPositionsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(cancellationToken);

    [Authorize(Policy = Policies.Manager)]
    [HttpPost]
    public async Task<ActionResult<PositionDto>> Create(
        CreatePositionCommand command, CreatePositionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
    }

    [Authorize(Policy = Policies.Manager)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PositionDto>> Update(
        Guid id, UpdatePositionCommand command, UpdatePositionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    [Authorize(Policy = Policies.Manager)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, DeletePositionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }
}
