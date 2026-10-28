using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Smena.Infrastructure.Security;

/// <summary>
/// Sertifikat (PFX) kojim se šifruju DataProtection ključevi u bazi. Zadaje se putanjom do fajla
/// ili kao base64 sadržaj, što je zgodno za env varijable i tajne u deploy okruženju.
/// </summary>
public sealed class CertificateSource
{
    public string? Path { get; init; }
    public string? Base64 { get; init; }
    public string? Password { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Path) || !string.IsNullOrWhiteSpace(Base64);

    public X509Certificate2 Load() => !string.IsNullOrWhiteSpace(Base64)
        ? X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(Base64), Password)
        : X509CertificateLoader.LoadPkcs12FromFile(Path!, Password);
}

public sealed class KeyProtectionOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Sertifikat kojim se šifruju novi ključevi.</summary>
    public CertificateSource Certificate { get; init; } = new();

    /// <summary>
    /// Prethodni sertifikat, dok traje rotacija: ključevi šifrovani njime ostaju čitljivi,
    /// a novi se šifruju trenutnim.
    /// </summary>
    public CertificateSource PreviousCertificate { get; init; } = new();

    public bool IsEncryptionConfigured => Certificate.IsConfigured;

    public static KeyProtectionOptions From(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Get<KeyProtectionOptions>() ?? new KeyProtectionOptions();
}

public static class KeyProtection
{
    /// <summary>
    /// Šifruje ključeve sertifikatom ako je zadat. Ključevi koji su ranije upisani nešifrovani
    /// ostaju čitljivi, pa uključivanje šifrovanja ne prekida postojeće tokene.
    /// </summary>
    public static IDataProtectionBuilder ProtectKeys(this IDataProtectionBuilder builder, KeyProtectionOptions options)
    {
        if (!options.IsEncryptionConfigured)
            return builder;

        builder.ProtectKeysWithCertificate(options.Certificate.Load());

        if (options.PreviousCertificate.IsConfigured)
            builder.UnprotectKeysWithAnyCertificate(options.PreviousCertificate.Load());

        return builder;
    }
}
