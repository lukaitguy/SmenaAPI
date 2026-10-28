using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Domain.DayOffs;

namespace Smena.Application.DayOffs;

public sealed record DayOffDto(
    Guid Id,
    DayOffStatus Status,
    DateOnly Date,
    Guid EmployeeId,
    string EmployeeName,
    string? Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal static class DayOffQueries
{
    public const int MaxListed = 200;

    public static IQueryable<DayOffDto> ProjectToDto(this IAppDbContext db, IQueryable<DayOffRequest> requests) =>
        from d in requests
        join e in db.Employees on d.EmployeeId equals e.Id
        orderby d.Date, e.FirstName
        select new DayOffDto(
            d.Id,
            d.Status,
            d.Date,
            e.Id,
            e.FirstName + " " + e.LastName,
            d.Reason,
            d.CreatedAt,
            d.UpdatedAt);

    public static Task<DayOffDto> LoadDtoAsync(this IAppDbContext db, Guid id, CancellationToken cancellationToken) =>
        db.ProjectToDto(db.DayOffRequests.Where(d => d.Id == id)).FirstAsync(cancellationToken);
}
