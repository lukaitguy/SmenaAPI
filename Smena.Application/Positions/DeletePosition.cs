using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Positions;

public sealed class DeletePositionHandler(IAppDbContext db)
{
    public async Task<Result> HandleAsync(Guid positionId, CancellationToken cancellationToken)
    {
        var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == positionId, cancellationToken);
        if (position is null)
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");

        if (await db.Shifts.AnyAsync(s => s.PositionId == positionId, cancellationToken))
            return Error.Conflict("positions.in_use", "Pozicija se koristi u smenama i ne može se obrisati.");

        db.Positions.Remove(position);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
