using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Shifts;

public sealed class ListShiftsHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    /// <summary>
    /// Raspored restorana za period, uključujući nacrte; menadžer može da suzi na jednog zaposlenog
    /// i/ili na jednu poziciju (raspored konobara, kuvara...).
    /// </summary>
    public Task<Result<IReadOnlyList<ShiftDto>>> HandleAsync(
        DateTimeOffset from, DateTimeOffset to, Guid? employeeId, Guid? positionId, CancellationToken cancellationToken) =>
        QueryAsync(from, to, employeeId, positionId, publishedOnly: false, cancellationToken);

    /// <summary>Objavljene smene prijavljenog zaposlenog; nacrti se ne vide dok ih menadžer ne objavi.</summary>
    public Task<Result<IReadOnlyList<ShiftDto>>> HandleMineAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        QueryAsync(from, to, currentUser.EmployeeId, positionId: null, publishedOnly: true, cancellationToken);

    /// <summary>
    /// Objavljene otvorene smene koje tek počinju, samo za pozicije koje prijavljeni zaposleni ima.
    /// </summary>
    public async Task<Result<IReadOnlyList<ShiftDto>>> HandleOpenAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (ValidateRange(from, to) is { } error)
            return error;

        var earliest = new[] { from.ToUniversalTime(), time.GetUtcNow() }.Max();
        var latest = to.ToUniversalTime();
        var myPositions = await db.Employees
            .Where(e => e.Id == currentUser.EmployeeId)
            .SelectMany(e => e.Positions.Select(p => p.Id))
            .ToListAsync(cancellationToken);

        var shifts = db.Shifts.Where(s =>
            s.EmployeeId == null && s.PublishedAt != null &&
            s.StartsAt >= earliest && s.StartsAt < latest &&
            myPositions.Contains(s.PositionId));

        return await db.ProjectToDto(shifts).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Objavljen raspored uloga prijavljenog zaposlenog: smene svih kolega na njegovim pozicijama, plus otvorene
    /// smene tih pozicija. <paramref name="positionId"/> suzuje na jednu od njegovih pozicija.
    /// </summary>
    public async Task<Result<IReadOnlyList<ShiftDto>>> HandleScheduleAsync(
        DateTimeOffset from, DateTimeOffset to, Guid? positionId, CancellationToken cancellationToken)
    {
        if (ValidateRange(from, to) is { } error)
            return error;

        var myPositions = await db.Employees
            .Where(e => e.Id == currentUser.EmployeeId)
            .SelectMany(e => e.Positions.Select(p => p.Id))
            .ToListAsync(cancellationToken);
        if (positionId is { } only)
            myPositions = myPositions.Where(p => p == only).ToList();

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var shifts = db.Shifts.Where(s =>
            s.PublishedAt != null && s.StartsAt < toUtc && s.EndsAt > fromUtc && myPositions.Contains(s.PositionId));

        return await db.ProjectToDto(shifts).ToListAsync(cancellationToken);
    }

    private static Error? ValidateRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
            return Error.Validation("shifts.invalid_range", "Kraj perioda mora biti posle početka.");
        if (to - from > TimeSpan.FromDays(ShiftQueries.MaxRangeDays))
            return Error.Validation("shifts.range_too_long", $"Period ne može biti duži od {ShiftQueries.MaxRangeDays} dana.");
        return null;
    }

    private async Task<Result<IReadOnlyList<ShiftDto>>> QueryAsync(
        DateTimeOffset from, DateTimeOffset to, Guid? employeeId, Guid? positionId, bool publishedOnly, CancellationToken cancellationToken)
    {
        if (ValidateRange(from, to) is { } error)
            return error;

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        // Smena ulazi u period ako se bilo kojim delom preklapa s njim.
        var shifts = db.Shifts.Where(s => s.StartsAt < toUtc && s.EndsAt > fromUtc);
        if (employeeId is { } id)
            shifts = shifts.Where(s => s.EmployeeId == id);
        if (positionId is { } position)
            shifts = shifts.Where(s => s.PositionId == position);
        if (publishedOnly)
            shifts = shifts.Where(s => s.PublishedAt != null);

        return await db.ProjectToDto(shifts).ToListAsync(cancellationToken);
    }
}
