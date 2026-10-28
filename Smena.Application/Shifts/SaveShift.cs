using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Claims;
using Smena.Application.Common;
using Smena.Domain.Shifts;

namespace Smena.Application.Shifts;

/// <param name="EmployeeId">Zaposleni kome se dodeljuje smena; izostavljen znači otvorena smena.</param>
public sealed record SaveShiftCommand(
    Guid? EmployeeId,
    Guid PositionId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Note);

public sealed class SaveShiftValidator : AbstractValidator<SaveShiftCommand>
{
    public SaveShiftValidator()
    {
        RuleFor(x => x.EmployeeId).NotEqual(Guid.Empty);
        RuleFor(x => x.PositionId).NotEmpty();
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt)
            .WithMessage("Kraj smene mora biti posle početka.");
        RuleFor(x => x).Must(x => x.EndsAt - x.StartsAt <= Shift.MaxDuration)
            .WithName(nameof(SaveShiftCommand.EndsAt))
            .WithMessage($"Smena ne može trajati duže od {Shift.MaxDuration.TotalHours} sati.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

/// <summary>Zajednička logika za pravljenje i izmenu smene.</summary>
public sealed class SaveShiftHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<SaveShiftCommand> validator,
    ClaimNotifier claimNotifier,
    TimeProvider time)
{
    public Task<Result<ShiftDto>> CreateAsync(SaveShiftCommand command, CancellationToken cancellationToken) =>
        SaveAsync(null, command, cancellationToken);

    public Task<Result<ShiftDto>> UpdateAsync(Guid shiftId, SaveShiftCommand command, CancellationToken cancellationToken) =>
        SaveAsync(shiftId, command, cancellationToken);

    private async Task<Result<ShiftDto>> SaveAsync(Guid? shiftId, SaveShiftCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var shift = shiftId is null
            ? null
            : await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken);
        if (shiftId is not null && shift is null)
            return Error.NotFound("shifts.not_found", "Smena nije pronađena.");

        var startsAt = command.StartsAt.ToUniversalTime();
        var endsAt = command.EndsAt.ToUniversalTime();

        if (command.EmployeeId is { } employeeId)
        {
            if (await AssignmentRules.CheckAsync(db, employeeId, command.PositionId, startsAt, endsAt, shiftId, cancellationToken) is { } error)
                return error;
        }
        else if (!await db.Positions.AnyAsync(p => p.Id == command.PositionId, cancellationToken))
        {
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");
        }

        IReadOnlyCollection<ShiftClaim> resolvedClaims = [];
        if (shift is null)
        {
            shift = new Shift(currentUser.RestaurantId!.Value, command.EmployeeId, command.PositionId, startsAt, endsAt, command.Note);
            db.Shifts.Add(shift);
        }
        else
        {
            var wasOpen = shift.IsOpen;
            shift.Reschedule(command.EmployeeId, command.PositionId, startsAt, endsAt, command.Note);

            // Menadžer je otvorenu smenu dodelio direktno: zahtevi za nju se zatvaraju.
            if (wasOpen && command.EmployeeId is not null)
                resolvedClaims = await ClaimResolution.ResolveAsync(
                    db, shift, command.EmployeeId.Value, currentUser.EmployeeId!.Value, time.GetUtcNow(), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await claimNotifier.DecidedAsync(resolvedClaims, cancellationToken);

        return await db.ProjectToDto(db.Shifts.Where(s => s.Id == shift.Id)).FirstAsync(cancellationToken);
    }
}
