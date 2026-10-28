using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Shifts;

public sealed class DeleteShiftHandler(IAppDbContext db)
{
    public async Task<Result> HandleAsync(Guid shiftId, CancellationToken cancellationToken)
    {
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken);
        if (shift is null)
            return Error.NotFound("shifts.not_found", "Smena nije pronađena.");

        db.Shifts.Remove(shift);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
