using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Shifts;

namespace Smena.Application.Swaps;

public sealed record CreateSwapCommand(Guid ShiftId, Guid TargetEmployeeId, string? Note);

public sealed class CreateSwapValidator : AbstractValidator<CreateSwapCommand>
{
    public CreateSwapValidator()
    {
        RuleFor(x => x.ShiftId).NotEmpty();
        RuleFor(x => x.TargetEmployeeId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class CreateSwapHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<CreateSwapCommand> validator,
    SwapNotifier notifier,
    TimeProvider time)
{
    public async Task<Result<SwapDto>> HandleAsync(CreateSwapCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var me = currentUser.EmployeeId!.Value;
        var now = time.GetUtcNow();

        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == command.ShiftId, cancellationToken);
        // Nacrt zaposleni ne vidi, pa za njega smena i ne postoji.
        if (shift is null || !shift.IsPublished)
            return Error.NotFound("shifts.not_found", "Smena nije pronađena.");
        if (shift.EmployeeId != me)
            return Error.Forbidden("swaps.not_your_shift", "Možete prepustiti samo svoju smenu.");
        if (shift.StartsAt <= now)
            return SwapRules.ShiftStarted();
        if (command.TargetEmployeeId == me)
            return Error.Validation("swaps.self", "Smenu ne možete prepustiti sami sebi.");

        if (await db.ShiftSwaps.AnyAsync(s => s.ShiftId == shift.Id && (s.Status == SwapStatus.Pending || s.Status == SwapStatus.Accepted), cancellationToken))
            return Error.Conflict("swaps.already_open", "Za ovu smenu već postoji otvoren zahtev.");

        if (await SwapRules.CheckTargetCanTakeAsync(db, shift, command.TargetEmployeeId, cancellationToken) is { } error)
            return error;

        var swap = new ShiftSwap(shift.RestaurantId, shift.Id, me, command.TargetEmployeeId, command.Note, now);
        db.ShiftSwaps.Add(swap);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dva zahteva za istu smenu u isto vreme: jedinstveni indeks u bazi pušta samo jedan.
            return Error.Conflict("swaps.already_open", "Za ovu smenu već postoji otvoren zahtev.");
        }

        await notifier.RequestedAsync(swap, cancellationToken);
        return await db.LoadDtoAsync(swap.Id, cancellationToken);
    }
}
