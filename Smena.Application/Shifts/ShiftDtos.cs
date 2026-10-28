using Smena.Application.Abstractions;
using Smena.Domain.Shifts;

namespace Smena.Application.Shifts;

public sealed record ShiftDto(
    Guid Id,
    Guid? EmployeeId,
    string? EmployeeName,
    Guid PositionId,
    string PositionName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Note,
    Guid? SeriesId,
    DateTimeOffset? PublishedAt);

internal static class ShiftQueries
{
    public const int MaxRangeDays = 62;

    public static IQueryable<ShiftDto> ProjectToDto(this IAppDbContext db, IQueryable<Shift> shifts) =>
        from s in shifts
        join p in db.Positions on s.PositionId equals p.Id
        from e in db.Employees.Where(e => e.Id == s.EmployeeId).DefaultIfEmpty()
        orderby s.StartsAt, e.FirstName
        select new ShiftDto(
            s.Id,
            s.EmployeeId,
            e == null ? null : e.FirstName + " " + e.LastName,
            p.Id,
            p.Name,
            s.StartsAt,
            s.EndsAt,
            s.Note,
            s.SeriesId,
            s.PublishedAt);
}
