using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Notifications;
using Smena.Domain.Shifts;

namespace Smena.Application.Swaps;

/// <summary>Radnje samih zaposlenih: kolega prihvata ili odbija, a onaj ko traži može da otkaže.</summary>
public sealed class RespondToSwapHandler(IAppDbContext db, ICurrentUser currentUser, ManagerNotifier managerNotifier, TimeProvider time)
{
    public async Task<Result<SwapDto>> AcceptAsync(Guid swapId, CancellationToken cancellationToken)
    {
        var swap = await db.ShiftSwaps.FirstOrDefaultAsync(s => s.Id == swapId, cancellationToken);
        if (swap is null)
            return SwapRules.NotFound();
        if (swap.TargetEmployeeId != currentUser.EmployeeId)
            return Error.Forbidden("swaps.not_target", "Ovaj zahtev nije upućen vama.");
        if (swap.Status != SwapStatus.Pending)
            return SwapRules.InvalidState(swap.Status);

        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == swap.ShiftId, cancellationToken);
        if (shift is null)
            return SwapRules.ShiftStarted();

        // Od podnošenja zahteva smene su mogle da se promene ili neko da postane zauzet.
        var (error, _) = await SwapRules.CheckExecutableAsync(db, swap, shift, time.GetUtcNow(), cancellationToken);
        if (error is not null)
            return error;

        swap.Accept(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        await managerNotifier.SwapAwaitingDecisionAsync(swap, cancellationToken);
        return await db.LoadDtoAsync(swap.Id, cancellationToken);
    }

    public async Task<Result<SwapDto>> DeclineAsync(Guid swapId, CancellationToken cancellationToken)
    {
        var swap = await db.ShiftSwaps.FirstOrDefaultAsync(s => s.Id == swapId, cancellationToken);
        if (swap is null)
            return SwapRules.NotFound();
        if (swap.TargetEmployeeId != currentUser.EmployeeId)
            return Error.Forbidden("swaps.not_target", "Ovaj zahtev nije upućen vama.");
        if (swap.Status != SwapStatus.Pending)
            return SwapRules.InvalidState(swap.Status);

        swap.Decline(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await db.LoadDtoAsync(swap.Id, cancellationToken);
    }

    public async Task<Result<SwapDto>> CancelAsync(Guid swapId, CancellationToken cancellationToken)
    {
        var swap = await db.ShiftSwaps.FirstOrDefaultAsync(s => s.Id == swapId, cancellationToken);
        if (swap is null)
            return SwapRules.NotFound();
        if (swap.RequesterEmployeeId != currentUser.EmployeeId)
            return Error.Forbidden("swaps.not_requester", "Zahtev može da otkaže samo onaj ko ga je podneo.");
        if (!swap.IsOpen)
            return SwapRules.InvalidState(swap.Status);

        swap.Cancel(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await db.LoadDtoAsync(swap.Id, cancellationToken);
    }
}
