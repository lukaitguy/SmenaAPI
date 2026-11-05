using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Smena.Infrastructure.Push;

/// <summary>
/// Web push po standardima koje podržavaju svi browseri, uključujući Safari na iPhone-u: poruka se šifruje po
/// RFC 8291 (<c>aes128gcm</c>), a pošiljalac se predstavlja VAPID potpisom po RFC 8292 (<c>vapid t=…, k=…</c>).
/// Starije šeme (<c>aesgcm</c>, <c>WebPush &lt;jwt&gt;</c>) Apple ne prima, pa se ne koriste.
/// </summary>
public static class WebPushProtocol
{
    /// <summary>Najveća veličina jednog zapisa; poruka mora da stane u njega, push servisi odbijaju veće.</summary>
    public const int RecordSize = 4096;

    private const int HeaderOverhead = 16 + 4 + 1 + 65;   // salt + rs + idlen + ključ pošiljaoca
    private const int TagLength = 16;

    public static int MaxPayloadBytes => RecordSize - HeaderOverhead - TagLength - 1;

    public static byte[] Decode(string base64Url) => WebEncoders.Base64UrlDecode(base64Url);

    public static string Encode(ReadOnlySpan<byte> bytes) => WebEncoders.Base64UrlEncode(bytes);

    /// <summary>Šifruje poruku za jedan browser; ključ pošiljaoca i so su novi za svaku poruku.</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> payload, byte[] browserPublicKey, byte[] authSecret)
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(payload, browserPublicKey, authSecret, ephemeral, RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>Isto, sa zadatim ključem i soli (radi provere sa uzorcima iz RFC-a i nezavisnim dekriptorom).</summary>
    public static byte[] Encrypt(
        ReadOnlySpan<byte> payload, byte[] browserPublicKey, byte[] authSecret, ECDiffieHellman senderKey, byte[] salt)
    {
        if (browserPublicKey.Length != 65 || browserPublicKey[0] != 4)
            throw new ArgumentException("Javni ključ browsera mora biti nekompresovana tačka od 65 bajtova.", nameof(browserPublicKey));
        if (authSecret.Length != 16)
            throw new ArgumentException("Tajna pretplate mora imati 16 bajtova.", nameof(authSecret));
        if (payload.Length > MaxPayloadBytes)
            throw new ArgumentException($"Poruka je prevelika: najviše {MaxPayloadBytes} bajtova.", nameof(payload));

        var senderPublic = PublicPoint(senderKey);

        using var browser = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = browserPublicKey[1..33], Y = browserPublicKey[33..65] },
        });
        var shared = senderKey.DeriveRawSecretAgreement(browser.PublicKey);

        // RFC 8291, odeljak 3.4
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, shared, authSecret);
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), browserPublicKey, senderPublic);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);

        // RFC 8188
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        // Jedan (poslednji) zapis: poruka pa graničnik 0x02.
        var plaintext = new byte[payload.Length + 1];
        payload.CopyTo(plaintext);
        plaintext[^1] = 0x02;

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm(contentKey, TagLength))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var header = new byte[HeaderOverhead];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), RecordSize);
        header[20] = (byte)senderPublic.Length;
        senderPublic.CopyTo(header, 21);

        return Concat(header, ciphertext, tag);
    }

    /// <summary>VAPID zaglavlje <c>Authorization</c>: potpisan token za servis kome se šalje (po adresi), važi 12 sati.</summary>
    public static string VapidAuthorization(string endpoint, string subject, string publicKey, string privateKey, DateTimeOffset now)
    {
        var audience = new Uri(endpoint).GetLeftPart(UriPartial.Authority);
        var header = Encode(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
        var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new { aud = audience, exp = now.AddHours(12).ToUnixTimeSeconds(), sub = subject }));
        var signingInput = $"{header}.{claims}";

        using var key = SigningKey(Decode(publicKey), Decode(privateKey));
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"vapid t={signingInput}.{Encode(signature)}, k={publicKey}";
    }

    private static ECDsa SigningKey(byte[] publicKey, byte[] privateKey)
    {
        if (publicKey.Length != 65 || publicKey[0] != 4 || privateKey.Length != 32)
            throw new ArgumentException("VAPID ključevi nisu ispravni (javni 65 bajtova, privatni 32).");

        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateKey,
            Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] },
        });
    }

    private static byte[] PublicPoint(ECDiffieHellman key)
    {
        var parameters = key.ExportParameters(false);
        return Concat([4], parameters.Q.X!, parameters.Q.Y!);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}
