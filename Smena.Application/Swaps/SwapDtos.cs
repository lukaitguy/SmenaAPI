using Smena.Application.Abstractions;
using Smena.Domain.Shifts;

namespace Smena.Application.Swaps;

public sealed record SwapDto(
    Guid Id,
    SwapStatus Status,
    Guid ShiftId,
    DateTimeOffset ShiftStartsAt,
    DateTimeOffset ShiftEndsAt,
    string PositionName,
    Guid RequesterId,
    string RequesterName,
    Guid TargetId,
    string TargetName,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal static class SwapQueries
{
    public const int MaxListed = 200;

    public static IQueryable<SwapDto> ProjectToDto(this IAppDbContext db, IQueryable<ShiftSwap> swaps) =>
        from s in swaps
        join sh in db.Shifts on s.ShiftId equals sh.Id
        join p in db.Positions on sh.PositionId equals p.Id
        join r in db.Employees on s.RequesterEmployeeId equals r.Id
        join t in db.Employees on s.TargetEmployeeId equals t.Id
        orderby s.CreatedAt descending
        select new SwapDto(
            s.Id,
            s.Status,
            sh.Id,
            sh.StartsAt,
            sh.EndsAt,
            p.Name,
            r.Id,
            r.FirstName + " " + r.LastName,
            t.Id,
            t.FirstName + " " + t.LastName,
            s.Note,
            s.CreatedAt,
            s.UpdatedAt);

    public static Task<SwapDto> LoadDtoAsync(this IAppDbContext db, Guid swapId, CancellationToken cancellationToken) =>
        Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(
            db.ProjectToDto(db.ShiftSwaps.Where(s => s.Id == swapId)), cancellationToken);
}
