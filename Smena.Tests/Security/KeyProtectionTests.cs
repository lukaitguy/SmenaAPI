using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Smena.Infrastructure.Security;

namespace Smena.Tests.Security;

public sealed class KeyProtectionTests : IDisposable
{
    private const string PfxPassword = "test-lozinka";
    private readonly string _keyDir = Directory.CreateTempSubdirectory("smena-keys-").FullName;

    public void Dispose() => Directory.Delete(_keyDir, recursive: true);

    private static byte[] NewPfx()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=smena-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return certificate.Export(X509ContentType.Pfx, PfxPassword);
    }

    private static CertificateSource Source(byte[] pfx) =>
        new() { Base64 = Convert.ToBase64String(pfx), Password = PfxPassword };

    private IDataProtector Protector(KeyProtectionOptions options) =>
        new ServiceCollection()
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(_keyDir))
            .SetApplicationName("Smena")
            .ProtectKeys(options)
            .Services.BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("test");

    private string KeyXml() =>
        string.Concat(Directory.GetFiles(_keyDir, "*.xml").Select(File.ReadAllText));

    [Fact]
    public void Without_a_certificate_keys_are_stored_in_plain_text()
    {
        Protector(new KeyProtectionOptions()).Protect("tajna");

        Assert.Contains("<masterKey", KeyXml());
    }

    [Fact]
    public void With_a_certificate_keys_are_encrypted_at_rest()
    {
        var options = new KeyProtectionOptions { Certificate = Source(NewPfx()) };

        var payload = Protector(options).Protect("tajna");

        var xml = KeyXml();
        Assert.DoesNotContain("<masterKey", xml);
        Assert.Contains("EncryptedData", xml);
        Assert.Equal("tajna", Protector(options).Unprotect(payload));
    }

    [Fact]
    public void Enabling_encryption_keeps_existing_plain_text_keys_readable()
    {
        var payload = Protector(new KeyProtectionOptions()).Protect("tajna");

        var withCertificate = new KeyProtectionOptions { Certificate = Source(NewPfx()) };

        Assert.Equal("tajna", Protector(withCertificate).Unprotect(payload));
    }

    [Fact]
    public void Previous_certificate_keeps_old_keys_readable_during_rotation()
    {
        var oldPfx = NewPfx();
        var payload = Protector(new KeyProtectionOptions { Certificate = Source(oldPfx) }).Protect("tajna");
        var rotated = new KeyProtectionOptions { Certificate = Source(NewPfx()), PreviousCertificate = Source(oldPfx) };

        Assert.Equal("tajna", Protector(rotated).Unprotect(payload));
    }

    [Fact]
    public void Without_the_previous_certificate_old_keys_cannot_be_read()
    {
        var oldPfx = NewPfx();
        var payload = Protector(new KeyProtectionOptions { Certificate = Source(oldPfx) }).Protect("tajna");
        var forgotten = new KeyProtectionOptions { Certificate = Source(NewPfx()) };

        Assert.ThrowsAny<CryptographicException>(() => Protector(forgotten).Unprotect(payload));
    }

    [Fact]
    public void Certificate_can_be_loaded_from_a_file()
    {
        var pfx = NewPfx();
        var path = Path.Combine(_keyDir, "keys.pfx");
        File.WriteAllBytes(path, pfx);

        using var fromFile = new CertificateSource { Path = path, Password = PfxPassword }.Load();
        using var fromBase64 = Source(pfx).Load();

        Assert.Equal(fromBase64.Thumbprint, fromFile.Thumbprint);
        Assert.True(fromFile.HasPrivateKey);
    }

    [Fact]
    public void Options_are_read_from_configuration()
    {
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Certificate:Base64"] = Convert.ToBase64String(NewPfx()),
            ["DataProtection:Certificate:Password"] = PfxPassword,
        }).Build();
        var empty = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Certificate:Base64"] = "",
        }).Build();

        Assert.True(KeyProtectionOptions.From(configured).IsEncryptionConfigured);
        Assert.False(KeyProtectionOptions.From(empty).IsEncryptionConfigured);
        Assert.False(KeyProtectionOptions.From(new ConfigurationBuilder().Build()).IsEncryptionConfigured);
    }
}
