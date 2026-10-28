using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Auth;

namespace Smena.Application.Positions;

public sealed class ListPositionsHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<PositionDto>> HandleAsync(CancellationToken cancellationToken) =>
        await db.Positions
            .OrderBy(p => p.Name)
            .Select(p => new PositionDto(p.Id, p.Name))
            .ToListAsync(cancellationToken);
}
