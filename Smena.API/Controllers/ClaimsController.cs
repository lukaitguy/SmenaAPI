using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Claims;
using Smena.Application.Common;
using Smena.Domain.Shifts;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

/// <summary>Zahtevi zaposlenih za preuzimanje otvorenih smena (podnosi se preko <c>POST /api/shifts/{id}/claims</c>).</summary>
[ApiController]
[Route("api/claims")]
public sealed class ClaimsController : ControllerBase
{
    [HttpGet("mine")]
    public Task<IReadOnlyList<ClaimDto>> Mine(ClaimStatus? status, ListClaimsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleMineAsync(status, cancellationToken);

    [Authorize(Policy = Policies.Manager)]
    [HttpGet]
    public Task<IReadOnlyList<ClaimDto>> List(ClaimStatus? status, ListClaimsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(status, cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<ClaimDto>> Cancel(Guid id, CancelClaimHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.HandleAsync(id, cancellationToken));

    [Authorize(Policy = Policies.Manager)]
    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<ClaimDto>> Approve(Guid id, DecideClaimHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.ApproveAsync(id, cancellationToken));

    [Authorize(Policy = Policies.Manager)]
    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<ClaimDto>> Reject(Guid id, DecideClaimHandler handler, CancellationToken cancellationToken) =>
        Respond(handler.RejectAsync(id, cancellationToken));

    private async Task<ActionResult<ClaimDto>> Respond(Task<Result<ClaimDto>> action)
    {
        var result = await action;
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }
}
