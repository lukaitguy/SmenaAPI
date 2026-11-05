using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Wishes;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

/// <summary>
/// Želje radnika za dan ("I smena ili slobodan"). Važe odmah, ne čekaju odobrenje i ništa ne blokiraju;
/// menadžer ih vidi pri pravljenju rasporeda.
/// </summary>
[ApiController]
[Route("api/wishes")]
public sealed class WishesController : ControllerBase
{
    /// <summary>Javlja ili menja želju za dan; za isti dan postoji jedna želja po radniku.</summary>
    [HttpPut]
    public async Task<ActionResult<WishDto>> Save(
        SaveWishCommand command, SaveWishHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    [HttpGet("mine")]
    public Task<IReadOnlyList<WishDto>> Mine(
        DateOnly? from, DateOnly? to, ListWishesHandler handler, CancellationToken cancellationToken) =>
        handler.HandleMineAsync(from, to, cancellationToken);

    [Authorize(Policy = Policies.Manager)]
    [HttpGet]
    public Task<IReadOnlyList<WishDto>> List(
        DateOnly? from, DateOnly? to, ListWishesHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(from, to, cancellationToken);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, DeleteWishHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }
}
