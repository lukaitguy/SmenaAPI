using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Invitations;

public sealed class RevokeInvitationHandler(IAppDbContext db, TimeProvider time)
{
    public async Task<Result> HandleAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null)
            return Error.NotFound("invitations.not_found", "Poziv nije pronađen.");

        if (invitation.AcceptedAt is not null)
            return Error.Conflict("invitations.already_accepted", "Poziv je već prihvaćen.");

        invitation.Revoke(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
