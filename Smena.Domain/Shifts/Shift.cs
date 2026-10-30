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
        Guid restaurantId, Guid? employeeId, Guid positionId, DateTimeOffset startsAt, DateTimeOffset endsAt, string? note,
        Guid? seriesId = null, Guid? templateId = null)
    {
        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        SeriesId = seriesId;
        TemplateId = templateId;
        Reschedule(employeeId, positionId, startsAt, endsAt, note);
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }

    /// <summary>Zaposleni kome je smena dodeljena; null je otvorena smena koju zaposleni mogu da traže.</summary>
    public Guid? EmployeeId { get; private set; }

    public bool IsOpen => EmployeeId is null;

    public Guid PositionId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Isti id imaju sve smene nastale jednim ponavljajućim rasporedom; null za pojedinačne smene.</summary>
    public Guid? SeriesId { get; private set; }

    /// <summary>Šablon (npr. "I") po kome je smena napravljena; null je smena sa proizvoljnim vremenom.</summary>
    public Guid? TemplateId { get; private set; }

    /// <summary>Nova smena je nacrt (vidi je samo menadžer) dok je ne objavi; null znači nacrt.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    public bool IsPublished => PublishedAt is not null;

    public void Publish(DateTimeOffset now) => PublishedAt ??= now;

    /// <summary>Postavlja šablon po kome je smena (null za proizvoljno vreme). Vreme ostaje kakvo jeste.</summary>
    public void UseTemplate(Guid? templateId) => TemplateId = templateId;

    /// <summary>Vraća smenu u nacrt: zaposleni je više ne vide, a zahtevi vezani za nju ostaju.</summary>
    public void Unpublish() => PublishedAt = null;

    public void Reschedule(Guid? employeeId, Guid positionId, DateTimeOffset startsAt, DateTimeOffset endsAt, string? note)
    {
        EmployeeId = employeeId;
        PositionId = positionId;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
