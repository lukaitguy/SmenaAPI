using Microsoft.AspNetCore.Mvc;
using Smena.Application.Shifts;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

/// <summary>Pregled objavljenog rasporeda za zaposlene.</summary>
[ApiController]
[Route("api/schedule")]
public sealed class ScheduleController : ControllerBase
{
    /// <summary>
    /// Objavljen raspored uloga prijavljenog zaposlenog (konobari vide konobare, kuvari kuvare...),
    /// zajedno sa otvorenim smenama te uloge. <c>positionId</c> suzuje na jednu od njegovih uloga.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShiftDto>>> Get(
        DateTimeOffset from, DateTimeOffset to, Guid? positionId, ListShiftsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleScheduleAsync(from, to, positionId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
