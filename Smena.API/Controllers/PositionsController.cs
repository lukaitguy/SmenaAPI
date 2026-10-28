using Microsoft.AspNetCore.Mvc;
using Smena.Application.Auth;
using Smena.Application.Positions;

namespace SmenaAPI.Controllers;

[ApiController]
[Route("api/positions")]
public sealed class PositionsController : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PositionDto>> List(ListPositionsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(cancellationToken);
}
