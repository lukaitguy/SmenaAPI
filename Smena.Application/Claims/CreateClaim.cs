using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Notifications;
using Smena.Application.Shifts;
using Smena.Domain.Shifts;

namespace Smena.Application.Claims;

public sealed record CreateClaimCommand(string? Note);

public sealed class CreateClaimValidator : AbstractValidator<CreateClaimCommand>
{
    public CreateClaimValidator()
    {
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class CreateClaimHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<CreateClaimCommand> validator,
    ManagerNotifier managerNotifier,
    TimeProvider time)
{
    public async Task<Result<ClaimDto>> HandleAsync(Guid shiftId, CreateClaimCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var me = currentUser.EmployeeId!.Value;
        var now = time.GetUtcNow();

        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken);
        // Nacrt zaposleni ne vidi, pa za njega smena i ne postoji.
        if (shift is null || !shift.IsPublished)
            return Error.NotFound("shifts.not_found", "Smena nije pronađena.");
        if (!shift.IsOpen)
            return Error.Conflict("claims.not_open", "Smena nije otvorena, već je dodeljena.");
        if (shift.StartsAt <= now)
            return ClaimResolution.ShiftStarted();

        if (await db.ShiftClaims.AnyAsync(c => c.ShiftId == shiftId && c.EmployeeId == me && c.Status == ClaimStatus.Pending, cancellationToken))
            return Error.Conflict("claims.already_requested", "Već ste tražili ovu smenu.");

        if (await AssignmentRules.CheckAsync(db, me, shift.PositionId, shift.StartsAt, shift.EndsAt, shift.Id, cancellationToken) is { } error)
            return error;

        var claim = new ShiftClaim(shift.RestaurantId, shift.Id, me, command.Note, now);
        db.ShiftClaims.Add(claim);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dva istovremena zahteva istog zaposlenog: jedinstveni indeks u bazi pušta samo jedan.
            return Error.Conflict("claims.already_requested", "Već ste tražili ovu smenu.");
        }

        await managerNotifier.ClaimRequestedAsync(claim, cancellationToken);
        return await db.LoadDtoAsync(claim.Id, cancellationToken);
    }
}
