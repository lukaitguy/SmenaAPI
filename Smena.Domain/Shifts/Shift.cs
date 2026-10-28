using Smena.Domain.Common;

namespace Smena.Domain.Shifts;

/// <summary>
/// Jedna smena zaposlenog na određenoj poziciji. Vreme se čuva kao UTC,
/// a prikazuje se u zoni restorana.
/// </summary>
public class Shift : IRestaurantScoped
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(16);

    private Shift() { }

    public Shift(
        Guid restaurantId, Guid employeeId, Guid positionId, DateTimeOffset startsAt, DateTimeOffset endsAt, string? note,
        Guid? seriesId = null)
    {
        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        SeriesId = seriesId;
        Reschedule(employeeId, positionId, startsAt, endsAt, note);
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PositionId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Isti id imaju sve smene nastale jednim ponavljajućim rasporedom; null za pojedinačne smene.</summary>
    public Guid? SeriesId { get; private set; }

    public void Reschedule(Guid employeeId, Guid positionId, DateTimeOffset startsAt, DateTimeOffset endsAt, string? note)
    {
        EmployeeId = employeeId;
        PositionId = positionId;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
