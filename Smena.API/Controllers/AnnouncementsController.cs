using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Announcements;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

/// <summary>
/// Najave (najčešće sastanci): menadžer ih pravi za uloge i/ili pojedince, primaoci dobijaju obaveštenje uživo i
/// mejlom pa potvrđuju "Pročitao sam", a menadžer vidi ko jeste i ko nije.
/// </summary>
[ApiController]
[Route("api/announcements")]
public sealed class AnnouncementsController : ControllerBase
{
    /// <summary>Sve najave restorana sa spiskom primalaca i ko je pročitao.</summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpGet]
    public Task<IReadOnlyList<AnnouncementDto>> List(ListAnnouncementsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(cancellationToken);

    /// <summary>Najave koje je prijavljeni zaposleni primio.</summary>
    [HttpGet("mine")]
    public Task<IReadOnlyList<AnnouncementDto>> Mine(ListAnnouncementsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleMineAsync(cancellationToken);

    [Authorize(Policy = Policies.Manager)]
    [HttpPost]
    public async Task<ActionResult<AnnouncementDto>> Create(
        CreateAnnouncementCommand command, CreateAnnouncementHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
    }

    /// <summary>"Pročitao sam".</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, MarkAnnouncementReadHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }

    [Authorize(Policy = Policies.Manager)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, DeleteAnnouncementHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }
}
