using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smena.Application.Invitations;
using SmenaAPI.Infrastructure;

namespace SmenaAPI.Controllers;

public sealed record CreatedInvitationResponse(InvitationDto Invitation, string AcceptUrl, bool EmailSent);

public sealed record InvitationTokenRequest(string Token);

[ApiController]
[Route("api/invitations")]
public sealed class InvitationsController : ControllerBase
{
    /// <summary>
    /// Pravi poziv i šalje ga emailom. Link se vraća i u odgovoru, pa ga menadžer može
    /// poslati sam ako slanje nije uspelo (<c>emailSent: false</c>).
    /// </summary>
    [Authorize(Policy = Policies.Manager)]
    [HttpPost]
    public async Task<ActionResult<CreatedInvitationResponse>> Create(
        CreateInvitationCommand command, CreateInvitationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
            return this.ToProblem(result.Error!);

        var created = result.Value;
        return StatusCode(StatusCodes.Status201Created,
            new CreatedInvitationResponse(created.Invitation, created.AcceptUrl, created.EmailSent));
    }

    [Authorize(Policy = Policies.Manager)]
    [HttpGet]
    public Task<IReadOnlyList<InvitationDto>> List(ListInvitationsHandler handler, CancellationToken cancellationToken) =>
        handler.HandleAsync(cancellationToken);

    [Authorize(Policy = Policies.Manager)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, RevokeInvitationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }

    // Token ide u telo zahteva, ne u URL, da ne završi u logovima servera i proksija.
    [AllowAnonymous]
    [HttpPost("preview")]
    public async Task<ActionResult<InvitationPreviewDto>> Preview(
        InvitationTokenRequest request, GetInvitationPreviewHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.Token, cancellationToken);
        return result.IsSuccess ? result.Value : this.ToProblem(result.Error!);
    }

    [AllowAnonymous]
    [HttpPost("accept")]
    public async Task<ActionResult<AccessTokenResponse>> Accept(
        AcceptInvitationCommand command, AcceptInvitationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
            return this.ToProblem(result.Error!);

        RefreshTokenCookie.Set(Response, result.Value);
        return new AccessTokenResponse(result.Value.AccessToken, result.Value.AccessTokenExpiresAt);
    }
}
