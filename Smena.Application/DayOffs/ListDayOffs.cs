using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Domain.DayOffs;

namespace Smena.Application.DayOffs;

public sealed class ListDayOffsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Svi zahtevi u restoranu (menadžer); korisno npr. sa status=Approved i periodom pri pravljenju rasporeda.</summary>
    public Task<IReadOnlyList<DayOffDto>> HandleAsync(DayOffStatus? status, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        QueryAsync(db.DayOffRequests.AsQueryable(), status, from, to, cancellationToken);

    public Task<IReadOnlyList<DayOffDto>> HandleMineAsync(DayOffStatus? status, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var me = currentUser.EmployeeId;
        return QueryAsync(db.DayOffRequests.Where(d => d.EmployeeId == me), status, from, to, cancellationToken);
    }

    private async Task<IReadOnlyList<DayOffDto>> QueryAsync(
        IQueryable<DayOffRequest> requests, DayOffStatus? status, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (status is { } s)
            requests = requests.Where(d => d.Status == s);
        if (from is { } f)
            requests = requests.Where(d => d.Date >= f);
        if (to is { } t)
            requests = requests.Where(d => d.Date <= t);

        return await db.ProjectToDto(requests).Take(DayOffQueries.MaxListed).ToListAsync(cancellationToken);
    }
}
