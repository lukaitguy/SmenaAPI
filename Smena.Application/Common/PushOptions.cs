namespace Smena.Application.Common;

/// <summary>
/// VAPID podešavanja za web push. Dok ključevi nisu zadati, push je isključen, a ostatak aplikacije radi normalno.
/// Par ključeva se pravi jednom (npr. <c>npx web-push generate-vapid-keys</c>); privatni ključ je tajna.
/// </summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>Javni ključ (base64url); šalje se browseru da može da se pretplati.</summary>
    public string? PublicKey { get; init; }

    /// <summary>Privatni ključ (base64url); samo na serveru, u produkciji kroz promenljivu okruženja.</summary>
    public string? PrivateKey { get; init; }

    /// <summary>Kontakt za push servise: <c>mailto:ime@domen.rs</c> ili https adresa.</summary>
    public string Subject { get; init; } = "mailto:admin@smena.local";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
