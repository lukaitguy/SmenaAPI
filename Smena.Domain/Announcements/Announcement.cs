using Smena.Domain.Common;

namespace Smena.Domain.Announcements;

/// <summary>
/// Najava menadžera (najčešće sastanak): naslov, opis i opcioni termin. Namenjena je ulogama i/ili pojedinačnim
/// osobama. Primaoci se određuju pri pravljenju i pamte se (<see cref="Recipients"/>), pa kasnije promene uloga
/// ne menjaju ko je najavu dobio.
/// </summary>
public class Announcement : IRestaurantScoped
{
    public const int MaxTitleLength = 100;
    public const int MaxBodyLength = 2000;

    private Announcement() { }

    public Announcement(
        Guid restaurantId, Guid createdByEmployeeId, string title, string? body, DateTimeOffset? meetingAt,
        IEnumerable<Guid> targetPositionIds, IEnumerable<Guid> targetEmployeeIds, DateTimeOffset now)
    {
        var trimmed = title?.Trim() ?? "";
        if (trimmed.Length is 0 or > MaxTitleLength)
            throw new ArgumentException($"Naslov je obavezan i najviše {MaxTitleLength} znakova.", nameof(title));
        var text = string.IsNullOrWhiteSpace(body) ? null : body.Trim();
        if (text is { Length: > MaxBodyLength })
            throw new ArgumentException($"Opis može imati najviše {MaxBodyLength} znakova.", nameof(body));

        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        CreatedByEmployeeId = createdByEmployeeId;
        Title = trimmed;
        Body = text;
        MeetingAt = meetingAt?.ToUniversalTime();
        TargetPositionIds = targetPositionIds.Distinct().ToList();
        TargetEmployeeIds = targetEmployeeIds.Distinct().ToList();
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Body { get; private set; }

    /// <summary>Termin sastanka (UTC); null je obična najava bez termina.</summary>
    public DateTimeOffset? MeetingAt { get; private set; }

    public Guid CreatedByEmployeeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Uloge kojima je najava namenjena.</summary>
    public List<Guid> TargetPositionIds { get; private set; } = [];

    /// <summary>Osobe kojima je najava namenjena pojedinačno (pored uloga).</summary>
    public List<Guid> TargetEmployeeIds { get; private set; } = [];

    public List<AnnouncementRecipient> Recipients { get; private set; } = [];

    public void AddRecipients(IEnumerable<Guid> employeeIds)
    {
        foreach (var employeeId in employeeIds.Distinct())
            Recipients.Add(new AnnouncementRecipient(RestaurantId, Id, employeeId));
    }
}

/// <summary>Jedan primalac najave i da li ju je pročitao ("Pročitao sam").</summary>
public class AnnouncementRecipient : IRestaurantScoped
{
    private AnnouncementRecipient() { }

    public AnnouncementRecipient(Guid restaurantId, Guid announcementId, Guid employeeId)
    {
        RestaurantId = restaurantId;
        AnnouncementId = announcementId;
        EmployeeId = employeeId;
    }

    public Guid RestaurantId { get; private set; }
    public Guid AnnouncementId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    /// <returns>true ako je ovo prvo čitanje (ponovljeno ne menja vreme).</returns>
    public bool MarkRead(DateTimeOffset now)
    {
        if (ReadAt is not null)
            return false;
        ReadAt = now;
        return true;
    }
}
