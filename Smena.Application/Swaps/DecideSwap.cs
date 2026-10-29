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
        if (shift is null)
            return SwapRules.ShiftStarted();

        // Menadžer je mogao u međuvremenu da izmeni smene ili raspored kolege, pa se sve proverava ponovo.
        var (error, received) = await SwapRules.CheckExecutableAsync(db, swap, shift, now, cancellationToken);
        if (error is not null)
            return error;

        shift.Reschedule(swap.TargetEmployeeId, shift.PositionId, shift.StartsAt, shift.EndsAt, shift.Note);
        received?.Reschedule(swap.RequesterEmployeeId, received.PositionId, received.StartsAt, received.EndsAt, received.Note);
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
