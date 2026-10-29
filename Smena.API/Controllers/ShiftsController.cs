using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Claims;
using Smena.Application.Shifts;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

[ApiController]
[Route("api/shifts")]
public sealed class ShiftsController : ControllerBase
{
    /// <summary>
    /// Raspored celog restorana za period (from/to, najviše 62 dana), uključujući nacrte.
    /// Može da se suzi na zaposlenog (<c>employeeId</c>) ili na ulogu (<c>positionId</c>).
    /// </summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ShiftDto>>> List(
        DateTimeOffset from, DateTimeOffset to, Guid? employeeId, Guid? positionId, ListShiftsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(from, to, employeeId, positionId, cancellationToken);
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

    /// <summary>Otvorene smene (bez zaposlenog) koje prijavljeni zaposleni može da traži, po svojim pozicijama.</summary>
    [HttpGet("open")]
    public async Task<ActionResult<IReadOnlyList<ShiftDto>>> Open(
        DateTimeOffset from, DateTimeOffset to, ListShiftsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleOpenAsync(from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    /// <summary>Zahtev da prijavljeni zaposleni preuzme otvorenu smenu; odlučuje menadžer.</summary>
    [HttpPost("{id:guid}/claims")]
    public async Task<ActionResult<ClaimDto>> Claim(
        Guid id, CreateClaimCommand command, CreateClaimHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
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

    /// <summary>
    /// Pravi smene po obrascu (dani u nedelji, od-do sat, period) u jednom koraku.
    /// Sat se računa u zoni restorana. Sve smene serije dobijaju isti <c>seriesId</c>.
    /// </summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpPost("recurring")]
    public async Task<ActionResult<RecurringShiftsResultDto>> CreateRecurring(
        CreateRecurringShiftsCommand command, CreateRecurringShiftsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>
    /// Objavljuje nacrte koji počinju u periodu [from, to) (npr. jedna nedelja), za zadate pozicije
    /// (<c>positionIds</c>, npr. samo konobare) ili za sve. Zaposleni tih uloga dobijaju mejl da pogledaju
    /// raspored. Do objavljivanja smene vidi samo menadžer.
    /// </summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpPost("publish")]
    public async Task<ActionResult<PublishedShiftsDto>> Publish(
        PublishShiftsCommand command, PublishShiftsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    /// <summary>
    /// Vraća objavljene smene koje još nisu počele u nacrte (period [from, to), opciono samo za <c>positionIds</c>).
    /// Zaposleni ih više ne vide i ne dobijaju obaveštenje; otvoreni zahtevi ostaju.
    /// </summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpPost("unpublish")]
    public async Task<ActionResult<UnpublishedShiftsDto>> Unpublish(
        UnpublishShiftsCommand command, UnpublishShiftsHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    /// <summary>Briše buduće smene serije (od <c>from</c>, podrazumevano od sada); prošle ostaju.</summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpDelete("series/{seriesId:guid}")]
    public async Task<ActionResult<DeletedShiftsDto>> DeleteSeries(
        Guid seriesId, DateTimeOffset? from, DeleteShiftSeriesHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(seriesId, from, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
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
