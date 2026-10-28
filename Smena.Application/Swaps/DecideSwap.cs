using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Shifts;

namespace Smena.Application.Swaps;

/// <summary>Odluka menadžera. Odobravanje je jedino mesto na kojem smena menja vlasnika.</summary>
public sealed class DecideSwapHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    SwapNotifier notifier,
    TimeProvider time)
{
    public async Task<Result<SwapDto>> ApproveAsync(Guid swapId, CancellationToken cancellationToken)
    {
        var swap = await db.ShiftSwaps.FirstOrDefaultAsync(s => s.Id == swapId, cancellationToken);
        if (swap is null)
            return SwapRules.NotFound();
        if (swap.Status != SwapStatus.Accepted)
            return SwapRules.InvalidState(swap.Status);

        var now = time.GetUtcNow();
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == swap.ShiftId, cancellationToken);
        if (shift is null || shift.StartsAt <= now)
            return SwapRules.ShiftStarted();

        // Menadžer je mogao u međuvremenu da izmeni smenu ili raspored kolege, pa se sve proverava ponovo.
        if (shift.EmployeeId != swap.RequesterEmployeeId)
            return Error.Conflict("swaps.shift_changed", "Smena je u međuvremenu dodeljena drugom zaposlenom.");
        if (await SwapRules.CheckTargetCanTakeAsync(db, shift, swap.TargetEmployeeId, cancellationToken) is { } error)
            return error;

        shift.Reschedule(swap.TargetEmployeeId, shift.PositionId, shift.StartsAt, shift.EndsAt, shift.Note);
        swap.Approve(currentUser.EmployeeId!.Value, now);
        await db.SaveChangesAsync(cancellationToken);

        await notifier.DecidedAsync(swap, cancellationToken);
        return await db.LoadDtoAsync(swap.Id, cancellationToken);
    }

    public async Task<Result<SwapDto>> RejectAsync(Guid swapId, CancellationToken cancellationToken)
    {
        var swap = await db.ShiftSwaps.FirstOrDefaultAsync(s => s.Id == swapId, cancellationToken);
        if (swap is null)
            return SwapRules.NotFound();
        if (!swap.IsOpen)
            return SwapRules.InvalidState(swap.Status);

        swap.Reject(currentUser.EmployeeId!.Value, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        await notifier.DecidedAsync(swap, cancellationToken);
        return await db.LoadDtoAsync(swap.Id, cancellationToken);
    }
}
