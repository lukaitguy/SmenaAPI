namespace Smena.Domain.Restaurants;

public class Restaurant
{
    public const string DefaultTimeZoneId = "Europe/Belgrade";

    private Restaurant() { }

    public Restaurant(string name, string timeZoneId, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        TimeZoneId = timeZoneId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>IANA zona u kojoj se prikazuju smene; vreme se u bazi čuva kao UTC.</summary>
    public string TimeZoneId { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
}
