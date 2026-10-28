using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Shifts;

public sealed record DeletedShiftsDto(int Deleted);

public sealed class DeleteShiftSeriesHandler(IAppDbContext db, TimeProvider time)
{
    /// <summary>
    /// Briše smene serije koje počinju od <paramref name="from"/> (podrazumevano od sada).
    /// Prošle smene ostaju kao istorija.
    /// </summary>
    public async Task<Result<DeletedShiftsDto>> HandleAsync(Guid seriesId, DateTimeOffset? from, CancellationToken cancellationToken)
    {
        if (!await db.Shifts.AnyAsync(s => s.SeriesId == seriesId, cancellationToken))
            return Error.NotFound("shifts.series_not_found", "Serija smena nije pronađena.");

        var cutoff = (from ?? time.GetUtcNow()).ToUniversalTime();
        var deleted = await db.Shifts
            .Where(s => s.SeriesId == seriesId && s.StartsAt >= cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return new DeletedShiftsDto(deleted);
    }
}
