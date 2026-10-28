using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Shifts;

namespace Smena.Application.Claims;

internal static class ClaimResolution
{
    /// <summary>
    /// Zatvara sve zahteve na čekanju za smenu koja je upravo dodeljena: zahtev onog kome je
    /// dodeljena je odobren, ostali su odbijeni. Vraća zahteve da bi se podnosioci obavestili.
    /// </summary>
    public static async Task<IReadOnlyCollection<ShiftClaim>> ResolveAsync(
        IAppDbContext db, Shift shift, Guid assignedEmployeeId, Guid managerEmployeeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await db.ShiftClaims
            .Where(c => c.ShiftId == shift.Id && c.Status == ClaimStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var claim in pending)
        {
            if (claim.EmployeeId == assignedEmployeeId)
                claim.Approve(managerEmployeeId, now);
            else
                claim.Reject(managerEmployeeId, now);
        }

        return pending;
    }

    public static Error NotFound() => Error.NotFound("claims.not_found", "Zahtev nije pronađen.");

    public static Error InvalidState(ClaimStatus status) =>
        Error.Conflict("claims.invalid_state", $"Zahtev je u stanju {status} i ta radnja nije moguća.");

    public static Error ShiftTaken() =>
        Error.Conflict("claims.shift_taken", "Smena je u međuvremenu dodeljena.");

    public static Error ShiftStarted() =>
        Error.Conflict("claims.shift_started", "Smena je već počela ili prošla.");
}
