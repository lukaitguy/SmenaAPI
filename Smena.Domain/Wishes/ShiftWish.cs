using Smena.Domain.Common;

namespace Smena.Domain.Wishes;

/// <summary>
/// Želja radnika za jedan dan: "mogu samo smenu X (npr. I) ili sam slobodan". To je samo obaveštenje za menadžera,
/// važi odmah i ništa ne blokira. Dan je kalendarski datum u zoni restorana, a željena smena je naziv šablona.
/// </summary>
public class ShiftWish : IRestaurantScoped
{
    public const int MaxNameLength = 20;

    private ShiftWish() { }

    public ShiftWish(Guid restaurantId, Guid employeeId, DateOnly date, string preferredShift, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        EmployeeId = employeeId;
        Date = date;
        CreatedAt = now;
        UpdatedAt = now;
        PreferredShift = Normalize(preferredShift);
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly Date { get; private set; }

    /// <summary>Naziv željene smene (npr. "I" ili "II"); radnik je za taj dan spreman na nju ili na slobodan dan.</summary>
    public string PreferredShift { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <returns>true ako se željena smena zaista promenila.</returns>
    public bool Change(string preferredShift, DateTimeOffset now)
    {
        var normalized = Normalize(preferredShift);
        if (normalized == PreferredShift)
            return false;

        PreferredShift = normalized;
        UpdatedAt = now;
        return true;
    }

    private static string Normalize(string preferredShift)
    {
        var name = preferredShift?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
            throw new ArgumentException($"Naziv smene je obavezan i najviše {MaxNameLength} znakova.", nameof(preferredShift));
        return name;
    }
}
