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

        db.Positions.Remove(position);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
