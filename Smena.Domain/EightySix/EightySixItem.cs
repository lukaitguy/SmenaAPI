using Smena.Domain.Common;

namespace Smena.Domain.EightySix;

/// <summary>
/// Stavka sa "86 liste": nešto što je ispalo iz ponude jer nema na stanju. Pripada odeljku (poziciji Kuvar ili Šanker)
/// i stoji na listi dok neko ne javi da se vratila, kada se briše.
/// </summary>
public class EightySixItem : IRestaurantScoped
{
    public const int MaxNameLength = 100;
    public const int MaxNoteLength = 200;

    /// <summary>Odeljci 86 liste su pozicije sa ovim imenima (kuhinja i šank).</summary>
    public static readonly string[] SectionPositionNames = ["Kuvar", "Šanker"];

    private EightySixItem() { }

    public EightySixItem(Guid restaurantId, Guid positionId, Guid createdByEmployeeId, string name, string? note, DateTimeOffset now)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length is 0 or > MaxNameLength)
            throw new ArgumentException($"Naziv stavke je obavezan i najviše {MaxNameLength} znakova.", nameof(name));

        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        PositionId = positionId;
        CreatedByEmployeeId = createdByEmployeeId;
        Name = trimmed;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }

    /// <summary>Odeljak liste: pozicija Kuvar ili Šanker.</summary>
    public Guid PositionId { get; private set; }

    public string Name { get; private set; } = null!;
    public string? Note { get; private set; }
    public Guid CreatedByEmployeeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
