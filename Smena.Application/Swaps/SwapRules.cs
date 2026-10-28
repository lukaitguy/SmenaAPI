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

    public static Error NotFound() => Error.NotFound("swaps.not_found", "Zahtev nije pronađen.");

    public static Error InvalidState(SwapStatus status) =>
        Error.Conflict("swaps.invalid_state", $"Zahtev je u stanju {status} i ta radnja nije moguća.");

    public static Error ShiftStarted() =>
        Error.Conflict("swaps.shift_started", "Smena je već počela ili prošla.");
}
