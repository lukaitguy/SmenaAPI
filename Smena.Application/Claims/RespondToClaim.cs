using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Claims;

/// <summary>Podnosilac može da odustane dok zahtev čeka odluku.</summary>
public sealed class CancelClaimHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<Result<ClaimDto>> HandleAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await db.ShiftClaims.FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);
        if (claim is null)
            return ClaimResolution.NotFound();
        if (claim.EmployeeId != currentUser.EmployeeId)
            return Error.Forbidden("claims.not_yours", "Zahtev može da otkaže samo onaj ko ga je podneo.");
        if (!claim.IsPending)
            return ClaimResolution.InvalidState(claim.Status);

        claim.Cancel(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await db.LoadDtoAsync(claim.Id, cancellationToken);
    }
}
