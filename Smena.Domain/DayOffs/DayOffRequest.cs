using Smena.Domain.Common;

namespace Smena.Domain.DayOffs;

public enum DayOffStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}

/// <summary>
/// Zahtev zaposlenog za slobodan dan. Datum je kalendarski dan u zoni restorana. Dok je odobren,
/// zaposlenom se ne mogu dodeljivati smene tog dana.
/// </summary>
public class DayOffRequest : IRestaurantScoped
{
    private DayOffRequest() { }

    public DayOffRequest(Guid restaurantId, Guid employeeId, DateOnly date, string? reason, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        EmployeeId = employeeId;
        Date = date;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = DayOffStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly Date { get; private set; }
    public string? Reason { get; private set; }
    public DayOffStatus Status { get; private set; }
    public Guid? DecidedByEmployeeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Čeka odluku ili je odobren: u oba slučaja dan je "zauzet" za isti zahtev.</summary>
    public bool IsActive => Status is DayOffStatus.Pending or DayOffStatus.Approved;

    public void Approve(Guid managerEmployeeId, DateTimeOffset now)
    {
        Require([DayOffStatus.Pending], DayOffStatus.Approved);
        Status = DayOffStatus.Approved;
        DecidedByEmployeeId = managerEmployeeId;
        UpdatedAt = now;
    }

    /// <summary>Odbija zahtev na čekanju ili povlači već odobren slobodan dan.</summary>
    public void Reject(Guid managerEmployeeId, DateTimeOffset now)
    {
        Require([DayOffStatus.Pending, DayOffStatus.Approved], DayOffStatus.Rejected);
        Status = DayOffStatus.Rejected;
        DecidedByEmployeeId = managerEmployeeId;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Require([DayOffStatus.Pending, DayOffStatus.Approved], DayOffStatus.Cancelled);
        Status = DayOffStatus.Cancelled;
        UpdatedAt = now;
    }

    private void Require(DayOffStatus[] from, DayOffStatus to)
    {
        if (!from.Contains(Status))
            throw new InvalidOperationException($"Zahtev u stanju {Status} ne može da pređe u {to}.");
    }
}
