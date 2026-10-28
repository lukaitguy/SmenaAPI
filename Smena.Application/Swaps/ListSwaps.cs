using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Domain.Shifts;

namespace Smena.Application.Swaps;

public sealed class ListSwapsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Svi zahtevi u restoranu (menadžer), najnoviji prvo.</summary>
    public async Task<IReadOnlyList<SwapDto>> HandleAsync(SwapStatus? status, CancellationToken cancellationToken)
    {
        var swaps = db.ShiftSwaps.AsQueryable();
        if (status is { } s)
            swaps = swaps.Where(x => x.Status == s);

        return await db.ProjectToDto(swaps).Take(SwapQueries.MaxListed).ToListAsync(cancellationToken);
    }

    /// <summary>Zahtevi u kojima prijavljeni zaposleni učestvuje, kao podnosilac ili kao kolega.</summary>
    public async Task<IReadOnlyList<SwapDto>> HandleMineAsync(SwapStatus? status, CancellationToken cancellationToken)
    {
        var me = currentUser.EmployeeId;
        var swaps = db.ShiftSwaps.Where(x => x.RequesterEmployeeId == me || x.TargetEmployeeId == me);
        if (status is { } s)
            swaps = swaps.Where(x => x.Status == s);

        return await db.ProjectToDto(swaps).Take(SwapQueries.MaxListed).ToListAsync(cancellationToken);
    }
}
