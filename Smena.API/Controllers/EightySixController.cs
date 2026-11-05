using Microsoft.AspNetCore.Mvc;
using Smena.Application.EightySix;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

/// <summary>
/// 86 lista: stavke koje nema na stanju. Vide je svi zaposleni, a menjaju kuvari i šankeri (svoj odeljak) i menadžer.
/// </summary>
[ApiController]
[Route("api/eighty-six")]
public sealed class EightySixController : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<EightySixItemDto>> List(ListEightySixHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<EightySixItemDto>> Add(
        AddEightySixItemCommand command, AddEightySixItemHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>Stavka je opet na stanju: skida se sa liste.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, RemoveEightySixItemHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }
}
