using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Shifts;

public sealed class ListShiftsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Raspored restorana za period; menadžer može da suzi na jednog zaposlenog.</summary>
    public Task<Result<IReadOnlyList<ShiftDto>>> HandleAsync(
        DateTimeOffset from, DateTimeOffset to, Guid? employeeId, CancellationToken cancellationToken) =>
        QueryAsync(from, to, employeeId, cancellationToken);

    /// <summary>Smene prijavljenog zaposlenog.</summary>
    public Task<Result<IReadOnlyList<ShiftDto>>> HandleMineAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        QueryAsync(from, to, currentUser.EmployeeId, cancellationToken);

    private async Task<Result<IReadOnlyList<ShiftDto>>> QueryAsync(
        DateTimeOffset from, DateTimeOffset to, Guid? employeeId, CancellationToken cancellationToken)
    {
        if (to <= from)
            return Error.Validation("shifts.invalid_range", "Kraj perioda mora biti posle početka.");
        if (to - from > TimeSpan.FromDays(ShiftQueries.MaxRangeDays))
            return Error.Validation("shifts.range_too_long", $"Period ne može biti duži od {ShiftQueries.MaxRangeDays} dana.");

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        // Smena ulazi u period ako se bilo kojim delom preklapa s njim.
        var shifts = db.Shifts.Where(s => s.StartsAt < toUtc && s.EndsAt > fromUtc);
        if (employeeId is { } id)
            shifts = shifts.Where(s => s.EmployeeId == id);

        return await db.ProjectToDto(shifts).ToListAsync(cancellationToken);
    }
}
