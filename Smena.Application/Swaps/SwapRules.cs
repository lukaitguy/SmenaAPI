using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Shifts;
using Smena.Domain.Shifts;

namespace Smena.Application.Swaps;

internal static class SwapRules
{
    /// <summary>Da li kolega sme i može da preuzme smenu. Proverava se pri zahtevu i ponovo pri odobravanju.</summary>
    public static Task<Error?> CheckTargetCanTakeAsync(
        IAppDbContext db, Shift shift, Guid targetEmployeeId, CancellationToken cancellationToken) =>
        AssignmentRules.CheckAsync(db, targetEmployeeId, shift.PositionId, shift.StartsAt, shift.EndsAt, shift.Id, cancellationToken);

    /// <summary>
    /// Provera za razmenu: svako mora da može da preuzme tuđu smenu, a sopstvena smena koju daje se ne
    /// računa kao preklapanje.
    /// </summary>
    public static async Task<Error?> CheckExchangeAsync(
        IAppDbContext db, ShiftSwap swap, Shift given, Shift received, CancellationToken cancellationToken)
    {
        Guid[] leaving = [given.Id, received.Id];
        return await AssignmentRules.CheckAsync(
                   db, swap.TargetEmployeeId, given.PositionId, given.StartsAt, given.EndsAt, leaving, cancellationToken)
               ?? await AssignmentRules.CheckAsync(
                   db, swap.RequesterEmployeeId, received.PositionId, received.StartsAt, received.EndsAt, leaving, cancellationToken);
    }

    /// <summary>
    /// Da li zahtev još može da se izvrši: smene nisu počele, nisu promenile vlasnika, i svi uključeni mogu da
    /// preuzmu dobijenu smenu. Vraća i kolegovu smenu kad je u pitanju razmena.
    /// </summary>
    public static async Task<(Error? Error, Shift? Received)> CheckExecutableAsync(
        IAppDbContext db, ShiftSwap swap, Shift shift, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (shift.StartsAt <= now)
            return (ShiftStarted(), null);
        if (shift.EmployeeId != swap.RequesterEmployeeId)
            return (Error.Conflict("swaps.shift_changed", "Smena je u međuvremenu dodeljena drugom zaposlenom."), null);

        if (!swap.IsExchange)
            return (await CheckTargetCanTakeAsync(db, shift, swap.TargetEmployeeId, cancellationToken), null);

        var received = await db.Shifts.FirstOrDefaultAsync(s => s.Id == swap.TargetShiftId, cancellationToken);
        if (received is null || received.StartsAt <= now)
            return (ShiftStarted(), null);
        if (received.EmployeeId != swap.TargetEmployeeId)
            return (Error.Conflict("swaps.shift_changed", "Kolegina smena je u međuvremenu dodeljena drugom zaposlenom."), null);

        return (await CheckExchangeAsync(db, swap, shift, received, cancellationToken), received);
    }

    public static Error NotFound() => Error.NotFound("swaps.not_found", "Zahtev nije pronađen.");

    public static Error InvalidState(SwapStatus status) =>
        Error.Conflict("swaps.invalid_state", $"Zahtev je u stanju {status} i ta radnja nije moguća.");

    public static Error ShiftStarted() =>
        Error.Conflict("swaps.shift_started", "Smena je već počela ili prošla.");
}
