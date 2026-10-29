using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.DayOffs;
using Smena.Domain.Employees;

namespace Smena.Application.Shifts;

/// <summary>Da li zaposleni sme i može da radi smenu. Isto pravilo važi za dodelu, prepuštanje i preuzimanje.</summary>
internal static class AssignmentRules
{
    public static Task<Error?> CheckAsync(
        IAppDbContext db,
        Guid employeeId,
        Guid positionId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        Guid? ignoreShiftId,
        CancellationToken cancellationToken) =>
        CheckAsync(db, employeeId, positionId, startsAt, endsAt,
            ignoreShiftId is { } id ? [id] : [], cancellationToken);

    /// <param name="ignoreShiftIds">Smene koje se ne računaju kao preklapanje, jer zaposleni upravo prestaje da ih radi (npr. pri razmeni).</param>
    public static async Task<Error?> CheckAsync(
        IAppDbContext db,
        Guid employeeId,
        Guid positionId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        IReadOnlyCollection<Guid> ignoreShiftIds,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees
            .Include(e => e.Positions)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
            return Error.NotFound("employees.not_found", "Zaposleni nije pronađen.");
        if (employee.Status != EmployeeStatus.Active)
            return Error.Conflict("shifts.employee_inactive", "Zaposleni nije aktivan.");
        if (employee.Positions.All(p => p.Id != positionId))
            return Error.Conflict("shifts.position_not_assigned", "Zaposleni nema ovu poziciju.");

        var overlaps = await db.Shifts.AnyAsync(
            s => s.EmployeeId == employeeId && !ignoreShiftIds.Contains(s.Id) && s.StartsAt < endsAt && s.EndsAt > startsAt,
            cancellationToken);
        if (overlaps)
            return Error.Conflict("shifts.overlap", "Zaposleni već ima smenu u tom periodu.");

        var zone = await DayOffRules.RestaurantZoneAsync(db, cancellationToken);
        return await DayOffRules.FindApprovedDayOffAsync(db, zone, employeeId, startsAt, endsAt, cancellationToken) is { } dayOff
            ? Error.Conflict("shifts.day_off", $"Zaposleni ima odobren slobodan dan {dayOff:dd.MM.yyyy.}.")
            : null;
    }
}
