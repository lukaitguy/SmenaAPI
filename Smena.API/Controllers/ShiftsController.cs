using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Shifts;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

[ApiController]
[Route("api/shifts")]
public sealed class ShiftsController : ControllerBase
{
    /// <summary>Raspored celog restorana za period (from/to, najviše 62 dana).</summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShiftDto>>> List(
        DateTimeOffset from, DateTimeOffset to, Guid? employeeId, ListShiftsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(from, to, employeeId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Smene prijavljenog zaposlenog.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<ShiftDto>>> Mine(
        DateTimeOffset from, DateTimeOffset to, ListShiftsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleMineAsync(from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [Authorize(Policy = Policies.Manager)]
    [HttpPost]
    public async Task<ActionResult<ShiftDto>> Create(
        SaveShiftCommand command, SaveShiftHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.CreateAsync(command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
    }

    [Authorize(Policy = Policies.Manager)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ShiftDto>> Update(
        Guid id, SaveShiftCommand command, SaveShiftHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.UpdateAsync(id, command, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    [Authorize(Policy = Policies.Manager)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, DeleteShiftHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }
}
