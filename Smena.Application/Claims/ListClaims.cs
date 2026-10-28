using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Domain.Shifts;

namespace Smena.Application.Claims;

public sealed class ListClaimsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Svi zahtevi u restoranu (menadžer), najnoviji prvo.</summary>
    public async Task<IReadOnlyList<ClaimDto>> HandleAsync(ClaimStatus? status, CancellationToken cancellationToken)
    {
        var claims = db.ShiftClaims.AsQueryable();
        if (status is { } s)
            claims = claims.Where(c => c.Status == s);

        return await db.ProjectToDto(claims).Take(ClaimQueries.MaxListed).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimDto>> HandleMineAsync(ClaimStatus? status, CancellationToken cancellationToken)
    {
        var me = currentUser.EmployeeId;
        var claims = db.ShiftClaims.Where(c => c.EmployeeId == me);
        if (status is { } s)
            claims = claims.Where(c => c.Status == s);

        return await db.ProjectToDto(claims).Take(ClaimQueries.MaxListed).ToListAsync(cancellationToken);
    }
}
