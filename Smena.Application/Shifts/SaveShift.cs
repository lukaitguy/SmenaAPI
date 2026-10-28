using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;
using Smena.Domain.Shifts;

namespace Smena.Application.Shifts;

public sealed record SaveShiftCommand(
    Guid EmployeeId,
    Guid PositionId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Note);

public sealed class SaveShiftValidator : AbstractValidator<SaveShiftCommand>
{
    public SaveShiftValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty();
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
    IValidator<SaveShiftCommand> validator)
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

        var employee = await db.Employees
            .Include(e => e.Positions)
            .FirstOrDefaultAsync(e => e.Id == command.EmployeeId, cancellationToken);
        if (employee is null)
            return Error.NotFound("employees.not_found", "Zaposleni nije pronađen.");
        if (employee.Status != EmployeeStatus.Active)
            return Error.Conflict("shifts.employee_inactive", "Zaposleni nije aktivan.");
        if (employee.Positions.All(p => p.Id != command.PositionId))
            return Error.Conflict("shifts.position_not_assigned", "Zaposleni nema ovu poziciju.");

        var startsAt = command.StartsAt.ToUniversalTime();
        var endsAt = command.EndsAt.ToUniversalTime();
        var overlaps = await db.Shifts.AnyAsync(
            s => s.EmployeeId == command.EmployeeId && s.Id != shiftId && s.StartsAt < endsAt && s.EndsAt > startsAt,
            cancellationToken);
        if (overlaps)
            return Error.Conflict("shifts.overlap", "Zaposleni već ima smenu u tom periodu.");

        if (shift is null)
        {
            shift = new Shift(currentUser.RestaurantId!.Value, command.EmployeeId, command.PositionId, startsAt, endsAt, command.Note);
            db.Shifts.Add(shift);
        }
        else
        {
            shift.Reschedule(command.EmployeeId, command.PositionId, startsAt, endsAt, command.Note);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await db.ProjectToDto(db.Shifts.Where(s => s.Id == shift.Id)).FirstAsync(cancellationToken);
    }
}
