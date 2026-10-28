using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;
using Smena.Domain.Shifts;

namespace Smena.Application.Swaps;

internal static class SwapRules
{
    /// <summary>Da li kolega sme i može da preuzme smenu. Proverava se pri zahtevu i ponovo pri odobravanju.</summary>
    public static async Task<Error?> CheckTargetCanTakeAsync(
        IAppDbContext db, Shift shift, Guid targetEmployeeId, CancellationToken cancellationToken)
    {
        var target = await db.Employees
            .Include(e => e.Positions)
            .FirstOrDefaultAsync(e => e.Id == targetEmployeeId, cancellationToken);
        if (target is null)
            return Error.NotFound("employees.not_found", "Zaposleni nije pronađen.");
        if (target.Status != EmployeeStatus.Active)
            return Error.Conflict("swaps.target_inactive", "Kolega nije aktivan.");
        if (target.Positions.All(p => p.Id != shift.PositionId))
            return Error.Conflict("swaps.target_position", "Kolega nema poziciju za ovu smenu.");

        var busy = await db.Shifts.AnyAsync(
            s => s.EmployeeId == targetEmployeeId && s.Id != shift.Id && s.StartsAt < shift.EndsAt && s.EndsAt > shift.StartsAt,
            cancellationToken);
        return busy
            ? Error.Conflict("swaps.target_busy", "Kolega već ima smenu u tom periodu.")
            : null;
    }

    public static Error NotFound() => Error.NotFound("swaps.not_found", "Zahtev nije pronađen.");

    public static Error InvalidState(SwapStatus status) =>
        Error.Conflict("swaps.invalid_state", $"Zahtev je u stanju {status} i ta radnja nije moguća.");

    public static Error ShiftStarted() =>
        Error.Conflict("swaps.shift_started", "Smena je već počela ili prošla.");
}
