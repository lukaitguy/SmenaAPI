using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Domain.Shifts;

namespace Smena.Application.Claims;

public sealed record ClaimDto(
    Guid Id,
    ClaimStatus Status,
    Guid ShiftId,
    DateTimeOffset ShiftStartsAt,
    DateTimeOffset ShiftEndsAt,
    string PositionName,
    Guid EmployeeId,
    string EmployeeName,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal static class ClaimQueries
{
    public const int MaxListed = 200;

    public static IQueryable<ClaimDto> ProjectToDto(this IAppDbContext db, IQueryable<ShiftClaim> claims) =>
        from c in claims
        join sh in db.Shifts on c.ShiftId equals sh.Id
        join p in db.Positions on sh.PositionId equals p.Id
        join e in db.Employees on c.EmployeeId equals e.Id
        orderby c.CreatedAt descending
        select new ClaimDto(
            c.Id,
            c.Status,
            sh.Id,
            sh.StartsAt,
            sh.EndsAt,
            p.Name,
            e.Id,
            e.FirstName + " " + e.LastName,
            c.Note,
            c.CreatedAt,
            c.UpdatedAt);

    public static Task<ClaimDto> LoadDtoAsync(this IAppDbContext db, Guid claimId, CancellationToken cancellationToken) =>
        db.ProjectToDto(db.ShiftClaims.Where(c => c.Id == claimId)).FirstAsync(cancellationToken);
}
