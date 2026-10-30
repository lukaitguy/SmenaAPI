using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Templates;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

/// <summary>Unapred definisane smene po pozicijama (npr. konobari: I 08:00-16:00, II 16:00-00:00).</summary>
[ApiController]
[Route("api/shift-templates")]
public sealed class ShiftTemplatesController : ControllerBase
{
    /// <summary>Čitaju svi zaposleni, jer im trebaju za prikaz oznake smene.</summary>
    [HttpGet]
    public Task<IReadOnlyList<ShiftTemplateDto>> List(ListShiftTemplatesHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(cancellationToken);

    [Authorize(Policy = Policies.Manager)]
    [HttpPost]
    public async Task<ActionResult<ShiftTemplateDto>> Create(
        CreateShiftTemplateCommand command, CreateShiftTemplateHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
    }

    [Authorize(Policy = Policies.Manager)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ShiftTemplateDto>> Update(
        Guid id, UpdateShiftTemplateCommand command, UpdateShiftTemplateHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, command, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    [Authorize(Policy = Policies.Manager)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, DeleteShiftTemplateHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }
}
