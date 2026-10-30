using Smena.Domain.Common;

namespace Smena.Domain.Shifts;

/// <summary>
/// Unapred definisana smena jedne pozicije (npr. konobari: "I" 08:00-16:00, "II" 16:00-00:00), da menadžer
/// pravi uobičajene smene jednim klikom. Vreme je lokalno vreme restorana; kraj pre ili jednak početku znači
/// da se smena završava sutradan (16:00-00:00).
/// </summary>
public class ShiftTemplate : IRestaurantScoped
{
    public const int MaxNameLength = 20;

    /// <summary>Šabloni koje dobija svaki novi restoran: pozicija, naziv smene, od, do. Šanker nema šablone.</summary>
    public static readonly (string Position, string Name, TimeOnly Start, TimeOnly End)[] Defaults =
    [
        ("Konobar", "I", new TimeOnly(8, 0), new TimeOnly(16, 0)),
        ("Konobar", "II", new TimeOnly(16, 0), new TimeOnly(0, 0)),
        ("Kuvar", "I", new TimeOnly(7, 0), new TimeOnly(15, 0)),
        ("Kuvar", "II", new TimeOnly(15, 0), new TimeOnly(23, 0)),
        ("Hostesa", "II", new TimeOnly(16, 0), new TimeOnly(0, 0)),
    ];

    private ShiftTemplate() { }

    public ShiftTemplate(Guid restaurantId, Guid positionId, string name, TimeOnly startTime, TimeOnly endTime)
    {
        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        PositionId = positionId;
        Update(name, startTime, endTime);
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid PositionId { get; private set; }
    public string Name { get; private set; } = null!;
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    public bool EndsNextDay => EndTime <= StartTime;

    public TimeSpan Duration
    {
        get
        {
            var duration = EndTime.ToTimeSpan() - StartTime.ToTimeSpan();
            return duration > TimeSpan.Zero ? duration : duration + TimeSpan.FromDays(1);
        }
    }

    public void Update(string name, TimeOnly startTime, TimeOnly endTime)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Naziv šablona je obavezan i najviše {MaxNameLength} znakova.", nameof(name));
        if (startTime == endTime)
            throw new ArgumentException("Početak i kraj smene ne mogu biti isti.", nameof(endTime));

        Name = name.Trim();
        StartTime = startTime;
        EndTime = endTime;
    }
}
