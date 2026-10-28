using Smena.Domain.Common;

namespace Smena.Domain.Shifts;

public enum ClaimStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}

/// <summary>
/// Zahtev zaposlenog da preuzme otvorenu smenu. Smena menja vlasnika tek kad menadžer odobri
/// jedan od zahteva; ostali zahtevi za istu smenu se tada odbijaju.
/// </summary>
public class ShiftClaim : IRestaurantScoped
{
    private ShiftClaim() { }

    public ShiftClaim(Guid restaurantId, Guid shiftId, Guid employeeId, string? note, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        ShiftId = shiftId;
        EmployeeId = employeeId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = ClaimStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid ShiftId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string? Note { get; private set; }
    public ClaimStatus Status { get; private set; }
    public Guid? DecidedByEmployeeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsPending => Status == ClaimStatus.Pending;

    public void Approve(Guid managerEmployeeId, DateTimeOffset now) => Decide(ClaimStatus.Approved, managerEmployeeId, now);

    public void Reject(Guid managerEmployeeId, DateTimeOffset now) => Decide(ClaimStatus.Rejected, managerEmployeeId, now);

    public void Cancel(DateTimeOffset now)
    {
        RequirePending(ClaimStatus.Cancelled);
        Status = ClaimStatus.Cancelled;
        UpdatedAt = now;
    }

    private void Decide(ClaimStatus to, Guid managerEmployeeId, DateTimeOffset now)
    {
        RequirePending(to);
        Status = to;
        DecidedByEmployeeId = managerEmployeeId;
        UpdatedAt = now;
    }

    private void RequirePending(ClaimStatus to)
    {
        if (Status != ClaimStatus.Pending)
            throw new InvalidOperationException($"Zahtev u stanju {Status} ne može da pređe u {to}.");
    }
}
