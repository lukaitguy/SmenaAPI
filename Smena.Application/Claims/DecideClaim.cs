using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Shifts;

namespace Smena.Application.Claims;

/// <summary>Odluka menadžera. Odobravanje jednog zahteva jedino je mesto na kojem otvorena smena dobija zaposlenog.</summary>
public sealed class DecideClaimHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ClaimNotifier notifier,
    TimeProvider time)
{
    public async Task<Result<ClaimDto>> ApproveAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await db.ShiftClaims.FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);
        if (claim is null)
            return ClaimResolution.NotFound();
        if (!claim.IsPending)
            return ClaimResolution.InvalidState(claim.Status);

        var now = time.GetUtcNow();
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == claim.ShiftId, cancellationToken);
        if (shift is null || shift.StartsAt <= now)
            return ClaimResolution.ShiftStarted();
        if (!shift.IsOpen)
            return ClaimResolution.ShiftTaken();

        // Zaposleni je od podnošenja zahteva mogao da postane zauzet ili neaktivan.
        if (await AssignmentRules.CheckAsync(db, claim.EmployeeId, shift.PositionId, shift.StartsAt, shift.EndsAt, shift.Id, cancellationToken) is { } error)
            return error;

        shift.Reschedule(claim.EmployeeId, shift.PositionId, shift.StartsAt, shift.EndsAt, shift.Note);
        var resolved = await ClaimResolution.ResolveAsync(db, shift, claim.EmployeeId, currentUser.EmployeeId!.Value, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await notifier.DecidedAsync(resolved, cancellationToken);
        return await db.LoadDtoAsync(claim.Id, cancellationToken);
    }

    public async Task<Result<ClaimDto>> RejectAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await db.ShiftClaims.FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);
        if (claim is null)
            return ClaimResolution.NotFound();
        if (!claim.IsPending)
            return ClaimResolution.InvalidState(claim.Status);

        claim.Reject(currentUser.EmployeeId!.Value, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        await notifier.DecidedAsync([claim], cancellationToken);
        return await db.LoadDtoAsync(claim.Id, cancellationToken);
    }
}
