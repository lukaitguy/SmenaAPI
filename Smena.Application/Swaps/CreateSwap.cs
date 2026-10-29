using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Shifts;

namespace Smena.Application.Swaps;

/// <param name="ShiftId">Moja smena koju dajem.</param>
/// <param name="TargetEmployeeId">Kolega koji preuzima moju smenu; može da se izostavi kad je zadata <paramref name="TargetShiftId"/>.</param>
/// <param name="TargetShiftId">Kolegina smena koju dobijam zauzvrat (razmena); izostavljeno znači samo prepuštanje.</param>
public sealed record CreateSwapCommand(Guid ShiftId, Guid? TargetEmployeeId, string? Note, Guid? TargetShiftId = null);

public sealed class CreateSwapValidator : AbstractValidator<CreateSwapCommand>
{
    public CreateSwapValidator()
    {
        RuleFor(x => x.ShiftId).NotEmpty();
        RuleFor(x => x.TargetEmployeeId).NotEqual(Guid.Empty);
        RuleFor(x => x.TargetShiftId).NotEqual(Guid.Empty);
        RuleFor(x => x).Must(x => x.TargetEmployeeId is not null || x.TargetShiftId is not null)
            .WithName(nameof(CreateSwapCommand.TargetEmployeeId))
            .WithMessage("Navedite kolegu ili njegovu smenu.");
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

        Shift? received = null;
        Guid targetEmployeeId;
        if (command.TargetShiftId is { } receivedId)
        {
            if (receivedId == shift.Id)
                return Error.Validation("swaps.same_shift", "Smenu ne možete zameniti samom sobom.");

            received = await db.Shifts.FirstOrDefaultAsync(s => s.Id == receivedId, cancellationToken);
            if (received is null || !received.IsPublished)
                return Error.NotFound("shifts.not_found", "Smena nije pronađena.");
            if (received.IsOpen)
                return Error.Conflict("swaps.target_shift_open", "Smena nema zaposlenog. Otvorena smena se traži, ne menja.");
            if (received.StartsAt <= now)
                return SwapRules.ShiftStarted();

            targetEmployeeId = received.EmployeeId!.Value;
            if (command.TargetEmployeeId is { } named && named != targetEmployeeId)
                return Error.Validation("swaps.target_mismatch", "Navedena smena ne pripada navedenom kolegi.");
        }
        else
        {
            targetEmployeeId = command.TargetEmployeeId!.Value;
        }

        if (targetEmployeeId == me)
            return Error.Validation("swaps.self", "Smenu ne možete prepustiti sami sebi.");

        // Smena koja već učestvuje u otvorenom zahtevu (kao moja ili kao kolegina) ne može u drugi.
        var involved = received is null ? new[] { shift.Id } : [shift.Id, received.Id];
        if (await db.ShiftSwaps.AnyAsync(
                s => (s.Status == SwapStatus.Pending || s.Status == SwapStatus.Accepted)
                     && (involved.Contains(s.ShiftId) || (s.TargetShiftId != null && involved.Contains(s.TargetShiftId.Value))),
                cancellationToken))
            return Error.Conflict("swaps.already_open", "Za ovu smenu već postoji otvoren zahtev.");

        var swap = new ShiftSwap(shift.RestaurantId, shift.Id, me, targetEmployeeId, command.Note, now, received?.Id);
        var error = received is null
            ? await SwapRules.CheckTargetCanTakeAsync(db, shift, targetEmployeeId, cancellationToken)
            : await SwapRules.CheckExchangeAsync(db, swap, shift, received, cancellationToken);
        if (error is not null)
            return error;

        db.ShiftSwaps.Add(swap);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dva zahteva za istu smenu u isto vreme: jedinstveni indeksi u bazi puštaju samo jedan.
            return Error.Conflict("swaps.already_open", "Za ovu smenu već postoji otvoren zahtev.");
        }

        await notifier.RequestedAsync(swap, cancellationToken);
        return await db.LoadDtoAsync(swap.Id, cancellationToken);
    }
}
