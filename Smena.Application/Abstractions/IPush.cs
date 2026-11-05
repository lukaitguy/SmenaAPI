namespace Smena.Application.Abstractions;

/// <summary>Obaveštenje za zaposlene: naslov, kratak tekst i stranica aplikacije koja se otvara klikom.</summary>
public sealed record PushMessage(IReadOnlyCollection<Guid> EmployeeIds, string Title, string Body, string Url);

/// <summary>
/// Stavlja web push u red za slanje u pozadini (kao <see cref="IEmailQueue"/>): zahtev ne čeka push servise
/// browsera. Greške slanja se loguju i ne vraćaju pozivaocu.
/// </summary>
public interface IPushQueue
{
    void Enqueue(PushMessage message);
}

/// <summary>Jedna pretplata na koju se šalje poruka.</summary>
public sealed record PushTarget(string Endpoint, string P256dh, string Auth);

public enum PushResult
{
    Sent,

    /// <summary>Pretplata više ne važi (browser je odjavljen ili obrisan): treba je ukloniti.</summary>
    Gone,

    Failed,
}

/// <summary>Šalje jednu već pripremljenu poruku na jednu pretplatu; implementacija u Infrastructure sloju.</summary>
public interface IPushSender
{
    bool IsConfigured { get; }

    Task<PushResult> SendAsync(PushTarget target, string payload, CancellationToken cancellationToken);
}
