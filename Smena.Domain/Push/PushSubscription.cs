using Smena.Domain.Common;

namespace Smena.Domain.Push;

/// <summary>
/// Jedan uređaj (browser) zaposlenog koji prima web push obaveštenja. Isti zaposleni može imati više uređaja.
/// Adresa (<see cref="Endpoint"/>) je jedinstvena za browser, pa kad se na istom browseru prijavi neko drugi,
/// pretplata prelazi na njega.
/// </summary>
public class PushSubscription : IRestaurantScoped
{
    public const int MaxEndpointLength = 2000;
    public const int MaxKeyLength = 200;
    public const int MaxPerEmployee = 10;

    private PushSubscription() { }

    public PushSubscription(
        Guid restaurantId, Guid employeeId, string endpoint, string p256dh, string auth, string? userAgent, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        EmployeeId = employeeId;
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim()[..Math.Min(userAgent.Trim().Length, 300)];
        CreatedAt = now;
    }

    /// <summary>Isti browser, druga osoba: pretplata prelazi na nju, sa novim ključevima.</summary>
    public void Reassign(Guid employeeId, string p256dh, string auth, string? userAgent)
    {
        EmployeeId = employeeId;
        P256dh = p256dh;
        Auth = auth;
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim()[..Math.Min(userAgent.Trim().Length, 300)];
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public Guid EmployeeId { get; private set; }

    /// <summary>Adresa push servisa browsera (FCM, Mozilla, Apple) na koju se šalje poruka.</summary>
    public string Endpoint { get; private set; } = null!;

    /// <summary>Javni ključ browsera za šifrovanje poruke.</summary>
    public string P256dh { get; private set; } = null!;

    /// <summary>Tajna pretplate za šifrovanje poruke.</summary>
    public string Auth { get; private set; } = null!;

    public string? UserAgent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
