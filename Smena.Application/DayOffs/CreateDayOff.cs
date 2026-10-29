using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Notifications;
using Smena.Domain.DayOffs;

namespace Smena.Application.DayOffs;

public sealed record CreateDayOffCommand(DateOnly Date, string? Reason);

public sealed class CreateDayOffValidator : AbstractValidator<CreateDayOffCommand>
{
    public CreateDayOffValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly)).WithMessage("Datum je obavezan.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class CreateDayOffHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<CreateDayOffCommand> validator,
    ManagerNotifier managerNotifier,
    TimeProvider time)
{
    public async Task<Result<DayOffDto>> HandleAsync(CreateDayOffCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var me = currentUser.EmployeeId!.Value;
        var now = time.GetUtcNow();
        var zone = await DayOffRules.RestaurantZoneAsync(db, cancellationToken);
        var today = DayOffRules.LocalDate(zone, now);

        if (command.Date < today)
            return Error.Validation("dayoffs.date_in_past", "Slobodan dan ne može biti u prošlosti.");
        if (command.Date > today.AddDays(DayOffRules.MaxDaysAhead))
            return Error.Validation("dayoffs.date_too_far", $"Slobodan dan se traži najviše {DayOffRules.MaxDaysAhead} dana unapred.");

        if (await db.DayOffRequests.AnyAsync(d => d.EmployeeId == me && d.Date == command.Date &&
                (d.Status == DayOffStatus.Pending || d.Status == DayOffStatus.Approved), cancellationToken))
            return Error.Conflict("dayoffs.already_requested", "Već ste tražili slobodan dan za taj datum.");

        var request = new DayOffRequest(currentUser.RestaurantId!.Value, me, command.Date, command.Reason, now);
        db.DayOffRequests.Add(request);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dva istovremena zahteva za isti dan: jedinstveni indeks u bazi pušta samo jedan.
            return Error.Conflict("dayoffs.already_requested", "Već ste tražili slobodan dan za taj datum.");
        }

        await managerNotifier.DayOffRequestedAsync(request, cancellationToken);
        return await db.LoadDtoAsync(request.Id, cancellationToken);
    }
}
