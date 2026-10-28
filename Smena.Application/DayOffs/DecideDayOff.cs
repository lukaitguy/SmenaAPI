using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.DayOffs;

/// <summary>Zaposleni otkazuje sopstveni zahtev, čak i odobren, ako se predomislio.</summary>
public sealed class CancelDayOffHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<Result<DayOffDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.DayOffRequests.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (request is null)
            return DayOffRules.NotFound();
        if (request.EmployeeId != currentUser.EmployeeId)
            return Error.Forbidden("dayoffs.not_yours", "Zahtev može da otkaže samo onaj ko ga je podneo.");
        if (!request.IsActive)
            return DayOffRules.InvalidState(request.Status);

        request.Cancel(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await db.LoadDtoAsync(request.Id, cancellationToken);
    }
}

/// <summary>Odluka menadžera. Slobodan dan se ne može odobriti dok zaposleni ima smenu tog dana.</summary>
public sealed class DecideDayOffHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    DayOffNotifier notifier,
    TimeProvider time)
{
    public async Task<Result<DayOffDto>> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.DayOffRequests.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (request is null)
            return DayOffRules.NotFound();
        if (request.Status != Domain.DayOffs.DayOffStatus.Pending)
            return DayOffRules.InvalidState(request.Status);

        var now = time.GetUtcNow();
        var zone = await DayOffRules.RestaurantZoneAsync(db, cancellationToken);
        if (request.Date < DayOffRules.LocalDate(zone, now))
            return Error.Conflict("dayoffs.date_passed", "Taj dan je već prošao.");

        // Smene (nacrti i objavljene) koje dodiruju taj kalendarski dan moraju prvo da se uklone ili premeste.
        var (dayStart, dayEnd) = DayOffRules.DayBounds(zone, request.Date);
        var shiftsThatDay = await db.Shifts.CountAsync(
            s => s.EmployeeId == request.EmployeeId && s.StartsAt < dayEnd && s.EndsAt > dayStart, cancellationToken);
        if (shiftsThatDay > 0)
            return Error.Conflict("dayoffs.has_shifts",
                $"Zaposleni već ima smena tog dana ({shiftsThatDay}). Uklonite ih ili premestite pre odobravanja.");

        request.Approve(currentUser.EmployeeId!.Value, now);
        await db.SaveChangesAsync(cancellationToken);

        await notifier.DecidedAsync(request, cancellationToken);
        return await db.LoadDtoAsync(request.Id, cancellationToken);
    }

    /// <summary>Odbija zahtev na čekanju ili povlači već odobren slobodan dan.</summary>
    public async Task<Result<DayOffDto>> RejectAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await db.DayOffRequests.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (request is null)
            return DayOffRules.NotFound();
        if (!request.IsActive)
            return DayOffRules.InvalidState(request.Status);

        request.Reject(currentUser.EmployeeId!.Value, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        await notifier.DecidedAsync(request, cancellationToken);
        return await db.LoadDtoAsync(request.Id, cancellationToken);
    }
}
