using Smena.Domain.Common;

namespace Smena.Domain.Shifts;

public enum SwapStatus
{
    /// <summary>Čeka odgovor kolege kome se smena nudi.</summary>
    Pending = 0,

    /// <summary>Kolega je prihvatio, čeka odluku menadžera.</summary>
    Accepted = 1,

    Declined = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
}

/// <summary>
/// Zahtev da zaposleni prepusti svoju smenu kolegi (prepuštanje) ili je zameni za kolegovu (razmena,
/// kad je zadat <see cref="TargetShiftId"/>). Smene menjaju vlasnika tek kad menadžer odobri zahtev
/// koji je kolega prethodno prihvatio.
/// </summary>
public class ShiftSwap : IRestaurantScoped
{
    private ShiftSwap() { }

    public ShiftSwap(
        Guid restaurantId, Guid shiftId, Guid requesterEmployeeId, Guid targetEmployeeId, string? note, DateTimeOffset now,
        Guid? targetShiftId = null)
    {
        if (requesterEmployeeId == targetEmployeeId)
            throw new InvalidOperationException("Smena se ne može prepustiti samom sebi.");
        if (targetShiftId == shiftId)
            throw new InvalidOperationException("Smena se ne može zameniti samom sobom.");

        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        ShiftId = shiftId;
        TargetShiftId = targetShiftId;
        RequesterEmployeeId = requesterEmployeeId;
        TargetEmployeeId = targetEmployeeId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = SwapStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid ShiftId { get; private set; }

    /// <summary>Kolegina smena koju podnosilac dobija zauzvrat; null znači da se smena samo prepušta.</summary>
    public Guid? TargetShiftId { get; private set; }

    public bool IsExchange => TargetShiftId is not null;

    public Guid RequesterEmployeeId { get; private set; }
    public Guid TargetEmployeeId { get; private set; }
    public string? Note { get; private set; }
    public SwapStatus Status { get; private set; }
    public Guid? DecidedByEmployeeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsOpen => Status is SwapStatus.Pending or SwapStatus.Accepted;

    public void Accept(DateTimeOffset now) => Move([SwapStatus.Pending], SwapStatus.Accepted, now);

    public void Decline(DateTimeOffset now) => Move([SwapStatus.Pending], SwapStatus.Declined, now);

    public void Cancel(DateTimeOffset now) => Move([SwapStatus.Pending, SwapStatus.Accepted], SwapStatus.Cancelled, now);

    public void Approve(Guid managerEmployeeId, DateTimeOffset now)
    {
        Move([SwapStatus.Accepted], SwapStatus.Approved, now);
        DecidedByEmployeeId = managerEmployeeId;
    }

    public void Reject(Guid managerEmployeeId, DateTimeOffset now)
    {
        Move([SwapStatus.Pending, SwapStatus.Accepted], SwapStatus.Rejected, now);
        DecidedByEmployeeId = managerEmployeeId;
    }

    private void Move(SwapStatus[] from, SwapStatus to, DateTimeOffset now)
    {
        if (!from.Contains(Status))
            throw new InvalidOperationException($"Zahtev u stanju {Status} ne može da pređe u {to}.");

        Status = to;
        UpdatedAt = now;
    }
}
